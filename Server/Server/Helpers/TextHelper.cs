using System.Text.Encodings.Web;

namespace Server.Helpers
{
    public static class TextHelper
    {
        /// <summary>
        /// Sanitizing plain text to prevent the XSS attach
        /// </summary>
        public static string SanitizePlainText(string input, int maxLength, bool allowNewLines = false)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var trimmed = input.Trim();

            if (trimmed.Length > maxLength)
                trimmed = trimmed[..maxLength];

            // remove control symbols
            trimmed = new string(trimmed.Where(c =>
            {
                if (!char.IsControl(c))
                    return true;

                if (allowNewLines && (c == '\n' || c == '\r'))
                    return true;

                return false;
            }).ToArray());

            // HTML-escaping
            var encoded = HtmlEncoder.Default.Encode(trimmed);

            return encoded;
        }
    }
}
