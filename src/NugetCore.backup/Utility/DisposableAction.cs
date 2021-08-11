using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace NuGet
{
    public sealed class DisposableAction : IDisposable
    {
        [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes", Justification = "This is immutable. Multiple calls to dispose will be complete no-ops and there is no state stored.")]
        public static readonly DisposableAction NoOp = new DisposableAction(() => { });

        private Action _action;

        public DisposableAction(Action action)
        {
            _action = action;
        }

        public static IDisposable All(params IDisposable[] tokens)
        {
            return new DisposableAction(() =>
            {
                foreach (var token in tokens)
                {
                    token.Dispose();
                }
            });
        }

        public static IDisposable All(IEnumerable<IDisposable> tokens)
        {
            return All(tokens.ToArray());
        }

        public void Dispose()
        {
            _action();
        }
    }
}
