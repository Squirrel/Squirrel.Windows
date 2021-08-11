using System;
using System.Linq;
using System.Reflection;

namespace NuGet
{
    public static class CustomAttributeProviderExtensions
    {
        public static T GetCustomAttribute<T>(this ICustomAttributeProvider attributeProvider)
        {
            return (T)GetCustomAttribute(attributeProvider, typeof(T));
        }

        public static object GetCustomAttribute(this ICustomAttributeProvider attributeProvider, Type type)
        {
            return attributeProvider.GetCustomAttributes(type, inherit: false)
                                       .FirstOrDefault();
        }
    }
}
