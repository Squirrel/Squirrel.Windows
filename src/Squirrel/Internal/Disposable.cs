using System;
using System.Threading;

namespace Squirrel
{
    internal static class Disposable
    {
        public static IDisposable Create(Action action)
        {
            return new AnonDisposable(action);
        }

        class AnonDisposable : IDisposable
        {
            static readonly Action dummyBlock = (() => { });
            Action block;

            public AnonDisposable(Action b)
            {
                block = b;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref block, dummyBlock)();
            }
        }
    }
}
