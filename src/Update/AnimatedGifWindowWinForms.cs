using Microsoft.WindowsAPICodePack.Taskbar;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Squirrel.Update
{
    public class AnimatedGifWindow : Form
    {
        const int WM_NCLBUTTONDOWN = 0xA1;
        const int HT_CAPTION = 0x2;

        [DllImport("user32.dll")]
        static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();

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

            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            this.WindowState = FormWindowState.Minimized;

            Action size = () => {
                pictureBox.Width = this.Width; pictureBox.Height = this.Height;
                pictureBox.Left = 0; pictureBox.Top = 0;
                this.CenterToScreen();
            };

            pictureBox.LoadCompleted += (o, e) => {
                if (pictureBox.Image == null) return;
                pictureBox.SizeMode = PictureBoxSizeMode.StretchImage;

                this.SizeChanged += (_o, _e) => size();

                this.Width = pictureBox.Image.Width;
                this.Height = pictureBox.Image.Height;
                this.CenterToScreen();
            };

            pictureBox.MouseDown += (o, e) => // Enable left-mouse dragging of splash
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };

            this.FormBorderStyle = FormBorderStyle.None;
            this.Width = 1;
            this.Height = 1;
            this.TopMost = true;
            this.Text = "Installing...";
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Do not call base to allow transparency
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
                if (token.IsCancellationRequested) return;

                try {
                    wnd.Show();
                } catch (Exception) {
                    return;
                }

                token.Register(() => wnd.Invoke(new Action(() => wnd.Close())));

                var t = new System.Windows.Forms.Timer();
                var taskbar = TaskbarManager.Instance;
                t.Tick += (o, e) => {
                    wnd.WindowState = FormWindowState.Normal;
                    taskbar.SetProgressState(TaskbarProgressBarState.Normal, wnd.Handle);

                    progressSource.Progress += (_o, val) => {
                        if (wnd.IsDisposed) return;
                        wnd.Invoke(new Action(() => taskbar.SetProgressValue(val, 100, wnd.Handle)));
                    };

                    t.Stop();
                };

                t.Interval = 400;
                t.Start();

                Task.Delay(TimeSpan.FromSeconds(5.0), token).ContinueWith(task => {
                    if (task.IsCanceled) return;
                    if (wnd.IsDisposed) return;
                    wnd.Invoke(new Action(() => wnd.TopMost = false));
                });

                if (token.IsCancellationRequested) return;

#if !DEBUG
                try {
#endif
                    Application.Run(wnd);
#if !DEBUG
                } catch (Exception) {
                    return;
                }
#endif
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }
}
