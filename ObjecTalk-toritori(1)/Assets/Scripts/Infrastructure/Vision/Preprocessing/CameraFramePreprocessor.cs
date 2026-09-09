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
    public sealed class CameraFramePreprocessor : IFramePreprocessor
    {
        // Every full-screen blit this class does (embedding prep and verification-frame copy) is
        // masked through the same shader/material, so registration and recognition always see an
        // identically shaped object-only disc rather than two independently-cropped squares.
        private const string MaskShaderName = "Hidden/ObjecTalk/CircularMask";
        private static readonly Color MaskFillColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        private const float MaskFeatherWidth = 0.01f;

        private readonly int _embeddingSize;
        private readonly int _maximumVerificationSize;
        private readonly float _cropFraction;
        private readonly float _maskRadius;

        private RenderTexture _embeddingTexture;
        private RenderTexture _verificationSurface;
        private Texture2D _verificationFrame;
        private int _verificationSize;
        private bool _disposed;

        private Material _maskMaterial;
        // One-shot so a missing shader (e.g. not yet added to Always Included Shaders in a device
        // build) does not spam the log every recognition cycle; masking is skipped gracefully rather
        // than throwing, matching this codebase's fail-soft rendering conventions elsewhere.
        private bool _warnedNoMaskMaterial;

        public CameraFramePreprocessor()
            : this(
                ImageGeometry.EmbeddingImageSize,
                ImageGeometry.MaximumVerificationImageSize,
                ImageGeometry.DefaultCropFraction,
                ImageGeometry.DefaultMaskFraction)
        {
        }

        public CameraFramePreprocessor(int embeddingSize, int maximumVerificationSize)
            : this(embeddingSize, maximumVerificationSize, ImageGeometry.DefaultCropFraction, ImageGeometry.DefaultMaskFraction)
        {
        }

        public CameraFramePreprocessor(int embeddingSize, int maximumVerificationSize, float cropFraction)
            : this(embeddingSize, maximumVerificationSize, cropFraction, ImageGeometry.DefaultMaskFraction)
        {
        }

        public CameraFramePreprocessor(
            int embeddingSize, int maximumVerificationSize, float cropFraction, float maskFraction)
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

            if (cropFraction <= 0f || cropFraction > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cropFraction),
                    "The crop fraction must be in the range (0, 1].");
            }

            _embeddingSize = embeddingSize;
            _maximumVerificationSize = maximumVerificationSize;
            _cropFraction = cropFraction;
            _maskRadius = CircularMaskLayout.NormalizedRadius(maskFraction);
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

            var crop = ImageGeometry.NormalizedCenterCrop(source.width, source.height, _cropFraction);
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

            var crop = ImageGeometry.CenterCrop(source.width, source.height, _cropFraction);
            var size = Math.Min(ImageGeometry.VerificationImageSize(crop.Width), _maximumVerificationSize);
            EnsureVerificationBuffers(size);

            var normalizedCrop = ImageGeometry.NormalizedCenterCrop(source.width, source.height, _cropFraction);
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

        /// <summary>
        /// Drops the reusable surfaces while leaving the preprocessor usable. The next call to
        /// <see cref="PrepareEmbedding"/> or <see cref="CopyVerificationFrame"/> allocates them
        /// again, so a paused application can hand its graphics memory back.
        /// </summary>
        public void ReleaseBuffers()
        {
            ThrowIfDisposed();
            ReleaseRenderTexture(ref _embeddingTexture);
            ReleaseRenderTexture(ref _verificationSurface);
            DestroyObject(_verificationFrame);
            _verificationFrame = null;
            _verificationSize = 0;
            DestroyObject(_maskMaterial);
            _maskMaterial = null;
        }

        // The concrete return type is more useful to direct callers than the interface's Texture,
        // and C# does not allow an implicit implementation to widen it.
        Texture IFramePreprocessor.PrepareEmbedding(Texture source)
        {
            return PrepareEmbedding(source);
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
            DestroyObject(_maskMaterial);
            _maskMaterial = null;
        }

        /// <summary>
        /// Crops <paramref name="source"/> into <paramref name="destination"/> and, whenever the mask
        /// material could be built, blanks everything outside the centred keep-circle in the same
        /// pass. Falls back to a plain crop blit (no masking) if the shader could not be found, so a
        /// build missing it from Always Included Shaders degrades gracefully instead of throwing.
        /// </summary>
        private void Blit(Texture source, RenderTexture destination, NormalizedRect crop)
        {
            var material = MaskMaterial();
            if (material == null)
            {
                Graphics.Blit(
                    source,
                    destination,
                    new Vector2(crop.ScaleX, crop.ScaleY),
                    new Vector2(crop.OffsetX, crop.OffsetY));
                return;
            }

            // Graphics.Blit(source, dest, material) resets the material's _MainTex_ST to identity, so
            // the centre crop is handed to the shader through a dedicated vector instead.
            material.SetVector("_CropST", new Vector4(crop.ScaleX, crop.ScaleY, crop.OffsetX, crop.OffsetY));
            Graphics.Blit(source, destination, material);
        }

        /// <summary>
        /// Builds the circular-mask material on first use. Returns null (rather than throwing) when
        /// the shader cannot be resolved, which callers treat as "masking unavailable this run".
        /// </summary>
        private Material MaskMaterial()
        {
            if (_maskMaterial != null)
            {
                return _maskMaterial;
            }

            var shader = Shader.Find(MaskShaderName);
            if (shader == null)
            {
                if (!_warnedNoMaskMaterial)
                {
                    Debug.LogWarning(
                        $"CameraFramePreprocessor: shader '{MaskShaderName}' was not found "
                        + "(is it in Always Included Shaders?); frames will not be background-masked.");
                    _warnedNoMaskMaterial = true;
                }
                return null;
            }

            _maskMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            _maskMaterial.SetColor("_FillColor", MaskFillColor);
            _maskMaterial.SetFloat("_MaskRadius", _maskRadius);
            _maskMaterial.SetFloat("_FeatherWidth", MaskFeatherWidth);
            return _maskMaterial;
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
