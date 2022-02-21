using System;
using Squirrel.SimpleSplat;

namespace Squirrel.Update
{
    internal interface ISplashWindow : IDisposable, IEnableLogger
    {
        void Show();
        void Hide();
        void SetProgressIndeterminate();
        void SetProgress(ulong completed, ulong total);
        void ShowErrorDialog(string title, string message);
        void ShowInfoDialog(string title, string message);
        bool ShowQuestionDialog(string title, string message);
    }
}
