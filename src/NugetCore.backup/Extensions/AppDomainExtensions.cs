using System;

namespace NuGet.Runtime
{
    public static class AppDomainExtensions
    {
        /// <summary>
        /// Creates an instance of a type in another application domain
        /// </summary>
        public static T CreateInstance<T>(this AppDomain domain)
        {
            return (T)domain.CreateInstanceAndUnwrap(typeof(T).Assembly.FullName,
                                                     typeof(T).FullName);
        }
    }
}
