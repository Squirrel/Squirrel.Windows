// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt

using System;
using System.Linq;
using System.Collections.Generic;

namespace Squirrel
{
    internal static class EnumerableExtensions
    {
        public static IEnumerable<T> Return<T>(T value)
        {
            yield return value;
        }

        /// <summary>
        /// Enumerates the sequence and invokes the given action for each value in the sequence.
        /// </summary>
        /// <typeparam name="TSource">Source sequence element type.</typeparam>
        /// <param name="source">Source sequence.</param>
        /// <param name="onNext">Action to invoke for each element.</param>
        public static void ForEach<TSource>(this IEnumerable<TSource> source, Action<TSource> onNext)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (onNext == null)
                throw new ArgumentNullException("onNext");

            foreach (var item in source) onNext(item);
        }

        /// <summary>
        /// Returns the elements with the maximum key value by using the default comparer to compare key values.
        /// </summary>
        /// <typeparam name="TSource">Source sequence element type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Source sequence.</param>
        /// <param name="keySelector">Key selector used to extract the key for each element in the sequence.</param>
        /// <returns>List with the elements that share the same maximum key value.</returns>
        public static IList<TSource> MaxBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (keySelector == null)
                throw new ArgumentNullException("keySelector");

            return MaxBy(source, keySelector, Comparer<TKey>.Default);
        }

        /// <summary>
        /// Returns the elements with the minimum key value by using the specified comparer to compare key values.
        /// </summary>
        /// <typeparam name="TSource">Source sequence element type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Source sequence.</param>
        /// <param name="keySelector">Key selector used to extract the key for each element in the sequence.</param>
        /// <param name="comparer">Comparer used to determine the maximum key value.</param>
        /// <returns>List with the elements that share the same maximum key value.</returns>
        public static IList<TSource> MaxBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (keySelector == null) throw new ArgumentNullException("keySelector");
            if (comparer == null) throw new ArgumentNullException("comparer");

            return ExtremaBy(source, keySelector, (key, minValue) => comparer.Compare(key, minValue));
        }

        private static IList<TSource> ExtremaBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TKey, TKey, int> compare)
        {
            var result = new List<TSource>();

            using (var e = source.GetEnumerator()) {
                if (!e.MoveNext()) throw new InvalidOperationException("Source sequence doesn't contain any elements.");

                var current = e.Current;
                var resKey = keySelector(current);
                result.Add(current);

                while (e.MoveNext()) {
                    var cur = e.Current;
                    var key = keySelector(cur);

                    var cmp = compare(key, resKey);
                    if (cmp == 0) {
                        result.Add(cur);
                    } else if (cmp > 0) {
                        result = new List<TSource> { cur };
                        resKey = key;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Lazily invokes an action for each value in the sequence.
        /// </summary>
        /// <typeparam name="TSource">Source sequence element type.</typeparam>
        /// <param name="source">Source sequence.</param>
        /// <param name="onNext">Action to invoke for each element.</param>
        /// <returns>Sequence exhibiting the specified side-effects upon enumeration.</returns>
        public static IEnumerable<TSource> Do<TSource>(this IEnumerable<TSource> source, Action<TSource> onNext)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (onNext == null) throw new ArgumentNullException("onNext");

            return DoHelper(source, onNext, _ => { }, () => { });
        }

        /// <summary>
        /// Lazily invokes an action for each value in the sequence, and executes an action for successful termination.
        /// </summary>
        /// <typeparam name="TSource">Source sequence element type.</typeparam>
        /// <param name="source">Source sequence.</param>
        /// <param name="onNext">Action to invoke for each element.</param>
        /// <param name="onCompleted">Action to invoke on successful termination of the sequence.</param>
        /// <returns>Sequence exhibiting the specified side-effects upon enumeration.</returns>
        public static IEnumerable<TSource> Do<TSource>(this IEnumerable<TSource> source, Action<TSource> onNext, Action onCompleted)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (onNext == null) throw new ArgumentNullException("onNext");
            if (onCompleted == null) throw new ArgumentNullException("onCompleted");

            return DoHelper(source, onNext, _ => { }, onCompleted);
        }

        /// <summary>
        /// Lazily invokes an action for each value in the sequence, and executes an action upon exceptional termination.
        /// </summary>
        /// <typeparam name="TSource">Source sequence element type.</typeparam>
        /// <param name="source">Source sequence.</param>
        /// <param name="onNext">Action to invoke for each element.</param>
        /// <param name="onError">Action to invoke on exceptional termination of the sequence.</param>
        /// <returns>Sequence exhibiting the specified side-effects upon enumeration.</returns>
        public static IEnumerable<TSource> Do<TSource>(this IEnumerable<TSource> source, Action<TSource> onNext, Action<Exception> onError)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (onNext == null) throw new ArgumentNullException("onNext");
            if (onError == null) throw new ArgumentNullException("onError");

            return DoHelper(source, onNext, onError, () => { });
        }

        /// <summary>
        /// Lazily invokes an action for each value in the sequence, and executes an action upon successful or exceptional termination.
        /// </summary>
        /// <typeparam name="TSource">Source sequence element type.</typeparam>
        /// <param name="source">Source sequence.</param>
        /// <param name="onNext">Action to invoke for each element.</param>
        /// <param name="onError">Action to invoke on exceptional termination of the sequence.</param>
        /// <param name="onCompleted">Action to invoke on successful termination of the sequence.</param>
        /// <returns>Sequence exhibiting the specified side-effects upon enumeration.</returns>
        public static IEnumerable<TSource> Do<TSource>(this IEnumerable<TSource> source, Action<TSource> onNext, Action<Exception> onError, Action onCompleted)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (onNext == null) throw new ArgumentNullException("onNext");
            if (onError == null) throw new ArgumentNullException("onError");
            if (onCompleted == null) throw new ArgumentNullException("onCompleted");

            return DoHelper(source, onNext, onError, onCompleted);
        }

        private static IEnumerable<TSource> DoHelper<TSource>(this IEnumerable<TSource> source, Action<TSource> onNext, Action<Exception> onError, Action onCompleted)
        {
            using (var e = source.GetEnumerator()) {
                while (true) {
                    var current = default(TSource);
                    try {
                        if (!e.MoveNext())
                            break;

                        current = e.Current;
                    } catch (Exception ex) {
                        onError(ex);
                        throw;
                    }

                    onNext(current);
                    yield return current;
                }

                onCompleted();
            }
        }

        /// <summary>
        /// Returns the source sequence prefixed with the specified value.
        /// </summary>
        /// <typeparam name="TSource">Source sequence element type.</typeparam>
        /// <param name="source">Source sequence.</param>
        /// <param name="values">Values to prefix the sequence with.</param>
        /// <returns>Sequence starting with the specified prefix value, followed by the source sequence.</returns>
        public static IEnumerable<TSource> StartWith<TSource>(this IEnumerable<TSource> source, params TSource[] values)
        {
            if (source == null) throw new ArgumentNullException("source");

            return source.StartWith_(values);
        }

        static IEnumerable<TSource> StartWith_<TSource>(this IEnumerable<TSource> source, params TSource[] values)
        {
            foreach (var x in values) yield return x;

            foreach (var item in source) yield return item;
        }

        /// <summary>
        /// Returns elements with a distinct key value by using the default equality comparer to compare key values.
        /// </summary>
        /// <typeparam name="TSource">Source sequence element type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Source sequence.</param>
        /// <param name="keySelector">Key selector.</param>
        /// <returns>Sequence that contains the elements from the source sequence with distinct key values.</returns>
        public static IEnumerable<TSource> Distinct<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (keySelector == null) throw new ArgumentNullException("keySelector");

            return source.Distinct_(keySelector, EqualityComparer<TKey>.Default);
        }

        /// <summary>
        /// Returns elements with a distinct key value by using the specified equality comparer to compare key values.
        /// </summary>
        /// <typeparam name="TSource">Source sequence element type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Source sequence.</param>
        /// <param name="keySelector">Key selector.</param>
        /// <param name="comparer">Comparer used to compare key values.</param>
        /// <returns>Sequence that contains the elements from the source sequence with distinct key values.</returns>
        public static IEnumerable<TSource> Distinct<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (keySelector == null) throw new ArgumentNullException("keySelector");
            if (comparer == null) throw new ArgumentNullException("comparer");

            return source.Distinct_(keySelector, comparer);
        }

        static IEnumerable<TSource> Distinct_<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            var set = new HashSet<TKey>(comparer);

            foreach (var item in source) {
                var key = keySelector(item);

                if (set.Add(key)) yield return item;
            }
        }
    }
}
