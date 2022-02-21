using System;
using System.Drawing;
using System.IO;
using Squirrel.SimpleSplat;
using Squirrel.Update.Windows;

namespace Squirrel.Update
{
    internal class ComposedWindow : ISplashWindow
    {
        private readonly Bitmap _img;
        private readonly Icon _icon;
        private ISplashWindow _window;

        public ComposedWindow(string appName, bool silent, byte[] iconBytes, byte[] splashBytes)
        {
            try {
                // we only accept a byte array and convert to memorystream because
                // gdi needs to seek and get length which is not supported in DeflateStream
                if (iconBytes?.Length > 0) _icon = new Icon(new MemoryStream(iconBytes));
                if (splashBytes?.Length > 0) _img = (Bitmap) Bitmap.FromStream(new MemoryStream(splashBytes));
            } catch (Exception ex) {
                this.Log().WarnException("Unable to load splash image", ex);
            }

            // don't bother creating a window if we're in silent mode, we can't show any UI
            try {
                if (!silent) {
                    if (_img != null) {
                        _window = new User32SplashWindow(appName, _icon, _img);
                    } else {
                        _window = new ComCtlProgressDialog(appName, _icon);
                    }
                } else {
                    this.Log().Warn("Running install in SILENT mode, no prompts will be shown to the user. Dialogs will auto-respond as no/cancel.");
                }
            } catch (Exception ex) {
                this.Log().ErrorException("Unable to open splash window", ex);
            }
        }

        public void Dispose()
        {
            LogThrow("Failed to dispose window.", () => _window?.Dispose());
            _img?.Dispose();
            _icon?.Dispose();
        }

        public void Hide()
            => LogThrow("Failed to hide window.", () => _window?.Hide());

        public void SetProgress(ulong completed, ulong total)
            => LogThrow("Failed to set progress.", () => _window?.SetProgress(completed, total));

        public void SetProgressIndeterminate()
            => LogThrow("Failed to set progress indeterminate.", () => _window?.SetProgressIndeterminate());

        public void Show()
            => LogThrow("Failed to show window.", () => _window?.Show());

        public void ShowErrorDialog(string title, string message)
            => LogThrow("Failed to show error dialog.", () => _window?.ShowErrorDialog(title, message));

        public void ShowInfoDialog(string title, string message)
            => LogThrow("Failed to show info dialog.", () => _window?.ShowInfoDialog(title, message));

        public bool ShowQuestionDialog(string title, string message)
            => LogThrow("Failed to show question dialog.", () => _window?.ShowQuestionDialog(title, message) ?? false, false);

        private void LogThrow(string msg, Action act)
        {
            try { act(); } catch (Exception ex) { this.Log().ErrorException(msg, ex); }
        }

        private T LogThrow<T>(string msg, Func<T> act, T errRet)
        {
            try {
                return act();
            } catch (Exception ex) {
                this.Log().ErrorException(msg, ex);
                return errRet;
            }
        }
    }
}
