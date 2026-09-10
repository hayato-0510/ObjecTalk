using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// Locates a registered object by normalised cross-correlation. Correlation is invariant to
    /// gain and offset, so a hall light that is dimmer or warmer than the one the object was
    /// registered under costs nothing; the scale list absorbs the visitor moving the object
    /// nearer or further. Engine-free for the same reason as <see cref="FeatureCentroid"/>.
    /// </summary>
    public static class TemplateMatch
    {
        /// <summary>Search resolution. Accuracy is flat from 320 down to 48; 64 leaves margin.</summary>
        public const int DefaultSearchSize = 64;

        /// <summary>The template is the middle of the prepared view, where the object is.</summary>
        public const float DefaultTemplateFraction = 0.5f;

        /// <summary>Distance changes the search covers, roughly half to one-and-a-half.</summary>
        public static readonly float[] DefaultScales = { 0.6f, 0.8f, 1.0f, 1.25f, 1.6f };

        private const int MinimumScaledTemplateSize = 4;

        /// <summary>
        /// Best correlation peak across <paramref name="scales"/>, reported as a 0..1
        /// bottom-left-origin position. False when nothing clears <paramref name="minimumScore"/>,
        /// in which case the caller keeps its previous placement rather than believing a guess.
        /// </summary>
        public static bool TryLocate(
            ReadOnlySpan<byte> template,
            int templateSize,
            ReadOnlySpan<byte> query,
            int querySize,
            ReadOnlySpan<float> scales,
            float minimumScore,
            out float relativeX,
            out float relativeY,
            out float score)
        {
            relativeX = 0f;
            relativeY = 0f;
            score = 0f;

            if (templateSize <= 0 || querySize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(templateSize), "Sizes must be positive.");
            }

            if (template.Length != templateSize * templateSize)
            {
                throw new ArgumentException(
                    "The template pixel count does not match its declared size.", nameof(template));
            }

            if (query.Length != querySize * querySize)
            {
                throw new ArgumentException(
                    "The query pixel count does not match its declared size.", nameof(query));
            }

            var scaled = new byte[querySize * querySize];
            var bestScore = float.NegativeInfinity;
            var bestX = 0f;
            var bestY = 0f;
            var located = false;

            for (var s = 0; s < scales.Length; s++)
            {
                var size = (int)Math.Round(templateSize * (double)scales[s]);
                if (size < MinimumScaledTemplateSize || size > querySize)
                {
                    continue;
                }

                Resample(template, templateSize, scaled, size);
                if (!BestPeak(scaled, size, query, querySize, out var x, out var y, out var peak))
                {
                    continue;
                }

                if (peak > bestScore)
                {
                    bestScore = peak;
                    bestX = x + (size / 2f);
                    bestY = y + (size / 2f);
                    located = true;
                }
            }

            if (!located || bestScore < minimumScore)
            {
                return false;
            }

            score = bestScore;
            relativeX = bestX / querySize;
            relativeY = 1f - (bestY / querySize);   // pixels run down, our convention runs up
            return true;
        }

        /// <summary>Nearest-neighbour box resample. Good enough: the search is already coarse.</summary>
        private static void Resample(
            ReadOnlySpan<byte> source, int sourceSize, byte[] destination, int destinationSize)
        {
            for (var y = 0; y < destinationSize; y++)
            {
                var sy = y * sourceSize / destinationSize;
                for (var x = 0; x < destinationSize; x++)
                {
                    var sx = x * sourceSize / destinationSize;
                    destination[(y * destinationSize) + x] = source[(sy * sourceSize) + sx];
                }
            }
        }

        private static bool BestPeak(
            byte[] template, int size,
            ReadOnlySpan<byte> query, int querySize,
            out int bestX, out int bestY, out float bestScore)
        {
            bestX = 0;
            bestY = 0;
            bestScore = float.NegativeInfinity;

            var count = size * size;
            double sum = 0d;
            for (var i = 0; i < count; i++)
            {
                sum += template[i];
            }

            var templateMean = sum / count;
            double templateVariance = 0d;
            for (var i = 0; i < count; i++)
            {
                var d = template[i] - templateMean;
                templateVariance += d * d;
            }

            // A flat template correlates with everything; refuse rather than pick a random peak.
            if (templateVariance <= double.Epsilon)
            {
                return false;
            }

            var span = querySize - size;
            var found = false;
            for (var oy = 0; oy <= span; oy++)
            {
                for (var ox = 0; ox <= span; ox++)
                {
                    double windowSum = 0d;
                    for (var y = 0; y < size; y++)
                    {
                        var row = ((oy + y) * querySize) + ox;
                        for (var x = 0; x < size; x++)
                        {
                            windowSum += query[row + x];
                        }
                    }

                    var windowMean = windowSum / count;
                    double cross = 0d;
                    double windowVariance = 0d;
                    for (var y = 0; y < size; y++)
                    {
                        var row = ((oy + y) * querySize) + ox;
                        var trow = y * size;
                        for (var x = 0; x < size; x++)
                        {
                            var q = query[row + x] - windowMean;
                            cross += (template[trow + x] - templateMean) * q;
                            windowVariance += q * q;
                        }
                    }

                    if (windowVariance <= double.Epsilon)
                    {
                        continue;
                    }

                    var value = (float)(cross / Math.Sqrt(templateVariance * windowVariance));
                    if (value > bestScore)
                    {
                        bestScore = value;
                        bestX = ox;
                        bestY = oy;
                        found = true;
                    }
                }
            }

            return found;
        }
    }
}
