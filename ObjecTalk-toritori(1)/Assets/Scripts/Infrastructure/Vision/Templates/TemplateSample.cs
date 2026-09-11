using System;
using ObjecTalk.Core.Recognition;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Infrastructure.Vision
{
    /// <summary>
    /// One registration view reduced to the small grayscale patch <see cref="TemplateMatch"/>
    /// correlates against. Unlike an AKAZE sample it owns no native memory, so disposing it is a
    /// formality the interface requires rather than something that matters.
    /// </summary>
    internal sealed class TemplateSample : ILocalFeatureSample
    {
        // A single zero: LocalFeatureBlob insists on non-empty keypoint bytes, and a template has
        // no keypoints to record. NullLocalFeatureVerifier's sample does the same.
        private static readonly byte[] NoKeypoints = { 0 };

        private byte[] _pixels;

        internal TemplateSample(byte[] pixels, int size)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException(nameof(pixels));
            }

            if (size <= 0 || pixels.Length != size * size)
            {
                throw new ArgumentException(
                    "A template must be square and match its declared size.", nameof(pixels));
            }

            _pixels = pixels;
            Size = size;
        }

        /// <summary>Side length in pixels; the template is always square.</summary>
        internal int Size { get; }

        internal ReadOnlySpan<byte> Pixels => _pixels;

        public bool HasFeatures => _pixels != null && _pixels.Length > 0;

        public LocalFeatureBlob Serialize()
        {
            if (!HasFeatures)
            {
                throw new InvalidOperationException("This sample has been disposed.");
            }

            return new LocalFeatureBlob(
                NoKeypoints,
                _pixels,
                Size,
                Size,
                TemplateVerifier.TemplateDescriptorType,
                Size,
                Size);
        }

        public void Dispose()
        {
            _pixels = null;
        }
    }
}
