using System;
using System.Runtime.InteropServices;
using System.Text;

// from clowd-windows/Clowd.PlatformUtil/Windows/User32MessageBox.cs

namespace Squirrel.Update.Windows
{
    internal static class User32MessageBox
    {
        public static MessageBoxResult Show(
            IntPtr owner,
            string messageBoxText,
            string caption,
            MessageBoxButtons button,
            MessageBoxIcon icon, MessageBoxResult defaultResult,
            MessageBoxOptions options)
        {
            return ShowCore(owner, messageBoxText, caption, button, icon, defaultResult, options);
        }

        public static MessageBoxResult Show(
            IntPtr owner,
            string messageBoxText,
            string caption,
            MessageBoxButtons button,
            MessageBoxIcon icon,
            MessageBoxResult defaultResult)
        {
            return ShowCore(owner, messageBoxText, caption, button, icon, defaultResult, 0);
        }

        public static MessageBoxResult Show(
            IntPtr owner,
            string messageBoxText,
            string caption,
            MessageBoxButtons button,
            MessageBoxIcon icon)
        {
            return ShowCore(owner, messageBoxText, caption, button, icon, 0, 0);
        }

        public static MessageBoxResult Show(
            IntPtr owner,
            string messageBoxText,
            string caption,
            MessageBoxButtons button)
        {
            return ShowCore(owner, messageBoxText, caption, button, MessageBoxIcon.None, 0, 0);
        }

        public static MessageBoxResult Show(IntPtr owner, string messageBoxText, string caption)
        {
            return ShowCore(owner, messageBoxText, caption, MessageBoxButtons.OK, MessageBoxIcon.None, 0, 0);
        }

