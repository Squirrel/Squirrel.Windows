using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Squirrel.SimpleSplat;
using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.Macros;
using static Vanara.PInvoke.SHCore;
using static Vanara.PInvoke.ShowWindowCommand;
using static Vanara.PInvoke.User32;
using static Vanara.PInvoke.User32.HitTestValues;
using static Vanara.PInvoke.User32.MonitorFlags;
using static Vanara.PInvoke.User32.SetWindowPosFlags;
using static Vanara.PInvoke.User32.SPI;
using static Vanara.PInvoke.User32.WindowClassStyles;
using static Vanara.PInvoke.User32.WindowMessage;
using static Vanara.PInvoke.User32.WindowStyles;
using static Vanara.PInvoke.User32.WindowStylesEx;

namespace Squirrel.Update.Windows
{
    internal unsafe class User32SplashWindow : WindowBase
    {
        public override IntPtr Handle => _hwnd != null ? _hwnd.DangerousGetHandle() : IntPtr.Zero;

        private SafeHWND _hwnd;
        private Exception _error;
        private Thread _thread;
        private uint _threadId;
        private ITaskbarList3 _taskbarList3;
        private double _progress;
        private double _uizoom = 1d;

        private readonly ManualResetEvent _signal;
        private readonly Bitmap _img;
        private readonly Icon _icon;

        private const int OPERATION_TIMEOUT = 5000;
        private const string WINDOW_CLASS_NAME = "SquirrelSplashWindow";

        // from gdiplusimaging.h
        private const int PropertyTagFrameDelay = 0x5100;
        private const int PropertyTagPixelUnit = 0x5110;
        private const int PropertyTagPixelPerUnitX = 0x5111;
        private const int PropertyTagPixelPerUnitY = 0x5112;

        public User32SplashWindow(string appName, Icon icon, Bitmap bitmap)
            : base(appName)
        {
            _signal = new ManualResetEvent(false);
            _icon = icon;
            _img = bitmap;

            try {
                var tbl = (ITaskbarList3) new CTaskbarList();
                tbl.HrInit();
                _taskbarList3 = tbl;
            } catch (Exception ex) {
                // failure to load the COM taskbar progress feature should not break this entire window
                this.Log().WarnException("Unable to load ITaskbarList3, progress will not be shown in taskbar", ex);
            }

            _thread = new Thread(ThreadProc);
            _thread.IsBackground = true;
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            if (!_signal.WaitOne(OPERATION_TIMEOUT)) {
                if (_error != null) throw _error;
                else throw new Exception("Timeout waiting for splash window to open");
            }
            if (_error != null) throw _error;

            SetProgressIndeterminate();
        }

        public override void Show()
        {
            if (_thread == null) return;
            ShowWindow(_hwnd, SW_SHOW);
        }

        public override void Hide()
        {
            if (_thread == null) return;
            ShowWindow(_hwnd, SW_HIDE);
        }

        public override void SetProgressIndeterminate()
        {
            if (_thread == null) return;
            var h = _hwnd.DangerousGetHandle();
            _taskbarList3?.SetProgressState(h, ThumbnailProgressState.Indeterminate);
            _progress = 0;
            InvalidateRect(_hwnd, null, false);
        }

        public override void SetProgress(ulong completed, ulong total)
        {
            if (_thread == null) return;
            var h = _hwnd.DangerousGetHandle();
            _taskbarList3?.SetProgressState(h, ThumbnailProgressState.Normal);
            _taskbarList3?.SetProgressValue(h, completed, total);
            _progress = completed / (double) total;
            InvalidateRect(_hwnd, null, false);
        }

        public override void Dispose()
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

