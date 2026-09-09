using System;
using System.Collections.Generic;

namespace ObjecTalk.Domain.Conversation
{
    /// <summary>
    /// Read-aloud question prompts shown on the Talk screen for a recognised object, keyed by its
    /// trimmed free-text category. A middle-schooler reads one out loud and answers it with
    /// push-to-talk; the prompts are display-only. An unknown or blank category returns nothing.
    /// </summary>
    public static class CategorySuggestions
    {
        private static readonly IReadOnlyList<string> None = Array.Empty<string>();

        private static readonly Dictionary<string, string[]> ByCategory =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["電子機器"] = new[]
                {
                    "これで何が作れる？",
                    "プログラミングってむずかしい？",
                    "AIってどうやって動いてるの？",
                },
                ["飲みもの"] = new[]
                {
                    "今日はこれ持っていったほうがいい？",
                    "これって体にいいの？",
                },
                ["傘"] = new[]
                {
                    "今日はこれ持っていったほうがいい？",
                    "明日の天気は？",
                },
                ["本"] = new[]
                {
                    "これに何が書いてあるの？",
                    "似た本ある？",
                    "どんな人におすすめ？",
                },
                ["文房具"] = new[]
                {
                    "これのじょうずな使い方は？",
                    "これで何ができる？",
                },
                ["アクセサリ"] = new[]
                {
                    "これってどうやって作るの？",
                    "どこで使うもの？",
                },
                ["その他"] = new[]
                {
                    "これ何だと思う？",
                    "これについて教えて",
                },
            };

        // The spellings an operator is likely to type instead of the canonical label. Kept in sync
        // with the backend's categoryPersona aliases.
        private static readonly Dictionary<string, string> Aliases =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["かさ"] = "傘",
                ["ほん"] = "本",
                ["のみもの"] = "飲みもの",
                ["飲み物"] = "飲みもの",
                ["ぶんぼうぐ"] = "文房具",
                ["アクセサリー"] = "アクセサリ",
                ["でんしきき"] = "電子機器",
            };

        public static IReadOnlyList<string> ForCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return None;
            }

            var key = category.Trim();
            if (Aliases.TryGetValue(key, out var canonical))
            {
                key = canonical;
            }

            return ByCategory.TryGetValue(key, out var prompts) ? prompts : None;
        }
    }
}
