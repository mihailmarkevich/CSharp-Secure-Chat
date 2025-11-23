using Server.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Server.Tests
{
    public class TextHelperTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void SanitizePlainText_EmptyOrWhitespace_ReturnsEmpty(string? input)
        {
            var result = TextHelper.SanitizePlainText(input, maxLength: 100, allowNewLines: false);

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void SanitizePlainText_TrimsToMaxLength()
        {
            var input = new string('a', 200);
            var result = TextHelper.SanitizePlainText(input, maxLength: 10, allowNewLines: false);

            Assert.Equal(10, result.Length);
            Assert.True(result.TrueForAll(c => c == 'a'));
        }

        [Fact]
        public void SanitizePlainText_RemovesControlCharacters()
        {
            var input = "Hello\u0001World\u0002!";
            var result = TextHelper.SanitizePlainText(input, maxLength: 100, allowNewLines: false);

            Assert.Equal("HelloWorld!", result);
        }

        [Fact]
        public void SanitizePlainText_AllowsNewLines_WhenConfigured()
        {
            var input = "Line1\r\nLine2\nLine3\rLine4";
            var result = TextHelper.SanitizePlainText(input, maxLength: 200, allowNewLines: true);

            Assert.Equal(input, result);
        }

        [Fact]
        public void SanitizePlainText_RemovesNewLines_WhenNotAllowed()
        {
            var input = "Line1\r\nLine2\nLine3\rLine4";
            var result = TextHelper.SanitizePlainText(input, maxLength: 200, allowNewLines: false);

            Assert.Equal("Line1Line2Line3Line4", result);
        }

        [Fact]
        public void SanitizePlainText_PreservesUnicodeLettersAndCommonSymbols()
        {
            var input = "Micha\u00ebl M\u00fcller \u2013 \u00ab\u0442\u0435\u0441\u0442\u00bb & Co. 12345 !?;:";
            var result = TextHelper.SanitizePlainText(input, maxLength: 200, allowNewLines: false);

            Assert.NotEmpty(result);
            Assert.Contains("Micha", result);
            Assert.Contains("Co.", result);
        }

        [Fact]
        public void SanitizePlainText_StripsHtmlTags_ForXssProtection()
        {
            var input = "<b>Michael</b><script>alert('xss');</script>";
            var result = TextHelper.SanitizePlainText(input, maxLength: 200, allowNewLines: false);

            Assert.False(ContainsHtmlTag(result));
            Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("</script", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SanitizePlainText_StripsPotentialImageXss()
        {
            var input = "<img src=x onerror=\"alert('xss')\"> hello";
            var result = TextHelper.SanitizePlainText(input, maxLength: 200, allowNewLines: false);

            Assert.False(ContainsHtmlTag(result));
        }

        [Fact]
        public void SanitizePlainText_LongXssPayload_DoesNotLeakTags()
        {
            var input = "<SCRIPT SRC=http://attacker/x.js></SCRIPT>" + new string('a', 100);
            var result = TextHelper.SanitizePlainText(input, maxLength: 300, allowNewLines: false);

            Assert.False(ContainsHtmlTag(result));
            Assert.DoesNotContain("<SCRIPT", result, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Very small helper to detect raw HTML tags like &lt;tag
        /// </summary>
        private static bool ContainsHtmlTag(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            for (int i = 0; i < input.Length - 1; i++)
            {
                if (input[i] == '<' && char.IsLetter(input[i + 1]))
                    return true;
            }

            return false;
        }
    }

    internal static class StringExtensions
    {
        public static bool TrueForAll(this string s, Func<char, bool> predicate)
        {
            foreach (var ch in s)
            {
                if (!predicate(ch)) return false;
            }
            return true;
        }
    }
}
