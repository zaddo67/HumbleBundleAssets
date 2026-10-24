using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HumbleBundleAssets.Services
{
    public enum SearchMode
    {
        All,        // All words anywhere (substring AND)
        Words,      // Whole words AND (boundary matching on space, _, -, camelCase, numbers, symbols)
        Phrase,     // Exact contiguous string match
        AnyWord,    // Any word anywhere (substring OR)
        Advanced    // Wildcards (*, ?), negations (-term), AND combination
    }

    public static class SearchMatcher
    {
        private static readonly Regex CamelCaseSplitRegex = new(
            @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|(?<=[a-zA-Z])(?=[0-9])|(?<=[0-9])(?=[a-zA-Z])",
            RegexOptions.Compiled);

        private static readonly Regex NonAlphaNumRegex = new(
            @"[^a-zA-Z0-9]+",
            RegexOptions.Compiled);

        public static bool Matches(string? fileName, string? innerPath, string query, SearchMode mode)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;

            var target1 = fileName ?? string.Empty;
            var target2 = innerPath ?? string.Empty;
            var combined = string.IsNullOrEmpty(target2) ? target1 : $"{target1} {target2}";

            return mode switch
            {
                SearchMode.Phrase => MatchesPhrase(target1, target2, query),
                SearchMode.Words => MatchesWords(combined, query),
                SearchMode.All => MatchesAll(combined, query),
                SearchMode.AnyWord => MatchesAnyWord(combined, query),
                SearchMode.Advanced => MatchesAdvanced(combined, query),
                _ => MatchesAll(combined, query)
            };
        }

        /// <summary>
        /// Exact contiguous substring match (case-insensitive)
        /// </summary>
        public static bool MatchesPhrase(string target1, string target2, string query)
        {
            var q = query.Trim();
            return target1.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                   target2.Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// All words must appear anywhere (case-insensitive substring for each token)
        /// </summary>
        public static bool MatchesAll(string combined, string query)
        {
            var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0) return true;

            foreach (var token in tokens)
            {
                if (!combined.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Any word appears (case-insensitive substring OR)
        /// </summary>
        public static bool MatchesAnyWord(string combined, string query)
        {
            var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0) return true;

            foreach (var token in tokens)
            {
                if (combined.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whole words AND search.
        /// Treats spaces, _, -, numbers, symbols and CamelCase transitions as word boundaries.
        /// </summary>
        public static bool MatchesWords(string combined, string query)
        {
            var queryWords = TokenizeWords(query);
            if (queryWords.Count == 0) return true;

            var targetWords = new HashSet<string>(TokenizeWords(combined), StringComparer.OrdinalIgnoreCase);

            foreach (var qWord in queryWords)
            {
                if (!targetWords.Contains(qWord))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Advanced search supporting wildcards (*, ?) and negative exclusions (-word).
        /// </summary>
        public static bool MatchesAdvanced(string combined, string query)
        {
            var rawTokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (rawTokens.Length == 0) return true;

            var positiveTokens = new List<string>();
            var negativeTokens = new List<string>();

            foreach (var t in rawTokens)
            {
                if (t.StartsWith('-') && t.Length > 1)
                {
                    negativeTokens.Add(t.Substring(1));
                }
                else
                {
                    positiveTokens.Add(t);
                }
            }

            // Exclusions: if any negative token matches, exclude this item
            foreach (var neg in negativeTokens)
            {
                if (WildcardMatch(combined, neg))
                {
                    return false;
                }
            }

            // Inclusions: all positive tokens must match
            foreach (var pos in positiveTokens)
            {
                if (!WildcardMatch(combined, pos))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool WildcardMatch(string text, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;

            if (!pattern.Contains('*') && !pattern.Contains('?'))
            {
                return text.Contains(pattern, StringComparison.OrdinalIgnoreCase);
            }

            var regexPattern = "^.*" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + ".*$";
            return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
        }

        public static List<string> TokenizeWords(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new List<string>();

            // 1. Separate CamelCase transitions (e.g. "BoyToy" -> "Boy Toy", "Walk01" -> "Walk 01")
            var separated = CamelCaseSplitRegex.Replace(input, " ");

            // 2. Replace any non-alphanumeric characters (including _, -, ., /, \, punctuation, symbols) with spaces
            var cleaned = NonAlphaNumRegex.Replace(separated, " ");

            // 3. Extract tokens
            var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return words.ToList();
        }
    }
}
