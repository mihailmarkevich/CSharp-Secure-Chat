using System.Text;
using System.Text.Encodings.Web;

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

            // Нормализуем Unicode, чтобы умляуты, акценты и т.п. были в нормальной форме
            var text = input.Normalize(NormalizationForm.FormC);

            // Удаляем управляющие символы, кроме \r\n (если разрешены)
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

            // Ограничиваем длину
            if (cleaned.Length > maxLength)
                cleaned = cleaned[..maxLength];

            return cleaned;
        }
    }
}
