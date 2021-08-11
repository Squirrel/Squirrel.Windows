using System;
using System.Collections;
using System.Collections.Generic;

namespace NuGet
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix", Justification="A hash set should not end in Collections")]
    public class ReadOnlyHashSet<T> : ISet<T>
    {
        private readonly ISet<T> _backingSet;

        public ReadOnlyHashSet(IEnumerable<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException("items");
            }

            _backingSet = new HashSet<T>(items);
        }

        public ReadOnlyHashSet(IEnumerable<T> items, IEqualityComparer<T> comparer)
        {
            if (items == null)
            {
                throw new ArgumentNullException("items");
            }

            if (comparer == null)
            {
                throw new ArgumentNullException("comparer");
            }

            _backingSet = new HashSet<T>(items, comparer);
        }
        
        public ReadOnlyHashSet(ISet<T> backingSet)
        {
            if (backingSet == null)
            {
                throw new ArgumentNullException("backingSet");
            }

            _backingSet = backingSet;
        }

        public bool Add(T item)
        {
            throw new NotSupportedException();
        }

        public void ExceptWith(IEnumerable<T> other)
        {
            _backingSet.ExceptWith(other);
        }

        public void IntersectWith(IEnumerable<T> other)
        {
            _backingSet.IntersectWith(other);
        }

        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            return _backingSet.IsProperSubsetOf(other);
        }

        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            return _backingSet.IsProperSupersetOf(other);
        }

        public bool IsSubsetOf(IEnumerable<T> other)
        {
            return _backingSet.IsSubsetOf(other);
        }

        public bool IsSupersetOf(IEnumerable<T> other)
        {
            return _backingSet.IsSupersetOf(other);
        }

        public bool Overlaps(IEnumerable<T> other)
        {
            return _backingSet.Overlaps(other);
        }

        public bool SetEquals(IEnumerable<T> other)
        {
            return _backingSet.SetEquals(other);
        }

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            _backingSet.SymmetricExceptWith(other);
        }

        public void UnionWith(IEnumerable<T> other)
        {
            _backingSet.UnionWith(other);
        }

        void ICollection<T>.Add(T item)
        {
            throw new NotSupportedException();
        }

        public void Clear()
        {
            throw new NotSupportedException();
        }

        public bool Contains(T item)
        {
            return _backingSet.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _backingSet.CopyTo(array, arrayIndex);
        }

        public int Count
        {
            get { return _backingSet.Count; }
        }

        public bool IsReadOnly
        {
            get { return true; }
        }

        public bool Remove(T item)
        {
            throw new NotSupportedException();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _backingSet.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_backingSet).GetEnumerator();
        }
    }
}
