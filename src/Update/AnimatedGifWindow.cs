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
using System.Windows.Threading;
using WpfAnimatedGif;

namespace Squirrel.Update
{
    public class AnimatedGifWindow : Window
    {
        public AnimatedGifWindow()
        {
            var img = new Image();
            var src = default(BitmapImage);

            var source = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
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
                        
            this.AllowsTransparency = true;
            this.WindowStyle = WindowStyle.None;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.ShowInTaskbar = false;
            this.Topmost = true;
            this.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        public static void ShowWindow(TimeSpan initialDelay, CancellationToken token)
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
                (new Application()).Run(wnd);
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }
}
