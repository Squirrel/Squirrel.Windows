using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using WpfAnimatedGif;

namespace Squirrel.Update
{
    public class AnimatedGifWindow : Window
    {
        AnimatedGifWindow()
        {
            var source = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "background.gif");

            if (File.Exists(source)) {
                var src = new BitmapImage();
                src.BeginInit();
                src.StreamSource = File.OpenRead(source);
                src.EndInit();

                var img = new Image();
                ImageBehavior.SetAnimatedSource(img, src);
                this.Content = img;
                this.Width = src.Width;
                this.Height = src.Height;
            }
                        
            this.AllowsTransparency = true;
            this.WindowStyle = WindowStyle.None;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.ShowInTaskbar = true;
            this.Topmost = true;
            this.TaskbarItemInfo = new TaskbarItemInfo {
                ProgressState = TaskbarItemProgressState.Normal
            };
            this.Title = "Installing...";
            this.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }



        public static void ShowWindow(TimeSpan initialDelay, CancellationToken token, ProgressSource progressSource)
        {
            var thread = new Thread(_ => {
                try {
                    showWindowImpl(initialDelay, token, progressSource);
                } catch (Exception) {
                    // We must never lose exceptions out of background threads, because it crashes the app
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        static void showWindowImpl(TimeSpan initialDelay, CancellationToken token, ProgressSource progressSource)
        {
            Task.Delay(initialDelay, token).Wait(token);

            var wnd = new AnimatedGifWindow();

            // The window doesn't need to be topmost after 5 seconds 
            Task.Delay(TimeSpan.FromSeconds(5.0), token).ContinueWith(_ => {
                wnd.Dispatcher.BeginInvoke(new Action(() => wnd.Topmost = false));
            }, token);

            // Close the window if the cancellation token fires
            token.Register(() => wnd.Dispatcher.BeginInvoke(new Action(wnd.Close)));

            // Pass progress calls through to the progress bar
            EventHandler<int> progressSourceOnProgress = ((sender, p) =>
                wnd.Dispatcher.BeginInvoke(
                    new Action(() => wnd.TaskbarItemInfo.ProgressValue = p/100.0)));
            progressSource.Progress += progressSourceOnProgress;
            try {
                (new Application()).Run(wnd);
            }
            finally {
                progressSource.Progress -= progressSourceOnProgress;
            }
        }
    }
}
