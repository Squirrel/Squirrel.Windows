// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt

using System;
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
        /// Essentially just .Single() but with context aware error messages which are more helpful to a user.
        /// Eg. "Invalid {is}. One {what} expected in {in}. None was found."
        /// Eg. "Invalid {is}. Only a single {what} expected in {in}. There were 2 or more."
        /// </summary>
        public static T ContextualSingle<T>(this IEnumerable<T> source, string strIs, string strWhat, string strIn = null)
        {
            T result;
            using (var e = source.GetEnumerator()) {
                // enumerator starts before the first element. If MoveNext is false there were no elements
                if (!e.MoveNext()) {
                    throw new InvalidOperationException(
                        $"Invalid {strIs}: One {strWhat} expected" +
                        (strIn == null ? "." : $" in {strIn}.") +
                        " None was found.");
                }

                // our first result should be the value we return
                result = e.Current;

                // if MoveNext returns true twice, there were at least 2 elements, so we should also throw.
                if (e.MoveNext()) {
                    throw new InvalidOperationException(
                        $"Invalid {strIs}: Only a single {strWhat} expected" +
                        (strIn == null ? "." : $" in {strIn}.") +
                        $" There were 2 or more.");
                }
            }
            return result;
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
    }
}
