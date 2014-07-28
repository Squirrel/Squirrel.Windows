using System;
using System.IO;
using System.Net;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading;

namespace Squirrel.Tests
{
    public sealed class StaticHttpServer : IDisposable
    {
        public int Port { get; private set; }
        public string RootPath { get; private set; }

        IDisposable inner;

        public StaticHttpServer(int port, string rootPath)
        {
            Port = port; RootPath = rootPath;
        }

        public IDisposable Start()
        {
            if (inner != null) {
                throw new InvalidOperationException("Already started!");
            }

            var server = new HttpListener();
            server.Prefixes.Add(String.Format("http://+:{0}/", Port));
            server.Start();

            var listener = Observable.Defer(() => Observable.FromAsyncPattern<HttpListenerContext>(server.BeginGetContext, server.EndGetContext)())
                .Repeat()
                .Subscribe(ctx => {
                    if (ctx.Request.HttpMethod != "GET") {
                        closeResponseWith(ctx, 400, "GETs only");
                        return;
                    }

                    var target = Path.Combine(RootPath, ctx.Request.Url.AbsolutePath.Replace('/', Path.DirectorySeparatorChar).Substring(1));
                    var fi = new FileInfo(target);

                    if (!fi.FullName.StartsWith(RootPath)) {
                        closeResponseWith(ctx, 401, "Not authorized");
                        return;
                    }

                    if (!fi.Exists) {
                        closeResponseWith(ctx, 404, "Not found");
                        return;
                    }

                    try {
                        using (var input = File.OpenRead(target)) {
                            ctx.Response.StatusCode = 200;
                            input.CopyTo(ctx.Response.OutputStream);
                            ctx.Response.Close();
                        }
                    } catch (Exception ex) {
                        closeResponseWith(ctx, 500, ex.ToString());
                    }
                });

            var ret = Disposable.Create(() => {
                listener.Dispose();
                server.Stop();
                inner = null;
            });

            inner = ret;
            return ret;
        }

        static void closeResponseWith(HttpListenerContext ctx, int statusCode, string message)
        {
            ctx.Response.StatusCode = statusCode;
            using (var sw = new StreamWriter(ctx.Response.OutputStream, Encoding.UTF8)) {
                sw.WriteLine(message);
            }
            ctx.Response.Close();
        }

        public void Dispose()
        {
            var toDispose = Interlocked.Exchange(ref inner, null);
            if (toDispose != null) {
                toDispose.Dispose();
            }
        }
    }
}
