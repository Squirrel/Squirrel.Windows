using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using Squirrel.SimpleSplat;
using Vanara.PInvoke;
using Vanara.Windows.Forms;
using static Vanara.PInvoke.ComCtl32;
using static Vanara.PInvoke.User32;

namespace Squirrel.Update.Windows
{
    internal class ComCtlProgressDialog : WindowBase
    {
        public override IntPtr Handle => _hwnd != HWND.NULL ? _hwnd.DangerousGetHandle() : IntPtr.Zero;
        private HWND _hwnd;
        private readonly Icon _icon;
        private readonly object _lock = new object();
        private readonly ManualResetEvent _signal;
        private bool _marquee = true;
        private bool _closing = false;

        public ComCtlProgressDialog(string appName, Icon icon) : base(appName)
        {
            _signal = new ManualResetEvent(false);
            _icon = icon;
        }

        public override void Hide()
        {
            lock (_lock) {
                if (Handle == IntPtr.Zero) return;
                _closing = true;
                SendMessage(_hwnd, WindowMessage.WM_CLOSE);
            }
        }

        public override void SetProgress(ulong completed, ulong total)
        {
            lock (_lock) {
                if (Handle == IntPtr.Zero) return;
                var progress = (int) Math.Round((double) completed / (double) total * 100);
                if (_marquee) {
                    _marquee = false;
                    SendMessage(_hwnd, (uint) TaskDialogMessage.TDM_SET_PROGRESS_BAR_MARQUEE, (IntPtr) 0, (IntPtr) 0); // turn off marque animation
                    SendMessage(_hwnd, (uint) TaskDialogMessage.TDM_SET_MARQUEE_PROGRESS_BAR, (IntPtr) 0); // disable marque mode
                }
                SendMessage(_hwnd, (uint) TaskDialogMessage.TDM_SET_PROGRESS_BAR_POS, (IntPtr) progress);
            }
        }

        public override void SetProgressIndeterminate()
        {
            lock (_lock) {
                if (Handle == IntPtr.Zero) return;
                if (!_marquee) {
                    _marquee = true;
                    SendMessage(_hwnd, (uint) TaskDialogMessage.TDM_SET_PROGRESS_BAR_POS, (IntPtr) 0); // clear current progress bar
                    SendMessage(_hwnd, (uint) TaskDialogMessage.TDM_SET_MARQUEE_PROGRESS_BAR, (IntPtr) 1); // enable marque mode
                    SendMessage(_hwnd, (uint) TaskDialogMessage.TDM_SET_PROGRESS_BAR_MARQUEE, (IntPtr) 1, (IntPtr) 0); // turn on marque animation
                }
            }
        }

        public override unsafe void SetMessage(string message)
        {
            lock (_lock) {
                if (Handle == IntPtr.Zero) return;
                if (String.IsNullOrWhiteSpace(message)) {
                    var arr = stackalloc byte[2];
                    SendMessage(_hwnd, (uint) TaskDialogMessage.TDM_SET_ELEMENT_TEXT, (IntPtr) 0, (IntPtr) arr);
                } else {
                    var hg = Marshal.StringToHGlobalUni(message);
                    try {
                        SendMessage(_hwnd, (uint) TaskDialogMessage.TDM_SET_ELEMENT_TEXT, (IntPtr) 0, hg);
                    } finally {
                        Marshal.FreeHGlobal(hg);
                    }
                }
            }
        }

        public override void Show()
        {
            lock (_lock) {
                if (Handle != IntPtr.Zero) return;
                _signal.Reset();
                _closing = false;
                _marquee = true;
                var t = new Thread(ShowProc);
                t.IsBackground = true;
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                if (!_signal.WaitOne(5000)) {
                    throw new Exception("Timeout waiting for splash window to open");
                }
            }
        }

        private void ShowProc()
        {
            var config = new TASKDIALOGCONFIG();
            config.dwCommonButtons = TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_CANCEL_BUTTON;
            config.MainInstruction = "Installing " + AppName;
            //config.Content = "Hello this is some content";
            config.pfCallbackProc = new TaskDialogCallbackProc(CallbackProc);
            config.dwFlags = TASKDIALOG_FLAGS.TDF_SIZE_TO_CONTENT | TASKDIALOG_FLAGS.TDF_SHOW_PROGRESS_BAR
                | TASKDIALOG_FLAGS.TDF_SHOW_MARQUEE_PROGRESS_BAR;

            if (_icon != null) {
                config.dwFlags |= TASKDIALOG_FLAGS.TDF_USE_HICON_MAIN;
                config.mainIcon = _icon.Handle;
            }

            using (new ComCtl32v6Context()) {
                var hr = TaskDialogIndirect(config, out var b1, out var b2, out var b3);
                if (hr.Failed)
                    this.Log().ErrorException("Failed to open task dialog.", hr.GetException());
            }
        }

        private HRESULT CallbackProc(HWND hwnd, TaskDialogNotification msg, IntPtr wParam, IntPtr lParam, IntPtr refData)
        {
            switch (msg) {

            case TaskDialogNotification.TDN_DIALOG_CONSTRUCTED:
                _hwnd = hwnd;
                // Start marquee animation
                SendMessage(hwnd, (uint) TaskDialogMessage.TDM_SET_PROGRESS_BAR_MARQUEE, (IntPtr) 1, (IntPtr) 0);
                break;

            case TaskDialogNotification.TDN_CREATED:
                _signal.Set();
                break;

            case TaskDialogNotification.TDN_DESTROYED:
                _hwnd = HWND.NULL;
                break;

            case TaskDialogNotification.TDN_BUTTON_CLICKED:
                // TODO support cancellation?
                return _closing ? 0 : 1;

            }

            return HRESULT.S_OK;
        }

        public override void Dispose() { }
    }
}
