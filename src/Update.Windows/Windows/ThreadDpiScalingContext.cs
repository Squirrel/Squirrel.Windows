using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static Vanara.PInvoke.User32;
using static Vanara.PInvoke.SHCore;

// from clowd-windows/Clowd.PlatformUtil/Windows/ThreadDpiScalingContext.cs

namespace Squirrel.Update.Windows
{
    internal enum ThreadScalingMode
    {
        Unaware,
        SystemAware,
        PerMonitorAware,
        PerMonitorV2Aware,
        UnawareGdiScaled,
    }

    internal static class ThreadDpiScalingContext
    {
        private static readonly DPI_AWARENESS_CONTEXT DPI_AWARENESS_CONTEXT_UNAWARE = new DPI_AWARENESS_CONTEXT((IntPtr)(-1));
        private static readonly DPI_AWARENESS_CONTEXT DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = new DPI_AWARENESS_CONTEXT((IntPtr)(-2));
        private static readonly DPI_AWARENESS_CONTEXT DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = new DPI_AWARENESS_CONTEXT((IntPtr)(-3));
        private static readonly DPI_AWARENESS_CONTEXT DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new DPI_AWARENESS_CONTEXT((IntPtr)(-4));
        private static readonly DPI_AWARENESS_CONTEXT DPI_AWARENESS_CONTEXT_UNAWARE_GDISCALED = new DPI_AWARENESS_CONTEXT((IntPtr)(-5));

