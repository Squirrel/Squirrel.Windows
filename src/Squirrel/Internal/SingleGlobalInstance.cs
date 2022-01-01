using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    internal sealed class SingleGlobalInstance : IDisposable, IEnableLogger
    {
        IDisposable handle = null;

        public SingleGlobalInstance(string key, TimeSpan timeOut)
        {
            if (ModeDetector.InUnitTestRunner()) {
                return;
            }

            var path = Path.Combine(Path.GetTempPath(), ".squirrel-lock-" + key);

            var st = new Stopwatch();
            st.Start();

            var fh = default(FileStream);
            while (st.Elapsed < timeOut) {
                try {
                    fh = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Delete);
                    fh.Write(new byte[] { 0xba, 0xad, 0xf0, 0x0d, }, 0, 4);
                    break;
                } catch (Exception ex) {
                    this.Log().WarnException("Failed to grab lockfile, will retry: " + path, ex);
                    Thread.Sleep(250);
                }
            }

            st.Stop();

            if (fh == null) {
                throw new Exception("Couldn't acquire lock, is another instance running?");
            }

            handle = Disposable.Create(() => {
                fh.Dispose();
                File.Delete(path);
            });
        }

        public void Dispose()
        {
            if (ModeDetector.InUnitTestRunner()) {
                return;
            }

            var disp = Interlocked.Exchange(ref handle, null);
            if (disp != null) disp.Dispose();
        }

        ~SingleGlobalInstance()
        {
            Dispose();
        }
    }
}
