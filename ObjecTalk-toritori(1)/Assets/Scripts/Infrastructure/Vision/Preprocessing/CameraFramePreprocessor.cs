using System;
using ObjecTalk.Domain.Recognition;
using UnityEngine;

namespace ObjecTalk.Infrastructure.Vision
{
    /// <summary>
    /// Turns a live camera frame into the two images the recognition cycle needs: a GPU-resident
    /// 224x224 square for the embedding model and a CPU-readable square of at most 320 pixels for
    /// AKAZE. Both buffers are allocated once and reused, so the steady-state loop performs no
    /// per-frame image allocation, and the source is never cast to <see cref="Texture2D"/>.
    /// </summary>
    public sealed class CameraFramePreprocessor : IDisposable
    {
        private readonly int _embeddingSize;
        private readonly int _maximumVerificationSize;

        private RenderTexture _embeddingTexture;
        private RenderTexture _verificationSurface;
        private Texture2D _verificationFrame;
        private int _verificationSize;
        private bool _disposed;

        public CameraFramePreprocessor()
            : this(ImageGeometry.EmbeddingImageSize, ImageGeometry.MaximumVerificationImageSize)
        {
        }

        public CameraFramePreprocessor(int embeddingSize, int maximumVerificationSize)
        {
            if (embeddingSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(embeddingSize), "The embedding size must be positive.");
            }

            if (maximumVerificationSize <= 0 || maximumVerificationSize > ImageGeometry.MaximumVerificationImageSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumVerificationSize),
                    $"The verification size must be between 1 and {ImageGeometry.MaximumVerificationImageSize}.");
            }

            _embeddingSize = embeddingSize;
            _maximumVerificationSize = maximumVerificationSize;
        }

        /// <summary>Side length of the square handed to the embedding model.</summary>
        public int EmbeddingSize => _embeddingSize;

        /// <summary>
        /// Side length of the verification frame, or zero until the first frame determines it.
        /// </summary>
        public int VerificationSize => _verificationSize;

        /// <summary>
        /// Center-crops <paramref name="source"/> to a square on the GPU and resizes it into the
        /// reusable embedding render texture. The same instance is returned on every call.
        /// </summary>
        public RenderTexture PrepareEmbedding(Texture source)
        {
            ThrowIfDisposed();
            ValidateSource(source);

            var crop = ImageGeometry.NormalizedCenterCrop(source.width, source.height);
            EnsureEmbeddingTexture();
            Blit(source, _embeddingTexture, crop);
            return _embeddingTexture;
        }

        /// <summary>
        /// Copies the center-cropped frame into a reusable CPU-readable texture bounded by
        /// <see cref="ImageGeometry.MaximumVerificationImageSize"/>. The same instance is returned
        /// on every call while the camera resolution is unchanged.
        /// </summary>
        public Texture2D CopyVerificationFrame(Texture source)
        {
            ThrowIfDisposed();
            ValidateSource(source);

            var crop = ImageGeometry.CenterCrop(source.width, source.height);
            var size = Math.Min(ImageGeometry.VerificationImageSize(crop.Width), _maximumVerificationSize);
            EnsureVerificationBuffers(size);

            var normalizedCrop = ImageGeometry.NormalizedCenterCrop(source.width, source.height);
            Blit(source, _verificationSurface, normalizedCrop);

            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = _verificationSurface;
                _verificationFrame.ReadPixels(new Rect(0f, 0f, size, size), 0, 0, false);
                _verificationFrame.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
            }

            return _verificationFrame;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseRenderTexture(ref _embeddingTexture);
            ReleaseRenderTexture(ref _verificationSurface);
            DestroyObject(_verificationFrame);
            _verificationFrame = null;
            _verificationSize = 0;
        }

        private static void Blit(Texture source, RenderTexture destination, NormalizedRect crop)
        {
            Graphics.Blit(
                source,
                destination,
                new Vector2(crop.ScaleX, crop.ScaleY),
                new Vector2(crop.OffsetX, crop.OffsetY));
        }

        private void EnsureEmbeddingTexture()
        {
            if (_embeddingTexture == null)
            {
                _embeddingTexture = CreateSurface(_embeddingSize, "ObjecTalk Embedding Frame");
            }
            else if (!_embeddingTexture.IsCreated())
            {
                // The graphics device can drop render textures across a pause/resume cycle.
                _embeddingTexture.Create();
            }
        }

        private void EnsureVerificationBuffers(int size)
        {
            if (_verificationFrame != null && _verificationSize == size)
            {
                if (!_verificationSurface.IsCreated())
                {
                    _verificationSurface.Create();
                }

                return;
            }

            ReleaseRenderTexture(ref _verificationSurface);
            DestroyObject(_verificationFrame);

            _verificationSurface = CreateSurface(size, "ObjecTalk Verification Surface");
            _verificationFrame = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = "ObjecTalk Verification Frame",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _verificationSize = size;
        }

        private static RenderTexture CreateSurface(int size, string name)
        {
            var surface = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            surface.Create();
            return surface;
        }

        private static void ReleaseRenderTexture(ref RenderTexture surface)
        {
            if (surface == null)
            {
                return;
            }

            surface.Release();
            DestroyObject(surface);
            surface = null;
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void ValidateSource(Texture source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source.width <= 0 || source.height <= 0)
            {
                throw new ArgumentException("The camera frame has no pixels yet.", nameof(source));
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CameraFramePreprocessor));
            }
        }
    }
}
