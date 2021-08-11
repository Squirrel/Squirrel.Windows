using System.Collections;
using System.Collections.Generic;

namespace NuGet
{
    internal class LazyQueue<TVal> : IEnumerable<TVal>
    {
        private readonly IEnumerator<TVal> _enumerator;
        private TVal _peeked;

        public LazyQueue(IEnumerator<TVal> enumerator)
        {
            _enumerator = enumerator;
        }

        public bool TryPeek(out TVal element)
        {
            element = default(TVal);

            if (_peeked != null)
            {
                element = _peeked;
                return true;
            }

            bool next = _enumerator.MoveNext();

            if (next)
            {
                element = _enumerator.Current;
                _peeked = element;
            }

            return next;
        }

        public void Dequeue()
        {
            // Reset the peeked element
            _peeked = default(TVal);
        }

        public IEnumerator<TVal> GetEnumerator()
        {
            return _enumerator;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
