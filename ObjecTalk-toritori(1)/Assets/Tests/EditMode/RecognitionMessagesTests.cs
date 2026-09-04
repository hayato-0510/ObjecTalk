using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class RecognitionMessagesTests
    {
        [Test]
        public void RegistrationViewPrompt_ReadsAsNofTotalWithFramingGuidance()
        {
            var text = RecognitionMessages.RegistrationViewPrompt(1, 3);

            Assert.That(text, Does.Contain("1 / 3"));
            Assert.That(text, Does.Contain("枠"));
        }

        [Test]
        public void RegistrationAngleHint_IsNonEmpty()
        {
            Assert.That(RecognitionMessages.RegistrationAngleHint, Is.Not.Empty);
        }
    }
}