        public static MessageBoxResult Show(IntPtr owner, string messageBoxText)
        {
            return ShowCore(owner, messageBoxText, String.Empty, MessageBoxButtons.OK, MessageBoxIcon.None, 0, 0);
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int MessageBox(IntPtr hWnd, string lpText, string lpCaption, MB_FLAGS uType);

        internal static MessageBoxResult ShowCore(
            IntPtr owner,
            string messageBoxText,
            string caption,
            MessageBoxButtons button,
            MessageBoxIcon icon,
            MessageBoxResult defaultResult,
            MessageBoxOptions options)
        {

            // for positioning message box and custom button labels, see here
            // https://www.codeguru.com/cpp/w-p/win32/messagebox/article.php/c10873/MessageBox-with-Custom-Button-Captions.htm
            // need a CBT hook
            // https://stackoverflow.com/questions/1530561/set-location-of-messagebox

            if ((options & (MessageBoxOptions.ServiceNotification | MessageBoxOptions.DefaultDesktopOnly)) != 0) {
                if (owner != IntPtr.Zero) {
                    throw new ArgumentException("Can't show a service notification with an owner.");
                }
            }

            MB_FLAGS style = (MB_FLAGS) button | (MB_FLAGS) icon | DefaultResultToButtonNumber(defaultResult, button) | (MB_FLAGS) options;
            return (MessageBoxResult) MessageBox(owner, messageBoxText, caption, style);
        }

        private static MB_FLAGS DefaultResultToButtonNumber(MessageBoxResult result, MessageBoxButtons button)
        {
            if (result == 0)
                return MB_FLAGS.MB_DEFBUTTON1;

            switch (button) {
            case MessageBoxButtons.OK:
                return MB_FLAGS.MB_DEFBUTTON1;
            case MessageBoxButtons.OKCancel:
                if (result == MessageBoxResult.Cancel)
                    return MB_FLAGS.MB_DEFBUTTON2;
                return MB_FLAGS.MB_DEFBUTTON1;
            case MessageBoxButtons.YesNo:
                if (result == MessageBoxResult.No)
                    return MB_FLAGS.MB_DEFBUTTON2;
                return MB_FLAGS.MB_DEFBUTTON1;
            case MessageBoxButtons.YesNoCancel:
                if (result == MessageBoxResult.No)
                    return MB_FLAGS.MB_DEFBUTTON2;
                if (result == MessageBoxResult.Cancel)
                    return MB_FLAGS.MB_DEFBUTTON3;
                return MB_FLAGS.MB_DEFBUTTON1;
            default:
                return MB_FLAGS.MB_DEFBUTTON1;
            }
        }

        public enum MessageBoxResult
        {
            None = 0,
            OK = 1,
            Cancel = 2,
            Abort = 3,
            Retry = 4,
            Ignore = 5,
            Yes = 6,
            No = 7,
            TryAgain = 10,
        }

        public enum MessageBoxIcon
        {
            None = 0,
            Hand = 0x00000010,
            Question = 0x00000020,
            Exclamation = 0x00000030,
            Asterisk = 0x00000040,
            Stop = Hand,
            Error = Hand,
            Warning = Exclamation,
            Information = Asterisk,
        }

        public enum MessageBoxButtons
        {
            OK = 0x00000000,
            OKCancel = 0x00000001,
            AbortRetryIgnore = 0x00000002,
            YesNoCancel = 0x00000003,
            YesNo = 0x00000004,
            RetryCancel = 0x00000005,
        }

        [Flags]
        public enum MessageBoxOptions
        {
            None = 0x00000000,
            ServiceNotification = 0x00200000,
            DefaultDesktopOnly = 0x00020000,
            RightAlign = 0x00080000,
            RtlReading = 0x00100000,
        }

        [Flags]
        private enum MB_FLAGS
        {
            /// <summary>The message box contains three push buttons: Abort, Retry, and Ignore.</summary>
            MB_ABORTRETRYIGNORE = 0x00000002,

            /// <summary>
            /// The message box contains three push buttons: Cance, Try Again, Continue. Use this message box type instead of MB_ABORTRETRYIGNORE.
            /// </summary>
            MB_CANCELTRYCONTINUE = 0x00000006,

            /// <summary>
            /// Adds a Help button to the message box. When the user clicks the Help button or presses F1, the system sends a WM_HELP message
            /// to the owner.
            /// </summary>
            MB_HELP = 0x00004000,

            /// <summary>The message box contains one push button: OK. This is the default.</summary>
            MB_OK = 0x00000000,

            /// <summary>The message box contains two push buttons: OK and Cancel.</summary>
            MB_OKCANCEL = 0x00000001,

            /// <summary>The message box contains two push buttons: Retry and Cancel.</summary>
            MB_RETRYCANCEL = 0x00000005,

            /// <summary>The message box contains two push buttons: Yes and No.</summary>
            MB_YESNO = 0x00000004,

            /// <summary>The message box contains three push buttons: Yes, No, and Cancel.</summary>
            MB_YESNOCANCEL = 0x00000003,

            /// <summary>An exclamation-point icon appears in the message box.</summary>
            MB_ICONEXCLAMATION = 0x00000030,

            /// <summary>An exclamation-point icon appears in the message box.</summary>
            MB_ICONWARNING = 0x00000030,

            /// <summary>An icon consisting of a lowercase letter i in a circle appears in the message box.</summary>
            MB_ICONINFORMATION = 0x00000040,

            /// <summary>An icon consisting of a lowercase letter i in a circle appears in the message box.</summary>
            MB_ICONASTERISK = 0x00000040,

            /// <summary>
            /// A question-mark icon appears in the message box. The question-mark message icon is no longer recommended because it does not
            /// clearly represent a specific type of message and because the phrasing of a message as a question could apply to any message
            /// type. In addition, users can confuse the message symbol question mark with Help information. Therefore, do not use this
            /// question mark message symbol in your message boxes. The system continues to support its inclusion only for backward compatibility.
            /// </summary>
            MB_ICONQUESTION = 0x00000020,

            /// <summary>A stop-sign icon appears in the message box.</summary>
            MB_ICONSTOP = 0x00000010,

            /// <summary>A stop-sign icon appears in the message box.</summary>
            MB_ICONERROR = 0x00000010,

            /// <summary>A stop-sign icon appears in the message box.</summary>
            MB_ICONHAND = 0x00000010,

            /// <summary>
            /// The first button is the default button.
            /// <para>MB_DEFBUTTON1 is the default unless MB_DEFBUTTON2, MB_DEFBUTTON3, or MB_DEFBUTTON4 is specified.</para>
            /// </summary>
            MB_DEFBUTTON1 = 0x00000000,

            /// <summary>The second button is the default button.</summary>
            MB_DEFBUTTON2 = 0x00000100,

            /// <summary>The third button is the default button.</summary>
            MB_DEFBUTTON3 = 0x00000200,

            /// <summary>The fourth button is the default button.</summary>
            MB_DEFBUTTON4 = 0x00000300,

            /// <summary>
            /// The user must respond to the message box before continuing work in the window identified by the hWnd parameter. However, the
            /// user can move to the windows of other threads and work in those windows.
            /// <para>
            /// Depending on the hierarchy of windows in the application, the user may be able to move to other windows within the thread.
            /// All child windows of the parent of the message box are automatically disabled, but pop-up windows are not.
            /// </para>
            /// <para>MB_APPLMODAL is the default if neither MB_SYSTEMMODAL nor MB_TASKMODAL is specified.</para>
            /// </summary>
            MB_APPLMODAL = 0x00000000,

            /// <summary>
            /// Same as MB_APPLMODAL except that the message box has the WS_EX_TOPMOST style. Use system-modal message boxes to notify the
            /// user of serious, potentially damaging errors that require immediate attention (for example, running out of memory). This flag
            /// has no effect on the user's ability to interact with windows other than those associated with hWnd.
            /// </summary>
            MB_SYSTEMMODAL = 0x00001000,

            /// <summary>
            /// Same as MB_APPLMODAL except that all the top-level windows belonging to the current thread are disabled if the hWnd parameter
            /// is NULL. Use this flag when the calling application or library does not have a window handle available but still needs to
            /// prevent input to other windows in the calling thread without suspending other threads.
            /// </summary>
            MB_TASKMODAL = 0x00002000,

            /// <summary>
            /// Same as desktop of the interactive window station. For more information, see Window Stations.
            /// <para>
            /// If the current input desktop is not the default desktop, MessageBox does not return until the user switches to the default desktop.
            /// </para>
            /// </summary>
            MB_DEFAULT_DESKTOP_ONLY = 0x00020000,

            /// <summary>The text is right-justified.</summary>
            MB_RIGHT = 0x00080000,

            /// <summary>Displays message and caption text using right-to-left reading order on Hebrew and Arabic systems.</summary>
            MB_RTLREADING = 0x00100000,

            /// <summary>
            /// The message box becomes the foreground window. Internally, the system calls the SetForegroundWindow function for the message box.
            /// </summary>
            MB_SETFOREGROUND = 0x00010000,

            /// <summary>The message box is created with the WS_EX_TOPMOST window style.</summary>
            MB_TOPMOST = 0x00040000,

            /// <summary>
            /// The caller is a service notifying the user of an event. The function displays a message box on the current active desktop,
            /// even if there is no user logged on to the computer.
            /// <para>
            /// Terminal Services: If the calling thread has an impersonation token, the function directs the message box to the session
            /// specified in the impersonation token.
            /// </para>
            /// <para>
            /// If this flag is set, the hWnd parameter must be NULL. This is so that the message box can appear on a desktop other than the
            /// desktop corresponding to the hWnd.
            /// </para>
            /// <para>
            /// For information on security considerations in regard to using this flag, see Interactive Services. In particular, be aware
            /// that this flag can produce interactive content on a locked desktop and should therefore be used for only a very limited set
            /// of scenarios, such as resource exhaustion.
            /// </para>
            /// </summary>
            MB_SERVICE_NOTIFICATION = 0x00200000,
        }
    }

