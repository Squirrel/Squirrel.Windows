
namespace NuGet
{
    internal static class ObjectExtensions
    {
        public static string ToStringSafe(this object obj)
        {
            return obj == null ? null : obj.ToString();
        }
    }
}
