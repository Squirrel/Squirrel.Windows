using System;

namespace Squirrel.Update
{
    internal interface ISplashWindow : IDisposable
    {
        void Show();
        void Hide();
        void SetNoProgress();
        void SetProgressIndeterminate();
        void SetProgress(ulong completed, ulong total);
        void ShowErrorDialog(string title, string message);
        void ShowInfoDialog(string title, string message);
        bool ShowQuestionDialog(string title, string message);
    }
}