    //internal class DialogCenteringService : IDisposable
    //{
    //    private readonly IntPtr owner;
    //    private readonly HookProc hookProc;
    //    private readonly IntPtr hHook = IntPtr.Zero;

    //    public DialogCenteringService(IntPtr owner)
    //    {
    //        if (owner == null) throw new ArgumentNullException("owner");

    //        this.owner = owner;
    //        hookProc = DialogHookProc;

    //        hHook = SetWindowsHookEx(WH_CALLWNDPROCRET, hookProc, IntPtr.Zero, GetCurrentThreadId());
    //    }

    //    private IntPtr DialogHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    //    {
    //        if (nCode < 0) {
    //            return CallNextHookEx(hHook, nCode, wParam, lParam);
    //        }

    //        CWPRETSTRUCT msg = (CWPRETSTRUCT) Marshal.PtrToStructure(lParam, typeof(CWPRETSTRUCT));
    //        IntPtr hook = hHook;

    //        var test = (CbtHookAction) msg.message;
    //        Rectangle recChild = new Rectangle(0, 0, 0, 0);
    //        bool success = GetWindowRect(msg.hwnd, ref recChild);


    //        if (msg.message == (int) CbtHookAction.HCBT_ACTIVATE) {
    //            try {
    //                CenterWindow(msg.hwnd);
    //            } finally {
    //                //UnhookWindowsHookEx(hHook);
    //            }
    //        }

