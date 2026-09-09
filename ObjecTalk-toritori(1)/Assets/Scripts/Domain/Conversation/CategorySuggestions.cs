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

        public static IReadOnlyList<string> ForCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return None;
            }

            return ByCategory.TryGetValue(category.Trim(), out var prompts) ? prompts : None;
        }
    }
}
