using System.Text;

namespace Server.Helpers
{
    public static class TextHelper
    {
        /// <summary>
        /// Sanitizing plain text to prevent the XSS attach
        /// </summary>
        public static string SanitizePlainText(string? input, int maxLength, bool allowNewLines)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var text = input.Normalize(NormalizationForm.FormC);

            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (char.IsControl(ch) && ch != '\r' && ch != '\n')
                    continue;

                if (!allowNewLines && (ch == '\r' || ch == '\n'))
                    continue;

                sb.Append(ch);
            }

            var cleaned = sb.ToString();

            if (cleaned.Length > maxLength)
                cleaned = cleaned[..maxLength];

            var encoded = new StringBuilder(cleaned.Length);
            foreach (var ch in cleaned)
            {
                switch (ch)
                {
                    case '<':
                        encoded.Append("&lt;");
                        break;
                    case '>':
                        encoded.Append("&gt;");
                        break;
                    case '&':
                        encoded.Append("&amp;");
                        break;
                    default:
                        encoded.Append(ch);
                        break;
                }
            }

            return encoded.ToString();
        }
    }
}
