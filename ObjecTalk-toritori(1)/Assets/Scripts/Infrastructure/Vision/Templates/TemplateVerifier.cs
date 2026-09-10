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

        /// <summary>
        /// Temporary instrumentation for the device tuning pass. The correlation score is computed
        /// here and nowhere else, and choosing a real MinimumTemplateScore means seeing what real
        /// frames produce. Off in normal use; turn it off again once the threshold is settled.
        /// </summary>
        public static bool LogScores;

        private readonly float _templateFraction;

        private byte[] _rgba;
        private bool _disposed;

        public TemplateVerifier()
            : this(TemplateMatch.DefaultTemplateFraction)
        {
        }

        /// <summary>
        /// <paramref name="templateFraction"/> is how much of the prepared view becomes the
        /// template. Smaller carries less of the surface the object was photographed on, which the
        /// device pass suggested was drowning the match; too small leaves too little of the object
        /// to be distinctive. Tunable because only the device can settle it.
        /// </summary>
        public TemplateVerifier(float templateFraction)
        {
            if (templateFraction <= 0f || templateFraction > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(templateFraction), "The template fraction must be within (0, 1].");
            }

            _templateFraction = templateFraction;
        }

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
                prepared, TemplateMatch.DefaultSearchSize, _templateFraction);
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
                out var score);

            if (LogScores)
            {
                Debug.Log($"[TrackDiag] correlation={score:F3} located={located} "
                    + $"pos=({relativeX:F3},{relativeY:F3}) threshold={thresholds.MinimumScore:F2}");
            }

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

            // A live camera frame is RGBA32; a registration snapshot decoded from JPEG is RGB24,
            // because a JPEG has no alpha to store. Both are ordinary inputs here.
            int bytesPerPixel;
            switch (image.format)
            {
                case TextureFormat.RGBA32:
                    bytesPerPixel = 4;
                    break;
                case TextureFormat.RGB24:
                    bytesPerPixel = 3;
                    break;
                default:
                    throw new ArgumentException(
                        $"Template frames must be RGBA32 or RGB24; got {image.format}.", nameof(image));
            }

            var width = image.width;
            var height = image.height;
            var stride = width * bytesPerPixel;
            var required = stride * height;

            NativeArray<byte> raw = image.GetRawTextureData<byte>();
            if (raw.Length != required)
            {
                throw new ArgumentException(
                    $"A {image.format} frame of {width}x{height} should be {required} bytes, not {raw.Length}.",
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
                TemplateMatch.DefaultSearchSize,
                bytesPerPixel);
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