        private IDisposable StartGifAnimation()
        {
            // check for animation properties
            ManualResetEvent exitGif = new ManualResetEvent(false);
            Thread gif = null;

            try {
                var pDimensionIDs = _img.FrameDimensionsList;
                var frameDimension = new FrameDimension(pDimensionIDs[0]);
                var frameCount = _img.GetFrameCount(frameDimension);
                this.Log().Info($"There were {frameCount} frames detected in the splash image ({(frameCount > 1 ? "it's animated" : "it's not animated")}).");
                if (frameCount > 1) {
                    var delayProperty = _img.GetPropertyItem(PropertyTagFrameDelay);
                    gif = new Thread(() => {
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
                }
            } catch (Exception e) {
                // errors starting a gif should not break the splash window
                this.Log().ErrorException("Failed to start GIF animation.", e);
            }

            return Disposable.Create(() => {
                exitGif.Set();
                gif?.Join(1000);
            });
        }

        private void ThreadProc()
        {
            try {
                // this is also set in the manifest, but this won't hurt anything and can help if the manifest got replaced with something else.
                ThreadDpiScalingContext.SetCurrentThreadScalingMode(ThreadScalingMode.PerMonitorV2Aware);

                _threadId = GetCurrentThreadId();
                CreateWindow();
            } catch (Exception ex) {
                _error = ex;
                _signal.Set();
            }
        }

        private void CreateWindow()
        {
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

            int x, y, w, h;
            try {
                // try to find monitor where mouse is
                GetCursorPos(out var point);
                var hMonitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
                MONITORINFO mi = new MONITORINFO { cbSize = 40 /*sizeof(MONITORINFO)*/ };
                if (!GetMonitorInfo(hMonitor, ref mi)) throw new Win32Exception();
                GetDpiForMonitor(hMonitor, MONITOR_DPI_TYPE.MDT_DEFAULT, out var dpiX, out var dpiY).ThrowIfFailed();

                // calculate scaling factor for image. If the image does not have embedded dpi information, we default to 96
                double dpiRatioX = dpiX / 96d;
                double dpiRatioY = dpiY / 96d;
                var embeddedDpi = _img.PropertyIdList.Any(p => p == PropertyTagPixelPerUnitX || p == PropertyTagPixelPerUnitY);
                if (embeddedDpi) {
                    dpiRatioX = dpiX / _img.HorizontalResolution;
                    dpiRatioY = dpiY / _img.VerticalResolution;
                }
                _uizoom = dpiX / 96d; // ui ignores image dpi, just takes screen dpi

                // calculate ideal window position & size, adjusted for image DPI and screen DPI
                w = (int) Math.Round(_img.Width * dpiRatioX);
                h = (int) Math.Round(_img.Height * dpiRatioY);
                x = (mi.rcWork.Width - w) / 2;
                y = (mi.rcWork.Height - h) / 2;
                this.Log().Info($"Image dpi is {_img.HorizontalResolution} ({(embeddedDpi ? "embedded" : "default")}), screen dpi is {dpiX}. Rendering image at [{x},{y},{w},{h}]");
            } catch (Exception ex) {
                this.Log().WarnException("Unable to calculate splash dpi scaling", ex);
                RECT rcArea = default;
                SystemParametersInfo(SPI_GETWORKAREA, 0, new IntPtr(&rcArea), 0);
                w = _img.Width;
                h = _img.Height;
                x = (rcArea.Width - w) / 2;
                y = (rcArea.Height - h) / 2;
            }

            _hwnd = CreateWindowEx(
                /*WS_EX_TOOLWINDOW |*/ WS_EX_TOPMOST,
                WINDOW_CLASS_NAME,
                AppName + " Setup",
                WS_CLIPCHILDREN | WS_POPUP,
                x, y, w, h,
                HWND.NULL,
                HMENU.NULL,
                instance,
                IntPtr.Zero);

            if (_hwnd.IsInvalid) {
                throw new Win32Exception();
            }

            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);

            MSG msg;
            PeekMessage(out msg, _hwnd, 0, 0, 0); // invoke creating message queue

            _signal.Set(); // signal to calling thread that the window has been created

            using (StartGifAnimation()) {
                bool bRet;
                while ((bRet = GetMessage(out msg, HWND.NULL, 0, 0)) != false) {
                    if (msg.message == (uint) WM_QUIT)
                        break;

                    TranslateMessage(msg);
                    DispatchMessage(msg);
                }
            }

            DestroyWindow(_hwnd);
        }

        private nint WndProc(HWND hwnd, uint uMsg, nint wParam, nint lParam)
        {
            switch (uMsg) {

            case (uint) WM_PAINT:
                GetWindowRect(hwnd, out var r);
                using (var buffer = new Bitmap(r.Width, r.Height))
                using (var brush = new SolidBrush(Color.FromArgb(190, Color.LimeGreen)))
                using (var g = Graphics.FromImage(buffer))
                using (var wnd = Graphics.FromHwnd(hwnd.DangerousGetHandle())) {
                    // draw image to back buffer
                    lock (_img) g.DrawImage(_img, 0, 0, r.Width, r.Height);
                    if (_progress > 0) {
                        var progressHeight = (int) (8 * _uizoom);
                        g.FillRectangle(brush, new Rectangle(0, r.Height - progressHeight, (int) (r.Width * _progress), progressHeight));
                    }

                    // only should do a single draw operation to the window front buffer to prevent flickering
                    wnd.DrawImage(buffer, 0, 0, r.Width, r.Height);
                }

                ValidateRect(hwnd, null);
                return 0;

            case (uint) WM_DPICHANGED:
                // the window DPI has changed, either because the user has changed their display 
                // settings, or the window is being dragged to a new monitor
                _uizoom = LOWORD(wParam) / 96d;
                var suggestedRect = Marshal.PtrToStructure<RECT>(lParam);
                SetWindowPos(hwnd, HWND.HWND_TOP,
                    suggestedRect.X, suggestedRect.Y, suggestedRect.Width, suggestedRect.Height,
                    SWP_NOACTIVATE | SWP_NOZORDER);
                return 0;

            case (uint) WM_NCHITTEST:
                // any clicks in the client area should register as a click on the title bar so that the 
                // user can drag the window, and it will be properly rescaled when dragged between monitors
                nint hit = DefWindowProc(hwnd, uMsg, wParam, lParam);
                if (hit == (ushort) HTCLIENT)
                    return (ushort) HTCAPTION;
                return hit;

            }

            return DefWindowProc(hwnd, uMsg, wParam, lParam);
        }

        [ComImport]
        [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
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
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

            // ITaskbarList3
            void SetProgressValue(IntPtr hwnd, UInt64 ullCompleted, UInt64 ullTotal);
            void SetProgressState(IntPtr hwnd, ThumbnailProgressState tbpFlags);
        }

        [ComImport]
        [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
        [ClassInterface(ClassInterfaceType.None)]
        private class CTaskbarList { }

        private enum ThumbnailProgressState
        {
            /// <summary> No progress is displayed. </summary>
            NoProgress = 0,
            /// <summary> The progress is indeterminate (marquee). </summary>
            Indeterminate = 0x1,
            /// <summary> Normal progress is displayed. </summary>
            Normal = 0x2,
            /// <summary> An error occurred (red). </summary>
            Error = 0x4,
            /// <summary> The operation is paused (yellow). </summary>
            Paused = 0x8
        }
    }
}
