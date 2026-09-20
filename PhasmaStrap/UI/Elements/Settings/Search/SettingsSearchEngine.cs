namespace PhasmaStrap.UI.Elements.Settings.Search
{
    internal sealed class SettingsSearchResult
    {
        public SettingsSearchEntry Entry { get; }

        public int Score { get; }

        public string Header => Entry.Header;

        public string Breadcrumb => Entry.Breadcrumb;

        public string Description => Entry.Description;

        public bool HasDescription => Entry.Description.Length > 0;

        public string KindLabel => Entry.Kind switch
        {
            SettingsSearchEntryKind.Option => "Setting",
            SettingsSearchEntryKind.Group => "Group",
            SettingsSearchEntryKind.Tab => "Tab",
            SettingsSearchEntryKind.Section => "Section",
            SettingsSearchEntryKind.Action => "Action",
            _ => "",
        };

        public SettingsSearchResult(SettingsSearchEntry entry, int score)
        {
            Entry = entry;
            Score = score;
        }
    }

    internal static class SettingsSearchEngine
    {
        public const int DefaultMaxResults = 40;

        public static List<SettingsSearchResult> Search(string query, int maxResults = DefaultMaxResults)
        {
            var results = new List<SettingsSearchResult>();

            string normalized = SettingsSearchEntry.Normalize(query ?? "");
            if (normalized.Length == 0)
                return results;

            string[] tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (SettingsSearchEntry entry in SettingsSearchIndex.Entries)
            {
                int total = 0;
                bool allMatched = true;

                foreach (string token in tokens)
                {
                    int score = ScoreToken(entry, token);
                    if (score == 0)
                    {
                        allMatched = false;
                        break;
                    }

                    total += score;
                }

                if (!allMatched)
                    continue;

                if (entry.NormalizedHeader == normalized)
                    total += 80;
                else if (entry.NormalizedHeader.StartsWith(normalized, StringComparison.Ordinal))
                    total += 40;
                else if (tokens.Length > 1 && entry.NormalizedHeader.Contains(normalized, StringComparison.Ordinal))
                    total += 25;

                total += entry.Kind switch
                {
                    SettingsSearchEntryKind.Option => 6,
                    SettingsSearchEntryKind.Group => 4,
                    SettingsSearchEntryKind.Tab => 3,
                    SettingsSearchEntryKind.Section => 2,
                    _ => 0,
                };

                results.Add(new SettingsSearchResult(entry, total));
            }

            results.Sort((a, b) =>
            {
                int byScore = b.Score.CompareTo(a.Score);
                if (byScore != 0)
                    return byScore;

                int byLength = a.Entry.Header.Length.CompareTo(b.Entry.Header.Length);
                if (byLength != 0)
                    return byLength;

                return string.Compare(a.Entry.Breadcrumb, b.Entry.Breadcrumb, StringComparison.Ordinal);
            });

            if (results.Count > maxResults)
                results.RemoveRange(maxResults, results.Count - maxResults);

            return results;
        }

        private static int ScoreToken(SettingsSearchEntry entry, string token)
        {
            int best = 0;

            if (entry.NormalizedHeader == token)
                best = Math.Max(best, 120);
            else if (entry.NormalizedHeader.StartsWith(token, StringComparison.Ordinal))
                best = Math.Max(best, 100);
            else if (StartsAnyWord(entry.HeaderWords, token))
                best = Math.Max(best, 85);
            else if (entry.NormalizedHeader.Contains(token, StringComparison.Ordinal))
                best = Math.Max(best, 65);

            if (best >= 85)
                return best;

            if (StartsAnyWord(entry.BreadcrumbWords, token))
                best = Math.Max(best, 40);
            else if (entry.NormalizedBreadcrumb.Contains(token, StringComparison.Ordinal))
                best = Math.Max(best, 30);

            if (entry.NormalizedDescription.Length > 0 && entry.NormalizedDescription.Contains(token, StringComparison.Ordinal))
                best = Math.Max(best, 22);

            if (best == 0 && token.Length >= 3 && IsSubsequence(token, entry.NormalizedHeader))
                best = 12;

            return best;
        }

        private static bool StartsAnyWord(string[] words, string token)
        {
            foreach (string word in words)
            {
                if (word.StartsWith(token, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool IsSubsequence(string needle, string haystack)
        {
            int n = 0;
            foreach (char c in haystack)
            {
                if (c == needle[n])
                {
                    n++;
                    if (n == needle.Length)
                        return true;
                }
            }

            return false;
        }
    }
}
