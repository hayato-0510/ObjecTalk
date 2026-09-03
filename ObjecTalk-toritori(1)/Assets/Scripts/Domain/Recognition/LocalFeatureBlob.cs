using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// One registration capture's AKAZE payload in the form the database stores it: opaque
    /// keypoint and descriptor bytes plus the metadata needed to rebuild the matrix safely.
    /// The type is deliberately free of OpenCV and of Unity, so the recognition orchestrator can
    /// move local features between the verifier and the repository without depending on either.
    /// </summary>
    [Serializable]
    public readonly struct LocalFeatureBlob
    {
        public LocalFeatureBlob(
            byte[] keypoints,
            byte[] descriptors,
            int descriptorRows,
            int descriptorColumns,
            int descriptorType,
            int imageWidth,
            int imageHeight)
        {
            if (keypoints == null || keypoints.Length == 0)
            {
                throw new ArgumentException("A local feature blob needs serialized keypoints.", nameof(keypoints));
            }

            if (descriptors == null || descriptors.Length == 0)
            {
                throw new ArgumentException("A local feature blob needs serialized descriptors.", nameof(descriptors));
            }

            if (descriptorRows <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptorRows), "Descriptor rows must be positive.");
            }

            if (descriptorColumns <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptorColumns), "Descriptor columns must be positive.");
            }

            if (descriptorType < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptorType), "Descriptor types cannot be negative.");
            }

            ThrowIfOutOfBounds(imageWidth, nameof(imageWidth));
            ThrowIfOutOfBounds(imageHeight, nameof(imageHeight));

            // Copy so a caller reusing its scratch buffers cannot mutate a blob it already handed over.
            Keypoints = (byte[])keypoints.Clone();
            Descriptors = (byte[])descriptors.Clone();
            DescriptorRows = descriptorRows;
            DescriptorColumns = descriptorColumns;
            DescriptorType = descriptorType;
            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
        }

        public byte[] Keypoints { get; }

        public byte[] Descriptors { get; }

        public int DescriptorRows { get; }

        public int DescriptorColumns { get; }

        public int DescriptorType { get; }

        public int ImageWidth { get; }

        public int ImageHeight { get; }

        /// <summary>False for a default instance, which carries no payload.</summary>
        public bool HasPayload => Keypoints != null && Descriptors != null;

        private static void ThrowIfOutOfBounds(int size, string paramName)
        {
            if (size <= 0 || size > ImageGeometry.MaximumVerificationImageSize)
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    $"Verification images must be between 1 and {ImageGeometry.MaximumVerificationImageSize} pixels on each side.");
            }
        }
    }
}
