using System;
using ObjecTalk.Core.Recognition;
using ObjecTalk.Domain.Recognition;
using Unity.Collections;
using UnityEngine;

namespace ObjecTalk.Infrastructure.Vision
{
    /// <summary>
    /// Confirms and locates a registered object by correlating a small grayscale template against
    /// the live view. Unlike <see cref="AkazeVerifier"/> this needs no native library, so it is the
    /// only backend that actually runs on the ARM64 player the realtime voice conversation forces
    /// (OpenCV+Unity ships armeabi-v7a only, com.unity.webrtc arm64-v8a only, and Android installs
    /// one ABI per APK).
    /// </summary>
    public sealed class TemplateVerifier : ILocalFeatureVerifier
    {
        /// <summary>
        /// Marks a persisted blob as a template rather than AKAZE descriptors. Rows written by one
        /// backend are meaningless to the other, and reading them anyway yields a confident
        /// position from noise - worse than reporting none.
        ///
        /// This has to be a real OpenCV Mat type, not an arbitrary tag: the repository reads
        /// descriptor_type as one and derives the expected blob length from its depth and channel
        /// count, so an invented value fails validation and the registration is refused. CV_8S is
        /// one byte per element like a grayscale pixel, and AKAZE only ever writes CV_8U, so the
        /// two remain distinguishable.
        /// </summary>
        public const int TemplateDescriptorType = 1;   // CV_8S: depth 1, one channel

        private byte[] _rgba;
        private bool _disposed;

        public void Initialize()
        {
            ThrowIfDisposed();
        }

        /// <summary>
        /// Builds a reference from a registration view: the centre of the frame, where the capture
        /// guide put the object.
        /// </summary>
        public ILocalFeatureSample Extract(Texture2D image)
        {
            ThrowIfDisposed();
            var prepared = Prepare(image);
            var template = TemplatePreparation.CentreCrop(
                prepared, TemplateMatch.DefaultSearchSize, TemplateMatch.DefaultTemplateFraction);
            var side = (int)Math.Sqrt(template.Length);
            return new TemplateSample(template, side);
        }

        /// <summary>
        /// Rebuilds a persisted template. Returns null for a blob this backend did not write, so
        /// <see cref="ObjectRecognitionService"/> keeps the object's embedding and lets a seeded
        /// template take over.
        /// </summary>
        public ILocalFeatureSample Restore(LocalFeatureBlob blob)
        {
            ThrowIfDisposed();
            if (!blob.HasPayload || blob.DescriptorType != TemplateDescriptorType)
            {
                return null;
            }

            var side = blob.DescriptorRows;
            if (side <= 0 || blob.DescriptorColumns != side || blob.Descriptors.Length != side * side)
            {
                return null;
            }

            return new TemplateSample((byte[])blob.Descriptors.Clone(), side);
        }

        public LocalVerificationResult Verify(
            Texture2D image, ILocalFeatureSample reference, in LocalVerificationThresholds thresholds)
        {
            ThrowIfDisposed();
            if (reference == null)
            {
                throw new ArgumentNullException(nameof(reference));
            }

            // Only this backend's own samples carry a template.
            if (!(reference is TemplateSample template) || !template.HasFeatures)
            {
                return Unlocated(thresholds);
            }

            var query = Prepare(image);
            var located = TemplateMatch.TryLocate(
                template.Pixels,
                template.Size,
                query,
                TemplateMatch.DefaultSearchSize,
                TemplateMatch.DefaultScales,
                thresholds.MinimumScore,
                out var relativeX,
                out var relativeY,
                out _);

            return located
                ? LocalVerificationResult.VerifiedAt(relativeX, relativeY)
                : Unlocated(thresholds);
        }

        public void Dispose()
        {
            _disposed = true;
            _rgba = null;
        }

        /// <summary>
        /// A match too weak to place costs the object its position. Whether it also costs the
        /// object its identity is the caller's policy, not this class's, and it stays off until
        /// real device scores have been measured.
        /// </summary>
        private static LocalVerificationResult Unlocated(in LocalVerificationThresholds thresholds)
            => thresholds.GatesRecognition
                ? LocalVerificationResult.Rejected
                : LocalVerificationResult.Verified();

        /// <summary>
        /// Copies a texture into the square grayscale view both sides of the comparison use.
        /// Unity hands over rows bottom-up; <see cref="TemplatePreparation"/> documents row zero as
        /// the top, so the rows are reversed here - the same correction
        /// <see cref="AkazeVerifier"/> makes with a vertical flip.
        /// </summary>
        private byte[] Prepare(Texture2D image)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            var width = image.width;
            var height = image.height;
            var stride = width * 4;
            var required = stride * height;

            NativeArray<byte> raw = image.GetRawTextureData<byte>();
            if (raw.Length != required)
            {
                throw new ArgumentException(
                    $"Template frames must be {nameof(TextureFormat.RGBA32)}; got {image.format}.",
                    nameof(image));
            }

            if (_rgba == null || _rgba.Length < required)
            {
                _rgba = new byte[required];
            }

            for (var y = 0; y < height; y++)
            {
                NativeArray<byte>.Copy(raw, (height - 1 - y) * stride, _rgba, y * stride, stride);
            }

            return TemplatePreparation.ToGrayscaleSquare(
                new ReadOnlySpan<byte>(_rgba, 0, required),
                width,
                height,
                ImageGeometry.DefaultCropFraction,
                TemplateMatch.DefaultSearchSize);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TemplateVerifier));
            }
        }
    }
}
