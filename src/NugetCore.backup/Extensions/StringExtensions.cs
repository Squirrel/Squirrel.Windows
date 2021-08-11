namespace NuGet
{
    internal static class StringExtensions
    {
        public static string SafeTrim(this string value)
        {
            return value == null ? null : value.Trim();
        }
    }
}
