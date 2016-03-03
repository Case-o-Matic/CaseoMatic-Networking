using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Caseomatic.Net.Utility
{
    // The string compression should work this way ("abbccc" => "ab2c3"). Also compressing
    // a string without marking it in some way could lead to problems if players write
    // some character and a number directly behind it, which is then recognized as a compressed string
    // For example: "gr8 b8 m8" => "grrrrrrrr bbbbbbbb mmmmmmmm"
    // A way to solve this would be to insert a character at the number index:
    // "gr/8 b/8 m/8", the compression algorithm would then only decompress such characters.
    // Contra of this method is that two- and three word compressing will be useless,
    // because "a/2" > "aa" and "a/3" = "aaa".
    public static class StringCompressor
    {
        private const char compressSignChar = '/';
        private static string pattern, antiPattern;

        static StringCompressor()
        {
            var alphabet = Enumerable.Range(97, 26).Select(i => (char)i + "+").ToArray();
            pattern = "(" + string.Join("|", alphabet) + ")";
            antiPattern = @"([a-zA-Z])\" + compressSignChar + "([0-9]+$)";
        }

        public static string Compress(string originalString)
        {
            var compressed =
                Regex.Matches(originalString, pattern)
                .Cast<Match>().Select(m => new
                {
                    Char = m.Groups[1].Value[0],
                    Count = m.Groups[1].Value
                })
                .Aggregate(string.Empty, (result, nextGroup) =>
                   result.ToString() + nextGroup.Char);

            return compressed;
        }

        public static string Decompress(string compressedString)
        {
            var decompressed =
                Regex.Matches(compressedString, antiPattern)
                .Cast<Match>().Select(m => new
                {
                    Char = m.Groups[1].Value[0],
                    Count = int.Parse(m.Groups[1].Value)
                })
                .Aggregate(string.Empty, (result, nextGroup) =>
                result.ToString() + Enumerable.Repeat(nextGroup.Char, nextGroup.Count));

            return decompressed;
        }
    }
}
