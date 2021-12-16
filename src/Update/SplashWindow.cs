using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.ShowWindowCommand;
using static Vanara.PInvoke.User32;
using static Vanara.PInvoke.User32.MonitorFlags;
using static Vanara.PInvoke.User32.SetWindowPosFlags;
using static Vanara.PInvoke.User32.SPI;
using static Vanara.PInvoke.User32.WindowClassStyles;
using static Vanara.PInvoke.User32.WindowMessage;
using static Vanara.PInvoke.User32.WindowStyles;
using static Vanara.PInvoke.User32.WindowStylesEx;
using POINT = System.Drawing.Point;

namespace Squirrel.Update
{
    internal unsafe class SplashWindow
    {
        public IntPtr Handle => _hwnd != null ? _hwnd.DangerousGetHandle() : IntPtr.Zero;

        private SafeHWND _hwnd;
        private Exception _error;
        private Thread _thread;
        private uint _threadId;
        private POINT _ptMouseDown;
        private ITaskbarList3 _taskbarList;
        private double _progress;

        private readonly ManualResetEvent _signal;
        private readonly Bitmap _img;
        private readonly Icon _icon;

        private const int OPERATION_TIMEOUT = 5000;
        private const string WINDOW_CLASS_NAME = "SquirrelSplashWindow";

        public SplashWindow(Icon icon, Bitmap splashImg)
        {
            _icon = icon;
            _img = splashImg;
            _signal = new ManualResetEvent(false);
            _taskbarList = (ITaskbarList3) new CTaskbarList();
            _taskbarList.HrInit();
        }

        public void Show()
        {
            if (_thread == null) {
                _error = null;
                _signal.Reset();
                _thread = new Thread(ThreadProc);
                _thread.IsBackground = true;
                _thread.Start();
                if (!_signal.WaitOne(OPERATION_TIMEOUT)) {
                    if (_error != null) throw _error;
                    else throw new Exception("Timeout waiting for splash window to open");
                }
                if (_error != null) throw _error;
            } else {
                ShowWindow(_hwnd, SW_SHOW);
            }
        }

        public void Hide()
        {
            if (_thread == null) return;
            ShowWindow(_hwnd, SW_HIDE);
        }

        public void SetNoProgress()
        {
            if (_thread == null) return;
            var h = _hwnd.DangerousGetHandle();
            _taskbarList.SetProgressState(h, ThumbnailProgressState.NoProgress);
            _progress = 0;
            InvalidateRect(_hwnd, null, false);
        }

        public void SetProgressIndeterminate()
        {
            if (_thread == null) return;
            var h = _hwnd.DangerousGetHandle();
            _taskbarList.SetProgressState(h, ThumbnailProgressState.Indeterminate);
            _progress = 0;
            InvalidateRect(_hwnd, null, false);
        }

        public void SetProgress(ulong completed, ulong total)
        {
            if (_thread == null) return;
            var h = _hwnd.DangerousGetHandle();
            _taskbarList.SetProgressState(h, ThumbnailProgressState.Normal);
            _taskbarList.SetProgressValue(h, completed, total);
            _progress = completed / (double) total;
            InvalidateRect(_hwnd, null, false);
        }

        public void Close()
        {
            if (_thread == null) return;
            PostThreadMessage(_threadId, (uint) WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(OPERATION_TIMEOUT);
            _thread = null;
            _error = null;
            _threadId = 0;
            _hwnd = null;
            _signal.Reset();
        }

        private void ThreadProc()
        {
            try {
                _threadId = GetCurrentThreadId();
                CreateWindow();
            } catch (Exception ex) {
                _error = ex;
                _signal.Set();
            }
        }

        private void CreateWindow()
        {
            int imgWidth = _img.Width;
            int imgHeight = _img.Height;

            var instance = GetModuleHandle(null);

            WNDCLASS wndClass = new WNDCLASS {
                style = CS_HREDRAW | CS_VREDRAW,
                lpfnWndProc = WndProc,
                hInstance = instance,
                //hbrBackground = COLOR_WINDOW
                hCursor = LoadCursor(HINSTANCE.NULL, IDC_APPSTARTING),
                lpszClassName = WINDOW_CLASS_NAME,
                hIcon = _icon != null ? new HICON(_icon.Handle) : LoadIcon(instance, IDI_APPLICATION),
            };

            if (RegisterClass(wndClass) == 0) {
                var clhr = GetLastError();
                if (clhr != 0x00000582) // already registered
                    throw clhr.GetException("Unable to register splash window class");
            }

            // try to find monitor where mouse is
            GetCursorPos(out var point);
            var hMonitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
            MONITORINFO mi = new MONITORINFO { cbSize = 40 /*sizeof(MONITORINFO)*/ };
            RECT rcArea = default;

            if (GetMonitorInfo(hMonitor, ref mi)) {
                rcArea.left = (mi.rcMonitor.right + mi.rcMonitor.left - imgWidth) / 2;
                rcArea.top = (mi.rcMonitor.top + mi.rcMonitor.bottom - imgHeight) / 2;
            } else {
                SystemParametersInfo(SPI_GETWORKAREA, 0, new IntPtr(&rcArea), 0);
                rcArea.left = (rcArea.right + rcArea.left - imgWidth) / 2;
                rcArea.top = (rcArea.top + rcArea.bottom - imgHeight) / 2;
            }

            _hwnd = CreateWindowEx(
                /*WS_EX_TOOLWINDOW |*/ WS_EX_TOPMOST,
                WINDOW_CLASS_NAME,
                "Setup",
                WS_CLIPCHILDREN | WS_POPUP,
                rcArea.left, rcArea.top, imgWidth, imgHeight,
                HWND.NULL,
                HMENU.NULL,
                instance,
                IntPtr.Zero);

            if (_hwnd.IsInvalid) {
                throw new Win32Exception();
            }

            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);

            // check for animation properties
            var pDimensionIDs = _img.FrameDimensionsList;
            var frameDimension = new FrameDimension(pDimensionIDs[0]);
            var frameCount = _img.GetFrameCount(frameDimension);
            var delayProperty = _img.GetPropertyItem(0x5100 /*PropertyTagFrameDelay*/);

            ManualResetEvent exitGif = new ManualResetEvent(false);
            Thread gif = new Thread(() => {
                fixed (byte* frameDelayBytes = delayProperty.Value) {
                    int framePosition = 0;
                    int* frameDelays = (int*) frameDelayBytes;
                    while (true) {

                        lock (_img) _img.SelectActiveFrame(frameDimension, framePosition++);
                        InvalidateRect(_hwnd, null, false);

                        if (framePosition == frameCount)
                            framePosition = 0;

                        int lPause = frameDelays[framePosition] * 10;
                        if (exitGif.WaitOne(lPause))
                            return;
                    }
                }
            });

            // start gif animation
            if (frameCount > 1 && delayProperty?.Value != null && (delayProperty.Value.Length / 4) >= frameCount) {
                gif.IsBackground = true;
                gif.Start();
            }

            MSG msg;
            PeekMessage(out msg, _hwnd, 0, 0, 0); // invoke creating message queue

            _signal.Set(); // signal to calling thread that the window has been created

            bool bRet;
            while ((bRet = GetMessage(out msg, HWND.NULL, 0, 0)) != false) {
                if (msg.message == (uint) WM_QUIT)
                    break;

                TranslateMessage(msg);
                DispatchMessage(msg);
            }

            exitGif.Set();
            gif.Join(1000);
            DestroyWindow(_hwnd);
        }

