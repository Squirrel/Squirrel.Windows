using Microsoft.WindowsAPICodePack.Taskbar;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Squirrel.Update
{
    public class AnimatedGifWindow : Form
    {
        PictureBox pictureBox;

        AnimatedGifWindow()
        {
            var source = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "background.gif");

            pictureBox = new PictureBox();
            this.Controls.Add(pictureBox);

            if (File.Exists(source)) {
                pictureBox.ImageLocation = source;
            }

            this.WindowState = FormWindowState.Minimized;
            Action size = () => { pictureBox.Width = this.Width; pictureBox.Height = this.Height; pictureBox.Left = 0; pictureBox.Top = 0; };
            pictureBox.LoadCompleted += (o, e) => {
                if (pictureBox.Image == null) return;
                pictureBox.SizeMode = PictureBoxSizeMode.StretchImage;

                this.SizeChanged += (_o, _e) => size();

                this.Width = pictureBox.Image.Width / 2;
                this.Height = pictureBox.Image.Height / 2;
                this.CenterToScreen();
            };

            this.FormBorderStyle = FormBorderStyle.None;
            this.Width = 1;
            this.Height = 1;
            this.TopMost = true;
        }

        public static void ShowWindow(TimeSpan initialDelay, CancellationToken token, ProgressSource progressSource)
        {
            var thread = new Thread(() => {
                if (token.IsCancellationRequested) return;

                try {
                    Task.Delay(initialDelay, token).ContinueWith(_ => { return true; }).Wait();
                } catch (Exception) {
                    // NB: Cancellation will end up here, so we'll bail out
                    return;
                }

                var wnd = new AnimatedGifWindow();
                wnd.Show();

                token.Register(() => wnd.Invoke(new Action(() => wnd.Close())));

                var t = new System.Windows.Forms.Timer();
                var taskbar = TaskbarManager.Instance;
                t.Tick += (o, e) => {
                    wnd.WindowState = FormWindowState.Normal;
                    taskbar.SetProgressState(TaskbarProgressBarState.Normal, wnd.Handle);

                    progressSource.Progress += (_o, val) => {
                        wnd.Invoke(new Action(() => taskbar.SetProgressValue(val, 100, wnd.Handle)));
                    };

                    t.Stop();
                };

                t.Interval = 400;
                t.Start();

                Task.Delay(TimeSpan.FromSeconds(5.0), token).ContinueWith(task => {
                    if (task.IsCanceled) return;
                    wnd.Invoke(new Action(() => wnd.TopMost = false));
                });

                Application.Run(wnd);
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }
}
