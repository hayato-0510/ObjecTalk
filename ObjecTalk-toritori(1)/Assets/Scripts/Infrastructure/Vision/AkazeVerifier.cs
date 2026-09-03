using System;
using ObjecTalk.Core.Recognition;
using ObjecTalk.Domain.Models;
using ObjecTalk.Domain.Recognition;
using OpenCvSharp;
using Unity.Collections;
using UnityEngine;

namespace ObjecTalk.Infrastructure.Vision
{
    /// <summary>
    /// AKAZE confirmation for the single best neural candidate. All working matrices and pixel
    /// buffers are allocated once per frame size and reused, and the incoming texture is the
    /// explicitly prepared verification frame - never a camera texture cast to
    /// <see cref="Texture2D"/>.
    /// </summary>
    public sealed class AkazeVerifier : ILocalFeatureVerifier
    {
        private readonly float _maximumDescriptorDistance;

        private AKAZE _detector;
        private BFMatcher _matcher;

        private byte[] _pixelBuffer;
        private Mat _sourceMat;
        private Mat _orientedMat;
        private Mat _grayMat;
        private Mat _liveDescriptors;
        private float[] _distanceBuffer;
        private int _bufferWidth;
        private int _bufferHeight;
        private bool _disposed;

        public AkazeVerifier()
            : this(LocalFeatureMatchPolicy.DefaultMaximumDescriptorDistance)
        {
        }

        public AkazeVerifier(float maximumDescriptorDistance)
        {
            if (float.IsNaN(maximumDescriptorDistance)
                || float.IsInfinity(maximumDescriptorDistance)
                || maximumDescriptorDistance <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumDescriptorDistance),
                    "The descriptor distance budget must be finite and greater than zero.");
            }