    //        return CallNextHookEx(hook, nCode, wParam, lParam);
    //    }

    //    public void Dispose()
    //    {
    //        UnhookWindowsHookEx(hHook);
    //    }

    //    private void CenterWindow(IntPtr hChildWnd)
    //    {
    //        Rectangle recChild = new Rectangle(0, 0, 0, 0);
    //        bool success = GetWindowRect(hChildWnd, ref recChild);

    //        if (!success) {
    //            return;
    //        }

    //        int width = recChild.Width - recChild.X;
    //        int height = recChild.Height - recChild.Y;

    //        Rectangle recParent = new Rectangle(0, 0, 0, 0);
    //        success = GetWindowRect(owner, ref recParent);

    //        if (!success) {
    //            return;
    //        }

    //        Point ptCenter = new Point(0, 0);
    //        ptCenter.X = recParent.X + ((recParent.Width - recParent.X) / 2);
    //        ptCenter.Y = recParent.Y + ((recParent.Height - recParent.Y) / 2);


    //        Point ptStart = new Point(0, 0);
    //        ptStart.X = (ptCenter.X - (width / 2));
    //        ptStart.Y = (ptCenter.Y - (height / 2));

    //        SetWindowPos(hChildWnd, (IntPtr) 0, ptStart.X, ptStart.Y, width, height, SetWindowPosFlags.SWP_ASYNCWINDOWPOS | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_NOOWNERZORDER | SetWindowPosFlags.SWP_NOZORDER);
    //    }

    //    // some p/invoke

    //    private struct Point
    //    {
    //        public int X;
    //        public int Y;
    //        public Point(int x, int y)
    //        {
    //            this.X = x;
    //            this.Y = y;
    //        }
    //    }

    //    private struct Rectangle
    //    {
    //        public int left;
    //        public int top;
    //        public int right;
    //        public int bottom;

    //        public int X {
    //            get => left;
    //            set {
    //                right -= (left - value);
    //                left = value;
    //            }
    //        }

    //        public int Y {
    //            get => top;
    //            set {
    //                bottom -= (top - value);
    //                top = value;
    //            }
    //        }

    //        public int Height {
    //            get => bottom - top;
    //            set => bottom = value + top;
    //        }

    //        public int Width {
    //            get => right - left;
    //            set => right = value + left;
    //        }

    //        public Rectangle(int left, int top, int right, int bottom)
    //        {
    //            this.left = left;
    //            this.top = top;
    //            this.right = right;
    //            this.bottom = bottom;
    //        }
    //    }

    //    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    //    public delegate void TimerProc(IntPtr hWnd, uint uMsg, UIntPtr nIDEvent, uint dwTime);

    //    private const int WH_CALLWNDPROCRET = 12;

    //    private enum CbtHookAction : int
    //    {
    //        HCBT_MOVESIZE = 0,
    //        HCBT_MINMAX = 1,
    //        HCBT_QS = 2,
    //        HCBT_CREATEWND = 3,
    //        HCBT_DESTROYWND = 4,
    //        HCBT_ACTIVATE = 5,
    //        HCBT_CLICKSKIPPED = 6,
    //        HCBT_KEYSKIPPED = 7,
    //        HCBT_SYSCOMMAND = 8,
    //        HCBT_SETFOCUS = 9
    //    }

    //    [DllImport("kernel32.dll")]
    //    static extern int GetCurrentThreadId();

    //    [DllImport("user32.dll")]
    //    private static extern bool GetWindowRect(IntPtr hWnd, ref Rectangle lpRect);

    //    [DllImport("user32.dll")]
    //    private static extern bool IsChild(IntPtr hWnd, ref Rectangle lpRect);

    //    [DllImport("user32.dll")]
    //    private static extern int MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    //    [DllImport("user32.dll")]
    //    [return: MarshalAs(UnmanagedType.Bool)]
    //    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, SetWindowPosFlags uFlags);

    //    [DllImport("User32.dll")]
    //    public static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, TimerProc lpTimerFunc);

    //    [DllImport("User32.dll")]
    //    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    //    [DllImport("user32.dll")]
    //    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hInstance, int threadId);

