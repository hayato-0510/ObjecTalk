using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// What the registration UI should read for a given number of captured views: the status line,
    /// the angle instruction, and how many progress dots are lit. Engine-free so the progress rule
    /// is testable without a canvas (Ruling T2-1b — no UnityEngine types in this assembly); the
    /// MonoBehaviour only applies the result to its optional text and image references.
    /// </summary>
    public readonly struct RegistrationProgressView
    {
        private RegistrationProgressView(string status, string instruction, int litDots)
        {
            Status = status;
            Instruction = instruction;
            LitDots = litDots;
        }

        /// <summary>Which view the user is about to take, or the completion message.</summary>
        public string Status { get; }

        /// <summary>Angle guidance, shown from the second view onward; empty before that.</summary>
        public string Instruction { get; }

        /// <summary>Progress dots to light, clamped to 0..<c>totalViews</c>.</summary>
        public int LitDots { get; }

        public static RegistrationProgressView For(int capturedViews, int totalViews)
        {
            if (capturedViews < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capturedViews), "Captured views cannot be negative.");
            }

            if (totalViews < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(totalViews), "At least one view is required.");
            }

            var status = capturedViews >= totalViews
                ? RecognitionMessages.RegistrationComplete
                : RecognitionMessages.RegistrationViewPrompt(Math.Min(capturedViews + 1, totalViews), totalViews);

            // The hint only makes sense once there is an earlier view to differ from.
            var instruction = capturedViews >= 1 ? RecognitionMessages.RegistrationAngleHint : string.Empty;

            return new RegistrationProgressView(status, instruction, Math.Min(capturedViews, totalViews));
        }
    }
}
