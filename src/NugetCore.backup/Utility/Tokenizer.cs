using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NuGet
{
    /// <summary>
    /// This class is used to parse string into tokens.    
    /// There are two types of tokens: variables, e.g. "$variable$", or text. 
    /// The dollar sign can be escaped using $$.
    /// A variable contains only word characters.
    /// 
    /// Examples:
    /// - "a $b$ c" is parsed into 
    ///   {text, "a "}, {variable, "b"}, {text, " c"}.
    /// - "a $$b$$ c" is parsed into
    ///   {text, "a $b$ c"}.
    /// - "a $b$ $c" is parsed into
    ///   {text, "a "}, {variable, "b"}, {text, " $c"}.
    /// - "a $b$$c$" is parsed into
    ///   {text, "a "}, {variable, "b"}, {variable, "c"}.
    /// - "a $b c$d$" is parsed into 
    ///   {text, "a $b c"}, {variable, "d"} (because space is not a word character).
    /// </summary>
    public class Tokenizer
    {
        string _text;
        int _index;

        public Tokenizer(string text)
        {
            _text = text;
            _index = 0;
        }

        /// <summary>
        /// Gets the next token.
        /// </summary>
        /// <returns>The parsed token. Or null if no more tokens are available.</returns>
        public Token Read()
        {
            if (_index >= _text.Length)
            {
                return null;
            }

            if (_text[_index] == '$')
            {
                _index++;
                return ParseTokenAfterDollarSign();
            }
            else
            {
                return ParseText();
            }
        }

        private static bool IsWordChar(char ch)
        {
            // See http://msdn.microsoft.com/en-us/library/20bw873z.aspx#WordCharacter
            var c = CharUnicodeInfo.GetUnicodeCategory(ch);
            return c == UnicodeCategory.LowercaseLetter ||
                c == UnicodeCategory.UppercaseLetter ||
                c == UnicodeCategory.TitlecaseLetter ||
                c == UnicodeCategory.OtherLetter ||
                c == UnicodeCategory.ModifierLetter ||
                c == UnicodeCategory.DecimalDigitNumber ||
                c == UnicodeCategory.ConnectorPunctuation;
        }

        // Parses and returns the next token after a $ is just read.
        // _index is one char after the $.
        private Token ParseTokenAfterDollarSign()
        {
            StringBuilder sb = new StringBuilder();
            while (_index < _text.Length)
            {
                char ch = _text[_index];
                if (ch == '$')
                {
                    ++_index;
                    if (sb.Length == 0)
                    {
                        // escape sequence "$$" is encountered
                        return new Token(TokenCategory.Text, "$");
                    }
                    else
                    {
                        // matching $ is read. So the token is a variable.
                        return new Token(TokenCategory.Variable, sb.ToString());
                    }
                }
                else if (IsWordChar(ch))
                {
                    sb.Append(ch);
                    ++_index;
                }
                else
                {
                    // non word char encountered. So the current token
                    // is not a variable after all.
                    sb.Insert(0, '$');
                    sb.Append(ch);
                    ++_index;
                    return new Token(TokenCategory.Text, sb.ToString());
                }
            }

            // no matching $ is found and the end of text is reached.
            // So the current token is a text.
            sb.Insert(0, '$');
            return new Token(TokenCategory.Text, sb.ToString());
        }

        private Token ParseText()
        {
            StringBuilder sb = new StringBuilder();
            while (_index < _text.Length && _text[_index] != '$')
            {
                sb.Append(_text[_index]);
                _index++;
            }

            return new Token(TokenCategory.Text, sb.ToString());
        }
    }
}