    //    [DllImport("user32.dll")]
    //    public static extern int UnhookWindowsHookEx(IntPtr idHook);

    //    [DllImport("user32.dll")]
    //    public static extern IntPtr CallNextHookEx(IntPtr idHook, int nCode, IntPtr wParam, IntPtr lParam);

    //    [DllImport("user32.dll")]
    //    public static extern int GetWindowTextLength(IntPtr hWnd);

    //    [DllImport("user32.dll")]
    //    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxLength);

    //    [DllImport("user32.dll")]
    //    public static extern int EndDialog(IntPtr hDlg, IntPtr nResult);

    //    [StructLayout(LayoutKind.Sequential)]
    //    public struct CWPRETSTRUCT
    //    {
    //        public IntPtr lResult;
    //        public IntPtr lParam;
    //        public IntPtr wParam;
    //        public uint message;
    //        public IntPtr hwnd;
    //    };

    //    [Flags]
    //    public enum SetWindowPosFlags : uint
    //    {
    //        /// <summary>
    //        ///     If the calling thread and the thread that owns the window are attached to different input queues, the system posts the request to the thread that owns the window. This prevents the calling thread from blocking its execution while other threads process the request.
    //        /// </summary>
    //        SWP_ASYNCWINDOWPOS = 0x4000,

    //        /// <summary>
    //        ///     Prevents generation of the WM_SYNCPAINT message.
    //        /// </summary>
    //        SWP_DEFERERASE = 0x2000,

    //        /// <summary>
    //        ///     Draws a frame (defined in the window's class description) around the window.
    //        /// </summary>
    //        SWP_DRAWFRAME = 0x0020,

    //        /// <summary>
    //        ///     Applies new frame styles set using the SetWindowLong function. Sends a WM_NCCALCSIZE message to the window, even if the window's size is not being changed. If this flag is not specified, WM_NCCALCSIZE is sent only when the window's size is being changed.
    //        /// </summary>
    //        SWP_FRAMECHANGED = 0x0020,

    //        /// <summary>
    //        ///     Hides the window.
    //        /// </summary>
    //        SWP_HIDEWINDOW = 0x0080,

    //        /// <summary>
    //        ///     Does not activate the window. If this flag is not set, the window is activated and moved to the top of either the topmost or non-topmost group (depending on the setting of the hWndInsertAfter parameter).
    //        /// </summary>
    //        SWP_NOACTIVATE = 0x0010,

    //        /// <summary>
    //        ///     Discards the entire contents of the client area. If this flag is not specified, the valid contents of the client area are saved and copied back into the client area after the window is sized or repositioned.
    //        /// </summary>
    //        SWP_NOCOPYBITS = 0x0100,

    //        /// <summary>
    //        ///     Retains the current position (ignores X and Y parameters).
    //        /// </summary>
    //        SWP_NOMOVE = 0x0002,

    //        /// <summary>
    //        ///     Does not change the owner window's position in the Z order.
    //        /// </summary>
    //        SWP_NOOWNERZORDER = 0x0200,

    //        /// <summary>
    //        ///     Does not redraw changes. If this flag is set, no repainting of any kind occurs. This applies to the client area, the nonclient area (including the title bar and scroll bars), and any part of the parent window uncovered as a result of the window being moved. When this flag is set, the application must explicitly invalidate or redraw any parts of the window and parent window that need redrawing.
    //        /// </summary>
    //        SWP_NOREDRAW = 0x0008,

    //        /// <summary>
    //        ///     Same as the SWP_NOOWNERZORDER flag.
    //        /// </summary>
    //        SWP_NOREPOSITION = 0x0200,

    //        /// <summary>
    //        ///     Prevents the window from receiving the WM_WINDOWPOSCHANGING message.
    //        /// </summary>
    //        SWP_NOSENDCHANGING = 0x0400,

    //        /// <summary>
    //        ///     Retains the current size (ignores the cx and cy parameters).
    //        /// </summary>
    //        SWP_NOSIZE = 0x0001,

    //        /// <summary>
    //        ///     Retains the current Z order (ignores the hWndInsertAfter parameter).
    //        /// </summary>
    //        SWP_NOZORDER = 0x0004,

    //        /// <summary>
    //        ///     Displays the window.
    //        /// </summary>
    //        SWP_SHOWWINDOW = 0x0040,
    //    }
    //}
}
