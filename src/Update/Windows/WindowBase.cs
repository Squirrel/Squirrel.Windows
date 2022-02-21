using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;

namespace Squirrel.Update.Windows
{
    internal abstract class WindowBase : ISplashWindow
    {
        public abstract IntPtr Handle { get; }

        public virtual string AppName { get; }

        public WindowBase(string appName)
        {
            AppName = appName;
        }

        public abstract void Dispose();

        public abstract void Hide();

        public abstract void SetProgress(ulong completed, ulong total);

        public abstract void SetProgressIndeterminate();

        public abstract void Show();

        public virtual void ShowErrorDialog(string title, string message)
        {
            this.Log().Info("User shown err: " + message);
            User32MessageBox.Show(
                Handle,
                message,
                AppName + " - " + title,
                User32MessageBox.MessageBoxButtons.OK,
                User32MessageBox.MessageBoxIcon.Error);
        }

        public virtual void ShowInfoDialog(string title, string message)
        {
            this.Log().Info("User shown message: " + message);
            User32MessageBox.Show(
                Handle,
                message,
                AppName + " - " + title,
                User32MessageBox.MessageBoxButtons.OK,
                User32MessageBox.MessageBoxIcon.Information);
        }

        public virtual bool ShowQuestionDialog(string title, string message)
        {
            var result = User32MessageBox.Show(
                Handle,
                message,
                AppName + " - " + title,
                User32MessageBox.MessageBoxButtons.OKCancel,
                User32MessageBox.MessageBoxIcon.Question,
                User32MessageBox.MessageBoxResult.Cancel);
            this.Log().Info("User prompted: '" + message + "' -- User answered " + result.ToString());
            return User32MessageBox.MessageBoxResult.OK == result;
        }
    }
}
