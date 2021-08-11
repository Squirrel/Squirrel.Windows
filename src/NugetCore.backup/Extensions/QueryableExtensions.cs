using System.Collections.Generic;
using System.Linq;

namespace NuGet
{
    public static class QueryableExtensions
    {
        public static IEnumerable<T> AsBufferedEnumerable<T>(this IQueryable<T> source, int bufferSize)
        {
            return new BufferedEnumerable<T>(source, bufferSize);
        }
    }
}