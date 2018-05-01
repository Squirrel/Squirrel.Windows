#if !MONO
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using WpfAnimatedGif;

namespace Squirrel.Update
{
    public class AnimatedGifWindow : Window
    {
        public AnimatedGifWindow()
        {
            var img = new Image();
            var src = default(BitmapImage);

            var executionLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var source = Path.Combine(
                executionLocation,
                "background.gif");

            if (File.Exists(source)) {
                src = new BitmapImage();
                src.BeginInit();
                src.StreamSource = File.OpenRead(source);
                src.EndInit();
            
                ImageBehavior.SetAnimatedSource(img, src);
                this.Content = img;
                this.Width = src.Width;
                this.Height = src.Height;
            }

            var setupIcon = Path.Combine(executionLocation, "setupIcon.ico");
            if (File.Exists(setupIcon)) {
                Icon = BitmapFrame.Create(new Uri(setupIcon, UriKind.Relative));
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
            var wnd = default(AnimatedGifWindow);

            var thread = new Thread(() => {
                if (token.IsCancellationRequested) return;

                try {
                    Task.Delay(initialDelay, token).ContinueWith(t => { return true; }).Wait();
                } catch (Exception) {
                    return;
                }

                wnd = new AnimatedGifWindow();
                wnd.Show();

                Task.Delay(TimeSpan.FromSeconds(5.0), token).ContinueWith(t => {
                    if (t.IsCanceled) return;
                    wnd.Dispatcher.BeginInvoke(new Action(() => wnd.Topmost = false));
                });

                token.Register(() => wnd.Dispatcher.BeginInvoke(new Action(wnd.Close)));
                EventHandler<int> progressSourceOnProgress = ((sender, p) =>
                    wnd.Dispatcher.BeginInvoke(
                        new Action(() => wnd.TaskbarItemInfo.ProgressValue = p/100.0)));
                progressSource.Progress += progressSourceOnProgress;
                try {
                    (new Application()).Run(wnd);
                } finally {
                    progressSource.Progress -= progressSourceOnProgress;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            this.DragMove();
        }
    }
}

#endif