            _maximumDescriptorDistance = maximumDescriptorDistance;
        }

        public void Initialize()
        {
            ThrowIfDisposed();
            if (_detector != null)
            {
                return;
            }

            _detector = AKAZE.Create();
            _matcher = new BFMatcher(NormTypes.Hamming);
            _liveDescriptors = new Mat();
        }

        public LocalFeatureSample Extract(Texture2D image)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();
            ValidateImage(image);

            UpdateGrayscale(image);

            var descriptors = new Mat();
            try
            {
                _detector.DetectAndCompute(_grayMat, null, out var keypoints, descriptors);
                return new LocalFeatureSample(keypoints, descriptors, new Size(image.width, image.height));
            }
            catch
            {
                descriptors.Dispose();
                throw;
            }
        }

        public bool Verify(Texture2D image, LocalFeatureSample reference, int minimumMatches)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();
            ValidateImage(image);

            if (reference == null)
            {
                throw new ArgumentNullException(nameof(reference));
            }

            if (minimumMatches < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumMatches),
                    "Verification requires at least one matching descriptor.");
            }

            if (!reference.HasFeatures)
            {
                return false;
            }

            UpdateGrayscale(image);
            _detector.DetectAndCompute(_grayMat, null, out var liveKeypoints, _liveDescriptors);
            if (liveKeypoints.Length == 0 || _liveDescriptors.Empty())
            {
                return false;
            }

            // Brute-force matching requires identical descriptor layouts; a mismatch means the
            // reference was stored by a different extractor and cannot confirm this frame.
            if (reference.Descriptors.Type() != _liveDescriptors.Type()
                || reference.Descriptors.Cols != _liveDescriptors.Cols)
            {
                return false;
            }

            var matches = _matcher.Match(reference.Descriptors, _liveDescriptors);
            EnsureDistanceBuffer(matches.Length);
            for (var i = 0; i < matches.Length; i++)
            {
                _distanceBuffer[i] = matches[i].Distance;
            }

            return LocalFeatureMatchPolicy.IsVerified(
                new ReadOnlySpan<float>(_distanceBuffer, 0, matches.Length),
                _maximumDescriptorDistance,
                minimumMatches);
        }

        /// <summary>
        /// Rebuilds a persisted capture. Returns null for a payload whose declared geometry does
        /// not match its bytes, so one corrupt row cannot stop the rest of the database loading.
        /// </summary>
        public LocalFeatureSample Restore(LocalFeatureBlob blob)
        {
            ThrowIfDisposed();

            var keypoints = LocalFeatureCodec.DeserializeKeypoints(blob.Keypoints);
            if (keypoints == null || keypoints.Length == 0)
            {
                return null;
            }

            var descriptors = LocalFeatureCodec.DeserializeDescriptors(blob);
            if (descriptors == null)
            {
                return null;
            }

            if (descriptors.Rows != keypoints.Length)
            {
                descriptors.Dispose();
                return null;
            }

            try
            {
                return new LocalFeatureSample(
                    keypoints,
                    descriptors,
                    new Size(blob.ImageWidth, blob.ImageHeight));
            }
            catch (ArgumentException)
            {
                // The sample constructor disposes the matrix before it throws.
                return null;
            }
        }

        ILocalFeatureSample ILocalFeatureVerifier.Extract(Texture2D image)
        {
            return Extract(image);
        }

        ILocalFeatureSample ILocalFeatureVerifier.Restore(LocalFeatureBlob blob)
        {
            return Restore(blob);
        }

        bool ILocalFeatureVerifier.Verify(Texture2D image, ILocalFeatureSample reference, int minimumMatches)
        {
            if (reference == null)
            {
                throw new ArgumentNullException(nameof(reference));
            }

            // Only this verifier's own samples carry the OpenCV matrix matching needs.
            return reference is LocalFeatureSample sample && Verify(image, sample, minimumMatches);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseImageBuffers();
            _liveDescriptors?.Dispose();
            _liveDescriptors = null;
            _matcher?.Dispose();
            _matcher = null;
            _detector?.Dispose();
            _detector = null;
            _distanceBuffer = null;
        }

        /// <summary>
        /// Copies the texture's raw RGBA bytes into the reusable matrix header, flips it into
        /// OpenCV's top-down row order, and converts it to the grayscale matrix AKAZE consumes.
        /// </summary>
        private void UpdateGrayscale(Texture2D image)
        {
            EnsureImageBuffers(image.width, image.height);

            NativeArray<byte> raw = image.GetRawTextureData<byte>();
            if (raw.Length != _pixelBuffer.Length)
            {
                throw new ArgumentException(
                    $"Verification frames must be {nameof(TextureFormat.RGBA32)}; got {image.format}.",
                    nameof(image));
            }

            raw.CopyTo(_pixelBuffer);
            Cv2.Flip(_sourceMat, _orientedMat, FlipMode.X);
            Cv2.CvtColor(_orientedMat, _grayMat, ColorConversionCodes.RGBA2GRAY);
        }

        private void EnsureImageBuffers(int width, int height)
        {
            if (_sourceMat != null && _bufferWidth == width && _bufferHeight == height)
            {
                return;
            }

            ReleaseImageBuffers();

            // The Mat header pins and points at the managed buffer, so filling the buffer is
            // enough to update the matrix; no per-frame copy into unmanaged memory is needed.
            _pixelBuffer = new byte[checked(width * height * 4)];
            _sourceMat = new Mat(height, width, MatType.CV_8UC4, _pixelBuffer);
            _orientedMat = new Mat(height, width, MatType.CV_8UC4);
            _grayMat = new Mat(height, width, MatType.CV_8UC1);
            _bufferWidth = width;
            _bufferHeight = height;
        }

        private void EnsureDistanceBuffer(int length)
        {
            if (_distanceBuffer == null || _distanceBuffer.Length < length)
            {
                _distanceBuffer = new float[Math.Max(length, 256)];
            }
        }

        private void ReleaseImageBuffers()
        {
            _grayMat?.Dispose();
            _grayMat = null;
            _orientedMat?.Dispose();
            _orientedMat = null;
            _sourceMat?.Dispose();
            _sourceMat = null;
            _pixelBuffer = null;
            _bufferWidth = 0;
            _bufferHeight = 0;
        }

        private static void ValidateImage(Texture2D image)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            if (image.width <= 0 || image.height <= 0)
            {
                throw new ArgumentException("The verification frame has no pixels.", nameof(image));
            }

            if (image.width > ImageGeometry.MaximumVerificationImageSize
                || image.height > ImageGeometry.MaximumVerificationImageSize)
            {
                throw new ArgumentException(
                    $"Verification frames are bounded to {ImageGeometry.MaximumVerificationImageSize} pixels on each side; "
                    + $"got {image.width}x{image.height}.",
                    nameof(image));
            }
        }

        private void ThrowIfNotInitialized()
        {
            if (_detector == null)
            {
                throw new InvalidOperationException($"{nameof(AkazeVerifier)}.{nameof(Initialize)} was not called.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AkazeVerifier));
            }
        }
    }
}
