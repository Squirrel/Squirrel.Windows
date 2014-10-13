namespace Squirrel
{
    public static class StringExtensions
    {
        public static string GetFinalUrl(this string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            return url.ToLower();
        }
    }
}
