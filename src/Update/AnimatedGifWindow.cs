using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using WpfAnimatedGif;

namespace Squirrel.Update
{
    public class AnimatedGifWindow : Window
    {
        public AnimatedGifWindow()
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

        /// <summary>
        /// Blocking method to show the progress window - expects to be called on STA thread separate to app main thread
        /// </summary>
        static void showWindowImpl(TimeSpan initialDelay, CancellationToken cancellation, ProgressSource progressSource)
        {
            Task.Delay(initialDelay, cancellation).ContinueWith(_ => true,cancellation).Wait(cancellation);

            var wnd = new AnimatedGifWindow();
            wnd.Show();

            // The window doesn't need to be topmost after 5 seconds 
            Task.Delay(TimeSpan.FromSeconds(5.0), cancellation).ContinueWith(_ => {
                wnd.Dispatcher.BeginInvoke(new Action(() => wnd.Topmost = false));
            }, cancellation);

            // Close the window if the cancellation token fires
            cancellation.Register(() => wnd.Dispatcher.BeginInvoke(new Action(wnd.Close)));

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