        private nint WndProc(HWND hwnd, uint uMsg, nint wParam, nint lParam)
        {
            switch (uMsg) {

            case (uint) WM_PAINT:
                GetWindowRect(hwnd, out var r);
                using (var buffer = new Bitmap(r.Width, r.Height))
                using (var brush = new SolidBrush(Color.FromArgb(160, Color.LimeGreen)))
                using (var g = Graphics.FromImage(buffer))
                using (var wnd = Graphics.FromHwnd(hwnd.DangerousGetHandle())) {
                    lock (_img) g.DrawImage(_img, 0, 0);
                    if (_progress > 0) {
                        g.FillRectangle(brush, new Rectangle(0, r.Height - 10, (int) (r.Width * _progress), 10));
                    }
                    wnd.DrawImage(buffer, 0, 0);
                }

                ValidateRect(hwnd, null);
                return 0;

            case (uint) WM_LBUTTONDOWN:
                GetCursorPos(out _ptMouseDown);
                SetCapture(hwnd);
                return 0;

            case (uint) WM_MOUSEMOVE:
                if (GetCapture() == hwnd) {
                    GetWindowRect(hwnd, out var rcWnd);
                    GetCursorPos(out var pt);

                    POINT ptDown = _ptMouseDown;
                    var xdiff = ptDown.X - pt.X;
                    var ydiff = ptDown.Y - pt.Y;

                    SetWindowPos(hwnd, HWND.HWND_TOP, rcWnd.left - xdiff, rcWnd.top - ydiff, 0, 0,
                        SWP_NOACTIVATE | SWP_NOSIZE | SWP_NOZORDER);

                    _ptMouseDown = pt;
                }
                return 0;

            case (uint) WM_LBUTTONUP:
                ReleaseCapture();
                return 0;

            }

            return DefWindowProc(hwnd, uMsg, wParam, lParam);
        }

        [ComImportAttribute()]
        [GuidAttribute("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
        [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface ITaskbarList3
        {
            // ITaskbarList
            [PreserveSig]
            void HrInit();
            [PreserveSig]
            void AddTab(IntPtr hwnd);
            [PreserveSig]
            void DeleteTab(IntPtr hwnd);
            [PreserveSig]
            void ActivateTab(IntPtr hwnd);
            [PreserveSig]
            void SetActiveAlt(IntPtr hwnd);

            // ITaskbarList2
            [PreserveSig]
            void MarkFullscreenWindow(
              IntPtr hwnd,
              [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

            // ITaskbarList3
            void SetProgressValue(IntPtr hwnd, UInt64 ullCompleted, UInt64 ullTotal);
            void SetProgressState(IntPtr hwnd, ThumbnailProgressState tbpFlags);
        }

        [GuidAttribute("56FDF344-FD6D-11d0-958A-006097C9A090")]
        [ClassInterfaceAttribute(ClassInterfaceType.None)]
        [ComImportAttribute()]
        internal class CTaskbarList { }

        public enum ThumbnailProgressState
        {
            /// <summary>
            /// No progress is displayed.
            /// </summary>
            NoProgress = 0,
            /// <summary>
            /// The progress is indeterminate (marquee).
            /// </summary>
            Indeterminate = 0x1,
            /// <summary>
            /// Normal progress is displayed.
            /// </summary>
            Normal = 0x2,
            /// <summary>
            /// An error occurred (red).
            /// </summary>
            Error = 0x4,
            /// <summary>
            /// The operation is paused (yellow).
            /// </summary>
            Paused = 0x8
        }
    }
}
