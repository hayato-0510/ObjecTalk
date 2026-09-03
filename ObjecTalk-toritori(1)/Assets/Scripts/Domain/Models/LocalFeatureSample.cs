using System;
using ObjecTalk.Core.Recognition;
using ObjecTalk.Domain.Recognition;
using ObjecTalk.Infrastructure.Vision;
using OpenCvSharp;

namespace ObjecTalk.Domain.Models
{
    /// <summary>
    /// AKAZE keypoints and descriptors extracted from one bounded verification image.
    /// The instance owns <see cref="Descriptors"/> and must be disposed by whoever created it.
    /// </summary>
    public sealed class LocalFeatureSample : ILocalFeatureSample
    {
        private Mat _descriptors;

        /// <summary>Takes ownership of <paramref name="descriptors"/>.</summary>
        public LocalFeatureSample(KeyPoint[] keypoints, Mat descriptors, Size imageSize)
        {
            if (keypoints == null)
            {
                throw new ArgumentNullException(nameof(keypoints));
            }

            if (descriptors == null)
            {
                throw new ArgumentNullException(nameof(descriptors));
            }

            if (imageSize.Width <= 0
                || imageSize.Height <= 0
                || imageSize.Width > ImageGeometry.MaximumVerificationImageSize
                || imageSize.Height > ImageGeometry.MaximumVerificationImageSize)
            {
                descriptors.Dispose();
                throw new ArgumentOutOfRangeException(
                    nameof(imageSize),
                    $"Verification images must be between 1 and {ImageGeometry.MaximumVerificationImageSize} pixels on each side.");
            }

            Keypoints = keypoints;
            _descriptors = descriptors;
            ImageSize = imageSize;
        }

        public KeyPoint[] Keypoints { get; }

        public Mat Descriptors => _descriptors;

        public Size ImageSize { get; }

        /// <summary>False when the capture is featureless and must be retried.</summary>
        public bool HasFeatures =>
            Keypoints.Length > 0 && _descriptors != null && !_descriptors.IsDisposed && !_descriptors.Empty();

        /// <summary>
        /// The persistable payload. Only a capture that found features can be serialized; the
        /// registration flow rejects a featureless capture long before it reaches this point.
        /// </summary>
        public LocalFeatureBlob Serialize()
        {
            if (!HasFeatures)
            {
                throw new InvalidOperationException("A featureless capture cannot be serialized.");
            }

            return new LocalFeatureBlob(
                LocalFeatureCodec.SerializeKeypoints(Keypoints),
                LocalFeatureCodec.SerializeDescriptors(_descriptors),
                _descriptors.Rows,
                _descriptors.Cols,
                _descriptors.Type(),
                ImageSize.Width,
                ImageSize.Height);
        }

        public void Dispose()
        {
            var descriptors = _descriptors;
            _descriptors = null;
            descriptors?.Dispose();
        }
    }
}
