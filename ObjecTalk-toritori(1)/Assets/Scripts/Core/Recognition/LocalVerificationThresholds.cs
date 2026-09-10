using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Core.Recognition
{
    /// <summary>
    /// Everything a local-feature verifier is allowed to know about the settings. Handing over the
    /// whole <see cref="RecognitionSettings"/> would also show it the similarity threshold and the
    /// tick cadence, which are not its business.
    /// </summary>
    public readonly struct LocalVerificationThresholds
    {
        public LocalVerificationThresholds(int minimumMatches, float minimumScore, bool gatesRecognition)
        {
            MinimumMatches = minimumMatches;
            MinimumScore = minimumScore;
            GatesRecognition = gatesRecognition;
        }

        public static LocalVerificationThresholds From(in RecognitionSettings settings)
            => new LocalVerificationThresholds(
                settings.MinimumLocalMatchCount,
                settings.MinimumTemplateScore,
                settings.TemplateGatesRecognition);

        /// <summary>Close descriptors AKAZE needs before it confirms.</summary>
        public int MinimumMatches { get; }

        /// <summary>Correlation below which a template position is not worth reporting.</summary>
        public float MinimumScore { get; }

        /// <summary>
        /// When false a verifier may withhold a position but must still confirm the candidate, so
        /// recognition rests on the embedding exactly as it does today.
        /// </summary>
        public bool GatesRecognition { get; }
    }
}
