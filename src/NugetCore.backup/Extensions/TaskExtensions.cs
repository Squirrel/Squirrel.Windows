using System;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet
{
    public static class TaskExtensions
    {
        public static TResult WhenAny<TResult>(this Task<TResult>[] tasks, Predicate<TResult> predicate)
        {
            int numTasksRemaining = tasks.Length;
            var tcs = new TaskCompletionSource<TResult>();

            foreach (var task in tasks)
            {
                task.ContinueWith(innerTask =>
                {
                    if (innerTask.Status == TaskStatus.RanToCompletion && predicate(innerTask.Result))
                    {
                        // success
                        tcs.TrySetResult(innerTask.Result);
                    }

                    if (Interlocked.Decrement(ref numTasksRemaining) == 0)
                    {
                        tcs.TrySetResult(default(TResult));
                    }
                }, TaskContinuationOptions.ExecuteSynchronously);
            }

            return tcs.Task.Result;
        }
    }
}
