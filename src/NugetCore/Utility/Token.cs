namespace NuGet
{
    public class Token
    {
        public string Value { get; private set; }
        public TokenCategory Category { get; private set; }

        public Token(TokenCategory category, string value)
        {
            Category = category;
            Value = value;
        }
    }
}
