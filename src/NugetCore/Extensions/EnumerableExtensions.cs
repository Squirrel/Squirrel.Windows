using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace NuGet
{
    public static class EnumerableExtensions
    {
        /// <summary>
        /// Returns a distinct set of elements using the comparer specified. This implementation will pick the last occurrence
        /// of each element instead of picking the first. This method assumes that similar items occur in order.
        /// </summary>        
        internal static IEnumerable<TElement> DistinctLast<TElement>(this IEnumerable<TElement> source,
                                                                   IEqualityComparer<TElement> equalityComparer,
                                                                   IComparer<TElement> comparer)
        {
            bool first = true;
            bool maxElementHasValue = false;
            var previousElement = default(TElement);
            var maxElement = default(TElement);

            foreach (TElement element in source)
            {
                // If we're starting a new group then return the max element from the last group
                if (!first && !equalityComparer.Equals(element, previousElement))
                {
                    yield return maxElement;

                    // Reset the max element
                    maxElementHasValue = false;
                }

                // If the current max element has a value and is bigger or doesn't have a value then update the max
                if (!maxElementHasValue || (maxElementHasValue && comparer.Compare(maxElement, element) < 0))
                {
                    maxElement = element;
                    maxElementHasValue = true;
                }

                previousElement = element;
                first = false;
            }

            if (!first)
            {
                yield return maxElement;
            }
        }

        /// <summary>
        /// Iterates over an IEnumerable while ignoring any exceptions.
        /// </summary>
        /// <returns>An IEnumerable containing elements from the original sequence that did not throw.</returns>
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "By defintion we want to ignore all exceptions")]
        internal static IEnumerable<TElement> SafeIterate<TElement>(this IEnumerable<TElement> source)
        {
            var result = new List<TElement>();
            using (var enumerator = source.GetEnumerator())
            {
                bool hasNext = true;
                while (hasNext)
                {
                    try
                    {
                        hasNext = enumerator.MoveNext();
                        if (!hasNext)
                        {
                            break;
                        }
                        result.Add(enumerator.Current);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
            return result;
        }

        public static bool IsEmpty<T>(this IEnumerable<T> sequence)
        {
            return sequence == null || !sequence.Any();
        }
    }
}
