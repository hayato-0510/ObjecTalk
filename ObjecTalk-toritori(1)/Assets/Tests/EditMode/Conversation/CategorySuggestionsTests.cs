using System.Linq;
using NUnit.Framework;
using ObjecTalk.Domain.Conversation;

namespace ObjecTalk.Tests.EditMode.Conversation
{
    [TestFixture]
    public sealed class CategorySuggestionsTests
    {
        [TestCase("電子機器")]
        [TestCase("飲みもの")]
        [TestCase("傘")]
        [TestCase("本")]
        [TestCase("文房具")]
        [TestCase("アクセサリ")]
        [TestCase("その他")]
        public void ForCategory_KnownLabel_ReturnsTwoToFourDistinctPrompts(string label)
        {
            var prompts = CategorySuggestions.ForCategory(label);

            Assert.That(prompts.Count, Is.InRange(2, 4), label);
            Assert.That(prompts, Is.Unique, label);
            Assert.That(prompts, Has.All.Not.Null.And.All.Not.Empty, label);
        }

        [Test]
        public void ForCategory_TrimsBeforeLookup()
        {
            Assert.That(
                CategorySuggestions.ForCategory("  本 "),
                Is.EqualTo(CategorySuggestions.ForCategory("本")));
        }

        [TestCase("かさ", "傘")]
        [TestCase("ほん", "本")]
        [TestCase("のみもの", "飲みもの")]
        [TestCase("飲み物", "飲みもの")]
        [TestCase("ぶんぼうぐ", "文房具")]
        [TestCase("アクセサリー", "アクセサリ")]
        public void ForCategory_FoldsCommonSpellingsToTheCanonicalLabel(string alias, string canonical)
        {
            Assert.That(
                CategorySuggestions.ForCategory(alias),
                Is.EqualTo(CategorySuggestions.ForCategory(canonical)));
            Assert.That(CategorySuggestions.ForCategory(alias), Is.Not.Empty);
        }

        [Test]
        public void ForCategory_ElectronicsPromptsCoverMakingAndAi()
        {
            var joined = string.Join("\n", CategorySuggestions.ForCategory("電子機器"));

            Assert.That(joined, Does.Contain("作れる"));
            Assert.That(joined, Does.Contain("AI"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("すごい未知")]
        public void ForCategory_UnknownOrBlank_ReturnsEmpty(string label)
        {
            Assert.That(CategorySuggestions.ForCategory(label), Is.Empty);
        }
    }
}