        private static ThreadScalingMode Get1607ThreadAwarenessContext()
        {
            var context = GetThreadDpiAwarenessContext();

            if (AreDpiAwarenessContextsEqual(context, DPI_AWARENESS_CONTEXT_UNAWARE))
            {
                return ThreadScalingMode.Unaware;
            }
            else if (AreDpiAwarenessContextsEqual(context, DPI_AWARENESS_CONTEXT_SYSTEM_AWARE))
            {
                return ThreadScalingMode.SystemAware;
            }
            else if (AreDpiAwarenessContextsEqual(context, DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE))
            {
                return ThreadScalingMode.PerMonitorAware;
            }
            else if (AreDpiAwarenessContextsEqual(context, DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
            {
                return ThreadScalingMode.PerMonitorV2Aware;
            }
            else if (AreDpiAwarenessContextsEqual(context, DPI_AWARENESS_CONTEXT_UNAWARE_GDISCALED))
            {
                return ThreadScalingMode.UnawareGdiScaled;
            }
            else
            {
                throw new ArgumentOutOfRangeException("DPI_AWARENESS_CONTEXT");
            }
        }

        private static void Set1607ThreadAwarenessContext(ThreadScalingMode mode)
        {
            var ctx = mode switch
            {
                ThreadScalingMode.Unaware => DPI_AWARENESS_CONTEXT_UNAWARE,
                ThreadScalingMode.SystemAware => DPI_AWARENESS_CONTEXT_SYSTEM_AWARE,
                ThreadScalingMode.PerMonitorAware => DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE,
                ThreadScalingMode.PerMonitorV2Aware => DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2,
                ThreadScalingMode.UnawareGdiScaled => DPI_AWARENESS_CONTEXT_UNAWARE_GDISCALED,
                _ => throw new ArgumentOutOfRangeException("mode"),
            };

            var ctxOld = SetThreadDpiAwarenessContext(ctx);
            if (((IntPtr)ctxOld) == IntPtr.Zero)
                throw new Exception("Failed to update thread awareness context");
        }

        private static void SetShcoreAwareness(ThreadScalingMode mode)
        {
            var aw = mode switch
            {
                ThreadScalingMode.Unaware => PROCESS_DPI_AWARENESS.PROCESS_DPI_UNAWARE,
                ThreadScalingMode.SystemAware => PROCESS_DPI_AWARENESS.PROCESS_SYSTEM_DPI_AWARE,
                ThreadScalingMode.PerMonitorAware => PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE,
                ThreadScalingMode.PerMonitorV2Aware => PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE,
                ThreadScalingMode.UnawareGdiScaled => PROCESS_DPI_AWARENESS.PROCESS_DPI_UNAWARE,
                _ => throw new ArgumentOutOfRangeException("mode"),
            };

            Marshal.ThrowExceptionForHR((int)SetProcessDpiAwareness(aw));
        }

        private static ThreadScalingMode GetShcoreAwareness()
        {
            Marshal.ThrowExceptionForHR((int)GetProcessDpiAwareness(default, out var aw));
            return aw switch
            {
                PROCESS_DPI_AWARENESS.PROCESS_DPI_UNAWARE => ThreadScalingMode.Unaware,
                PROCESS_DPI_AWARENESS.PROCESS_SYSTEM_DPI_AWARE => ThreadScalingMode.SystemAware,
                PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE => ThreadScalingMode.PerMonitorAware,
                _ => throw new ArgumentOutOfRangeException("DPI_AWARENESS"),
            };
        }

        /// <summary>
        /// Gets the current thread scaling / dpi awareness.
        /// </summary>
        public static ThreadScalingMode GetCurrentThreadScalingMode()
        {
            try
            {
                return Get1607ThreadAwarenessContext();
            }
            catch { }

            try
            {
                return GetShcoreAwareness();
            }
            catch { }

            return ThreadScalingMode.Unaware;
        }

        /// <summary>
        /// Sets the current thread scaling / dpi awareness. This will only succeed if the OS is windows 8.1 or above,
        /// and if no winapi functions which perform scaling have been called on this thread. 
        /// </summary>
        public static bool SetCurrentThreadScalingMode(ThreadScalingMode mode)
        {
            try
            {
                Set1607ThreadAwarenessContext(mode);
                return true;
            }
            catch { }

            try
            {
                // technically, on older versions of windows, this will update the dpi awareness for the whole process.
                // if an exe manifest is present, or on subsequent calls, this will fail.
                // in newer versions of windows, dpi is thread-specific, so this logic is equivilant to SetThreadDpiAwarenessContext
                SetShcoreAwareness(mode);
                return true;
            }
            catch { }

            // unable to set awareness, this could be because a UI has been created already or because the 
            // api's we need do not yet exist (older windows SDK's)
            return false;
        }

        public static void RunScalingAware(ThreadScalingMode mode, Action task)
        {
            RunScalingAware(mode, () => { task(); return true; });
        }

        public static T RunScalingAware<T>(ThreadScalingMode mode, Func<T> task)
        {
            var thread = new AwareThread<T>(mode, task);
            return thread.GetResult();
        }

        public static Task RunScalingAwareAsync(ThreadScalingMode mode, Action task)
        {
            return RunScalingAwareAsync(mode, () => { task(); return true; });
        }

        public static Task<T> RunScalingAwareAsync<T>(ThreadScalingMode mode, Func<T> task)
        {
            var thread = new AwareThread<T>(mode, task);
            return thread.Wait();
        }

        private class AwareThread<T>
        {
            ThreadScalingMode scaling;
            Func<T> job;
            TaskCompletionSource<T> source;
            Thread thread;

            public AwareThread(ThreadScalingMode mode, Func<T> task)
            {
                scaling = mode;
                job = task;
                source = new TaskCompletionSource<T>();
                thread = new Thread(Run);
                thread.IsBackground = true;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }

            public Task<T> Wait()
            {
                return this.source.Task;
            }

            public T GetResult()
            {
                thread.Join();
                return source.Task.Result;
            }

            void Run()
            {
                try
                {
                    // we won't try shcore here, since between 8.1-10 this is global and not thread specific.
                    // we also only catch DllNotFoundException in the case it's not supported by the OS we want to complete the task anyway
                    try
                    {
                        Set1607ThreadAwarenessContext(scaling);
                    }
                    catch (DllNotFoundException) { }

                    this.source.SetResult(job());
                }
                catch (Exception ex)
                {
                    this.source.SetException(ex);
                }
            }
        }
    }
}
