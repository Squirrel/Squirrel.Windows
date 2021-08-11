using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace NuGet
{
    /// <summary>
    /// An IEnumerble&lt;T&gt; implementation that queries an IQueryable&lt;T&gt; on demand. 
    /// This is usefult when a lot of data can be returned from an IQueryable source, but
    /// you don't want to do it all at once.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix", Justification = "Collection isn't correct")]
    public class BufferedEnumerable<TElement> : IEnumerable<TElement>
    {
        private readonly IQueryable<TElement> _source;
        private readonly int _bufferSize;
        private readonly QueryState<TElement> _state;

        public BufferedEnumerable(IQueryable<TElement> source, int bufferSize)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            _state = new QueryState<TElement>(bufferSize);
            _source = source;
            _bufferSize = bufferSize;
        }

        public IEnumerator<TElement> GetEnumerator()
        {
            return new BufferedEnumerator<TElement>(_state, _source, _bufferSize);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return _source.ToString();
        }

        internal class BufferedEnumerator<T> : IEnumerator<T>
        {
            private readonly int _bufferSize;

            private IQueryable<T> _source;
            private QueryState<T> _state;
            private int _index = -1;

            public BufferedEnumerator(QueryState<T> state, IQueryable<T> source, int bufferSize)
            {
                _state = state;
                _source = source;
                _bufferSize = bufferSize;
            }

            public T Current
            {
                get
                {
                    Debug.Assert(_index < _state.Cache.Count);
                    return _state.Cache[_index];
                }
            }

            internal bool IsEmpty
            {
                get
                {
                    return _state.HasItems && (_index == _state.Cache.Count - 1);
                }
            }

            public void Dispose()
            {
                _source = null;
                _state = null;
            }

            object IEnumerator.Current
            {
                get
                {
                    return Current;
                }
            }

            public bool MoveNext()
            {
                if (IsEmpty)
                {
                    // Request a page
                    List<T> items = _source.Skip(_state.Cache.Count)
                                           .Take(_bufferSize)
                                           .ToList();

                    // See if we have anymore items after the last query
                    _state.HasItems = _bufferSize == items.Count;

                    // Add it to the cache
                    _state.Cache.AddRange(items);
                }

                _index++;
                // We can keep going unless the source said we're empty
                return _index < _state.Cache.Count;
            }

            public void Reset()
            {
                _index = -1;
            }

            public override string ToString()
            {
                return _source.ToString();
            }
        }

        internal class QueryState<T>
        {
            public QueryState(int bufferSize)
            {
                Cache = new List<T>(bufferSize);
                HasItems = true;
            }

            public List<T> Cache { get; private set; }
            public bool HasItems { get; set; }
        }
    }
}
