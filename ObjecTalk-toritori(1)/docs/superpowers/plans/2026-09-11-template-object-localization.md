# Template Object Localization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** locate a recognized object in the live camera frame without OpenCV, so the face overlay follows it on the Android device.

**Architecture:** normalized cross-correlation of a 64x64 grayscale template against the prepared verification frame. The maths lives in engine-free Domain classes; a new `TemplateVerifier` implements the existing `ILocalFeatureVerifier` contract so it drops in beside `AkazeVerifier` and `NullLocalFeatureVerifier`. Templates for already-registered objects are seeded from the registration JPEG that `ObjectItem.ImageData` already holds.

**Tech Stack:** Unity 6000.1.7f1, C# (no Burst, no OpenCV), NUnit via Unity Test Framework, uloop CLI for compile/test.

**Spec:** `docs/superpowers/specs/2026-09-11-template-object-localization-design.md`

## Global Constraints

- `ObjecTalk.Domain.Recognition` has `noEngineReferences: true` — no `UnityEngine` types in Domain, ever.
- Relative positions are **0..1 with a bottom-left origin**, matching `FaceOverlayLayout` and `FeatureCentroid`. Image pixel rows arrive top-left-origin; flip the vertical axis.
- Thresholds come from `RecognitionSettings`, never hard-coded at a call site.
- `TemplateGatesRecognition` defaults to **false**. The first shipped version must not be able to reject a candidate the embedding accepted.
- Test command: `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" run-tests --test-mode EditMode`. It compiles first and runs the whole suite; there is no working filter. Baseline before this plan: **351 passing**.
- Compile only: `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" compile`.
- If Unity is not running, `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" launch` first.
- Commit after every task. Do not commit `ProjectSettings/*.asset`, `Assets/Settings/UniversalRP.asset`, `Assets/UniversalRenderPipelineGlobalSettings.asset` or `Assets/DefaultVolumeProfile.asset.meta` — Unity re-dirties those on every open and they are not ours.

## File Structure

| File | Responsibility |
|---|---|
| `Assets/Scripts/Domain/Recognition/TemplateMatch.cs` | NCC search over scales; the only place correlation maths lives |
| `Assets/Scripts/Domain/Recognition/TemplatePreparation.cs` | RGBA pixels to a square grayscale template |
| `Assets/Scripts/Core/Recognition/LocalVerificationThresholds.cs` | The two numbers a verifier is allowed to know |
| `Assets/Scripts/Infrastructure/Vision/Templates/TemplateVerifier.cs` | `ILocalFeatureVerifier` over templates; `Texture2D` glue |
| `Assets/Scripts/Infrastructure/Vision/Templates/TemplateSample.cs` | `ILocalFeatureSample` holding one template |
| `Assets/Scripts/Infrastructure/Vision/Templates/ObjecTalk.Infrastructure.Vision.Templates.asmdef` | Makes the above testable — the reason this backend beats AKAZE structurally |

---

### Task 1: NCC template matching in Domain

**Files:**
- Create: `Assets/Scripts/Domain/Recognition/TemplateMatch.cs`
- Test: `Assets/Tests/EditMode/TemplateMatchTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `TemplateMatch.TryLocate(ReadOnlySpan<byte> template, int templateSize, ReadOnlySpan<byte> query, int querySize, ReadOnlySpan<float> scales, float minimumScore, out float relativeX, out float relativeY, out float score) -> bool`; constants `TemplateMatch.DefaultScales` (`float[]`), `TemplateMatch.DefaultSearchSize` (`int`, 64), `TemplateMatch.DefaultTemplateFraction` (`float`, 0.5f).

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/TemplateMatchTests.cs`:

```csharp
using System;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class TemplateMatchTests
    {
        private const int QuerySize = 64;

        /// <summary>A flat field with a bright square burned into it at (left, top).</summary>
        private static byte[] Scene(int size, int left, int top, int blockSize, byte background, byte block)
        {
            var pixels = new byte[size * size];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = background;
            }

            for (var y = top; y < top + blockSize; y++)
            {
                for (var x = left; x < left + blockSize; x++)
                {
                    // A gradient inside the block, so correlation has something to lock onto
                    // rather than a featureless slab that matches everywhere.
                    pixels[(y * size) + x] = (byte)Math.Min(255, block + ((x - left) * 3) + ((y - top) * 5));
                }
            }

            return pixels;
        }

        private static readonly float[] UnitScale = { 1f };

        [Test]
        public void TryLocate_FindsATemplateAtTheCentreOfTheQuery()
        {
            var template = Scene(16, 0, 0, 16, 40, 200);
            var query = Scene(QuerySize, 24, 24, 16, 40, 200);

            var found = TemplateMatch.TryLocate(
                template, 16, query, QuerySize, UnitScale, 0.5f,
                out var x, out var y, out var score);

            Assert.That(found, Is.True);
            Assert.That(x, Is.EqualTo(0.5f).Within(0.02f));
            Assert.That(y, Is.EqualTo(0.5f).Within(0.02f));
            Assert.That(score, Is.GreaterThan(0.9f));
        }

        [Test]
        public void TryLocate_ReportsTheOffsetWhenTheObjectHasMoved()
        {
            var template = Scene(16, 0, 0, 16, 40, 200);
            // Block at x=8..24, y=40..56; its centre is (16, 48) in a 64 wide view.
            var query = Scene(QuerySize, 8, 40, 16, 40, 200);

            var found = TemplateMatch.TryLocate(
                template, 16, query, QuerySize, UnitScale, 0.5f,
                out var x, out var y, out _);

            Assert.That(found, Is.True);
            Assert.That(x, Is.EqualTo(16f / QuerySize).Within(0.02f));
            // Bottom-left origin, so a block low in the image reports a low y.
            Assert.That(y, Is.EqualTo(1f - (48f / QuerySize)).Within(0.02f));
        }

        [Test]
        public void TryLocate_IsUnaffectedByGainAndOffset()
        {
            // The property the whole lighting result rests on: correlation is normalised, so the
            // same scene brightened and lifted must land in the same place with the same score.
            var template = Scene(16, 0, 0, 16, 40, 120);
            var query = Scene(QuerySize, 24, 24, 16, 40, 120);
            var brightened = new byte[query.Length];
            for (var i = 0; i < query.Length; i++)
            {
                brightened[i] = (byte)Math.Min(255, (int)(query[i] * 0.6f) + 90);
            }

            TemplateMatch.TryLocate(template, 16, query, QuerySize, UnitScale, 0.5f,
                out var x0, out var y0, out var s0);
            var found = TemplateMatch.TryLocate(template, 16, brightened, QuerySize, UnitScale, 0.5f,
                out var x1, out var y1, out var s1);

            Assert.That(found, Is.True);
            Assert.That(x1, Is.EqualTo(x0).Within(1e-4f));
            Assert.That(y1, Is.EqualTo(y0).Within(1e-4f));
            Assert.That(s1, Is.EqualTo(s0).Within(0.05f));
        }

        [Test]
        public void TryLocate_FindsAnObjectThatMovedCloser()
        {
            var template = Scene(16, 0, 0, 16, 40, 200);
            // The same block drawn twice as large: only the 2.0 scale can match it.
            var query = Scene(QuerySize, 16, 16, 32, 40, 200);

            var found = TemplateMatch.TryLocate(
                template, 16, query, QuerySize, new[] { 1f, 2f }, 0.5f,
                out var x, out var y, out _);

            Assert.That(found, Is.True);
            Assert.That(x, Is.EqualTo(0.5f).Within(0.05f));
            Assert.That(y, Is.EqualTo(0.5f).Within(0.05f));
        }

        [Test]
        public void TryLocate_RefusesAQueryThatDoesNotContainTheTemplate()
        {
            var template = Scene(16, 0, 0, 16, 40, 200);
            var noise = new byte[QuerySize * QuerySize];
            var rng = new Random(11);
            rng.NextBytes(noise);

            var found = TemplateMatch.TryLocate(
                template, 16, noise, QuerySize, UnitScale, 0.9f,
                out var x, out var y, out _);

            Assert.That(found, Is.False);
            Assert.That(x, Is.EqualTo(0f));
            Assert.That(y, Is.EqualTo(0f));
        }

        [Test]
        public void TryLocate_SkipsAScaleThatWouldExceedTheQuery()
        {
            var template = Scene(16, 0, 0, 16, 40, 200);
            var query = Scene(QuerySize, 24, 24, 16, 40, 200);

            // 8.0 would need a 128px template inside a 64px query; 1.0 must still be tried.
            var found = TemplateMatch.TryLocate(
                template, 16, query, QuerySize, new[] { 8f, 1f }, 0.5f,
                out var x, out _, out _);

            Assert.That(found, Is.True);
            Assert.That(x, Is.EqualTo(0.5f).Within(0.02f));
        }

        [Test]
        public void TryLocate_RejectsPixelCountsThatDoNotMatchTheDeclaredSize()
        {
            Assert.Throws<ArgumentException>(() => TemplateMatch.TryLocate(
                new byte[10], 16, new byte[QuerySize * QuerySize], QuerySize,
                UnitScale, 0.5f, out _, out _, out _));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" run-tests --test-mode EditMode`
Expected: compile error `The name 'TemplateMatch' does not exist`. Create the stub in Step 3 and re-run before implementing, so you watch a real failure rather than a compile error.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Domain/Recognition/TemplateMatch.cs`:

```csharp
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
        private static void Resample(ReadOnlySpan<byte> source, int sourceSize, byte[] destination, int destinationSize)
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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" run-tests --test-mode EditMode`
Expected: 358 passing (351 baseline + 7), 0 failing, no warnings from our files.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Domain/Recognition/TemplateMatch.cs Assets/Scripts/Domain/Recognition/TemplateMatch.cs.meta Assets/Tests/EditMode/TemplateMatchTests.cs Assets/Tests/EditMode/TemplateMatchTests.cs.meta
git commit -m "feat: normalised cross-correlation template search"
```

---

### Task 2: Turning camera pixels into a template

**Files:**
- Create: `Assets/Scripts/Domain/Recognition/TemplatePreparation.cs`
- Test: `Assets/Tests/EditMode/TemplatePreparationTests.cs`

**Interfaces:**
- Consumes: `TemplateMatch.DefaultSearchSize`, `TemplateMatch.DefaultTemplateFraction`, `ImageGeometry.CenterCrop(int, int, float)` and `ImageGeometry.DefaultCropFraction` (both already exist in `ObjecTalk.Domain.Recognition`).
- Produces:
  - `TemplatePreparation.ToGrayscaleSquare(ReadOnlySpan<byte> rgba, int width, int height, float cropFraction, int outputSize) -> byte[]`
  - `TemplatePreparation.CentreCrop(ReadOnlySpan<byte> gray, int size, float fraction) -> byte[]`

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/TemplatePreparationTests.cs`:

```csharp
using System;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class TemplatePreparationTests
    {
        private static byte[] Rgba(int width, int height, Func<int, int, (byte r, byte g, byte b)> shade)
        {
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var (r, g, b) = shade(x, y);
                    var i = ((y * width) + x) * 4;
                    pixels[i] = r;
                    pixels[i + 1] = g;
                    pixels[i + 2] = b;
                    pixels[i + 3] = 255;
                }
            }

            return pixels;
        }

        [Test]
        public void ToGrayscaleSquare_ProducesTheRequestedSize()
        {
            var rgba = Rgba(80, 40, (x, y) => (10, 10, 10));

            var gray = TemplatePreparation.ToGrayscaleSquare(rgba, 80, 40, 0.75f, 16);

            Assert.That(gray.Length, Is.EqualTo(16 * 16));
        }

        [Test]
        public void ToGrayscaleSquare_WeightsGreenMostLikeHumanLuminance()
        {
            var green = TemplatePreparation.ToGrayscaleSquare(
                Rgba(8, 8, (x, y) => (0, 255, 0)), 8, 8, 1f, 4);
            var blue = TemplatePreparation.ToGrayscaleSquare(
                Rgba(8, 8, (x, y) => (0, 0, 255)), 8, 8, 1f, 4);

            Assert.That(green[0], Is.GreaterThan(blue[0]));
        }

        [Test]
        public void ToGrayscaleSquare_KeepsTheCentreAndDropsTheEdges()
        {
            // Black frame with a white centre column; a 50% crop must come back all white.
            var rgba = Rgba(40, 40, (x, y) => (x >= 10 && x < 30) ? ((byte)255, (byte)255, (byte)255)
                                                                  : ((byte)0, (byte)0, (byte)0));

            var gray = TemplatePreparation.ToGrayscaleSquare(rgba, 40, 40, 0.5f, 8);

            foreach (var pixel in gray)
            {
                Assert.That(pixel, Is.GreaterThan(200));
            }
        }

        [Test]
        public void ToGrayscaleSquare_KeepsTheTopOfTheImageAtTheTopOfTheTemplate()
        {
            // Rows run downward in the source; the template keeps that order, and only
            // TemplateMatch flips the axis when it reports a position.
            var rgba = Rgba(16, 16, (x, y) => y < 8 ? ((byte)0, (byte)0, (byte)0)
                                                    : ((byte)255, (byte)255, (byte)255));

            var gray = TemplatePreparation.ToGrayscaleSquare(rgba, 16, 16, 1f, 8);

            Assert.That(gray[0], Is.LessThan(50), "first row is the top of the source");
            Assert.That(gray[gray.Length - 1], Is.GreaterThan(200));
        }

        [Test]
        public void CentreCrop_TakesTheMiddleFraction()
        {
            var gray = new byte[16 * 16];
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    gray[(y * 16) + x] = (byte)(x >= 4 && x < 12 && y >= 4 && y < 12 ? 200 : 10);
                }
            }

            var cropped = TemplatePreparation.CentreCrop(gray, 16, 0.5f);

            Assert.That(cropped.Length, Is.EqualTo(8 * 8));
            foreach (var pixel in cropped)
            {
                Assert.That(pixel, Is.EqualTo(200));
            }
        }

        [Test]
        public void ToGrayscaleSquare_RejectsAPixelBufferThatIsNotRgba()
        {
            Assert.Throws<ArgumentException>(() =>
                TemplatePreparation.ToGrayscaleSquare(new byte[10], 8, 8, 1f, 4));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" run-tests --test-mode EditMode`
Expected: `TemplatePreparation` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Domain/Recognition/TemplatePreparation.cs`:

```csharp
using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// Turns camera or capture pixels into the square grayscale image <see cref="TemplateMatch"/>
    /// works on. Framing follows <see cref="ImageGeometry"/> so a template and a live view are
    /// cropped identically; anything else would compare two different framings.
    /// </summary>
    public static class TemplatePreparation
    {
        // Rec. 601 luma. Green dominates because human vision does.
        private const double RedWeight = 0.299d;
        private const double GreenWeight = 0.587d;
        private const double BlueWeight = 0.114d;

        /// <summary>
        /// Centre-crops <paramref name="rgba"/> to a square, converts to luma, and resamples to
        /// <paramref name="outputSize"/>. Row order is preserved: row zero is the top of the
        /// source, and only <see cref="TemplateMatch"/> flips the axis when reporting a position.
        /// </summary>
        public static byte[] ToGrayscaleSquare(
            ReadOnlySpan<byte> rgba, int width, int height, float cropFraction, int outputSize)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "The image has no size.");
            }

            if (outputSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(outputSize), "The template has no size.");
            }

            if (rgba.Length != width * height * 4)
            {
                throw new ArgumentException(
                    "Pixels must be four channels per pixel at the declared size.", nameof(rgba));
            }

            var crop = ImageGeometry.CenterCrop(width, height, cropFraction);
            var output = new byte[outputSize * outputSize];

            for (var y = 0; y < outputSize; y++)
            {
                var sy = crop.Y + (y * crop.Height / outputSize);
                for (var x = 0; x < outputSize; x++)
                {
                    var sx = crop.X + (x * crop.Width / outputSize);
                    var i = (((sy * width) + sx) * 4);
                    var luma = (RedWeight * rgba[i]) + (GreenWeight * rgba[i + 1]) + (BlueWeight * rgba[i + 2]);
                    output[(y * outputSize) + x] = (byte)(luma > 255d ? 255d : luma);
                }
            }

            return output;
        }

        /// <summary>The middle <paramref name="fraction"/> of a square grayscale image.</summary>
        public static byte[] CentreCrop(ReadOnlySpan<byte> gray, int size, float fraction)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "The image has no size.");
            }

            if (gray.Length != size * size)
            {
                throw new ArgumentException(
                    "The pixel count does not match the declared size.", nameof(gray));
            }

            if (fraction <= 0f || fraction > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(fraction), "The fraction must be within (0, 1].");
            }

            var side = Math.Max(1, (int)(size * fraction));
            var offset = (size - side) / 2;
            var output = new byte[side * side];
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    output[(y * side) + x] = gray[((offset + y) * size) + offset + x];
                }
            }

            return output;
        }
    }
}
```

Check `ImageGeometry.PixelRect`'s member names before compiling (`Assets/Scripts/Domain/Recognition/ImageGeometry.cs`) and adjust `crop.X` / `crop.Y` / `crop.Width` / `crop.Height` to whatever it actually exposes.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" run-tests --test-mode EditMode`
Expected: 364 passing (358 + 6), 0 failing.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Domain/Recognition/TemplatePreparation.cs Assets/Scripts/Domain/Recognition/TemplatePreparation.cs.meta Assets/Tests/EditMode/TemplatePreparationTests.cs Assets/Tests/EditMode/TemplatePreparationTests.cs.meta
git commit -m "feat: prepare grayscale templates from camera pixels"
```

---

### Task 3: Give verifiers a threshold they can use

Replaces `Verify`'s `int minimumMatches` with a value carrying both a match count and a score, and adds the two new settings. Mechanical but it touches every verifier, so it is its own task and its own review.

**Files:**
- Create: `Assets/Scripts/Core/Recognition/LocalVerificationThresholds.cs`
- Modify: `Assets/Scripts/Domain/Recognition/RecognitionTypes.cs` (the `RecognitionSettings` struct)
- Modify: `Assets/Scripts/Core/Recognition/ILocalFeatureVerifier.cs`
- Modify: `Assets/Scripts/Core/Recognition/NullLocalFeatureVerifier.cs`
- Modify: `Assets/Scripts/Core/Recognition/ObjectRecognitionService.cs` (the `_verifier.Verify` call in `VerifyLocally`)
- Modify: `Assets/Scripts/Infrastructure/Vision/AkazeVerifier.cs` (both the public and the explicit-interface `Verify`)
- Modify: `Assets/Tests/Shared/RecognitionServiceDoubles.cs` (`FakeLocalFeatureVerifier`)
- Modify: `Assets/Tests/EditMode/ObjectRecognitionServiceTests.cs` (the `Settings(...)` helper and `LastMinimumMatches`)
- Modify: `Assets/Tests/EditMode/NullLocalFeatureVerifierTests.cs`
- Test: `Assets/Tests/EditMode/RecognitionSettingsTests.cs` if it exists, otherwise add the cases to `ObjectRecognitionServiceTests`

**Interfaces:**
- Consumes: nothing from Tasks 1-2.
- Produces:
  - `readonly struct LocalVerificationThresholds` with `int MinimumMatches`, `float MinimumScore`, `bool GatesRecognition`, constructor `(int minimumMatches, float minimumScore, bool gatesRecognition)`, and `static LocalVerificationThresholds From(in RecognitionSettings settings)`.
  - `ILocalFeatureVerifier.Verify(Texture2D image, ILocalFeatureSample reference, in LocalVerificationThresholds thresholds) -> LocalVerificationResult`
  - `RecognitionSettings.MinimumTemplateScore` (`float`), `RecognitionSettings.TemplateGatesRecognition` (`bool`), `RecognitionSettings.DefaultMinimumTemplateScore = 0.35f`.

- [ ] **Step 1: Write the failing tests**

Add to `Assets/Tests/EditMode/ObjectRecognitionServiceTests.cs`, above the `// --- verification gating` comment:

```csharp
        // --- thresholds -----------------------------------------------------------------------

        [Test]
        public void ThresholdsCarryBothTheMatchCountAndTheTemplateScore()
        {
            var settings = Settings(minimumLocalMatchCount: 17, minimumTemplateScore: 0.42f);

            var thresholds = LocalVerificationThresholds.From(settings);

            Assert.That(thresholds.MinimumMatches, Is.EqualTo(17));
            Assert.That(thresholds.MinimumScore, Is.EqualTo(0.42f).Within(1e-6f));
        }

        [Test]
        public void RecognitionIsNotGatedByTheTemplateScoreUnlessSettingsSaySo()
        {
            // The shipped default must be unable to reject anything the embedding accepted.
            var thresholds = LocalVerificationThresholds.From(Settings());

            Assert.That(thresholds.GatesRecognition, Is.False);
        }

        [Test]
        public void TheVerifierIsGivenTheThresholdsFromSettings()
        {
            Build(Settings(minimumLocalMatchCount: 17, requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(1f, 0f);

            RunOneCycle(now: 0d);

            Assert.That(_verifier.LastThresholds.MinimumMatches, Is.EqualTo(17));
        }
```

Extend the `Settings(...)` helper in the same file:

```csharp
        private static RecognitionSettings Settings(
            float similarityThreshold = 0.7f,
            float ambiguityMargin = 0.05f,
            int requiredConsecutiveHits = RecognitionSettings.DefaultRequiredConsecutiveHits,
            int minimumLocalMatchCount = RecognitionSettings.DefaultMinimumLocalMatchCount,
            float minimumTemplateScore = RecognitionSettings.DefaultMinimumTemplateScore,
            bool templateGatesRecognition = false)
        {
            return new RecognitionSettings(
                similarityThreshold,
                ambiguityMargin,
                requiredConsecutiveHits,
                RecognitionSettings.DefaultLossGracePeriodSeconds,
                RecognitionSettings.MinimumSupportedRecognitionIntervalSeconds,
                RecognitionSettings.MaximumSupportedRecognitionIntervalSeconds,
                minimumLocalMatchCount,
                minimumTemplateScore,
                templateGatesRecognition);
        }
```

Delete the now-redundant `MinimumLocalMatchCountComesFromSettings` test — `TheVerifierIsGivenTheThresholdsFromSettings` replaces it and asserts the same intent through the new type.

- [ ] **Step 2: Run to verify it fails**

Run: `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" run-tests --test-mode EditMode`
Expected: `LocalVerificationThresholds` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Core/Recognition/LocalVerificationThresholds.cs`:

```csharp
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
```

In `RecognitionTypes.cs`, add beside the other constants:

```csharp
        public const float DefaultMinimumTemplateScore = 0.35f;
```

Add a constructor overload taking `minimumTemplateScore` and `templateGatesRecognition` after `minimumLocalMatchCount`, have the existing seven-argument constructor delegate to it with `DefaultMinimumTemplateScore` and `false`, validate that the score is finite and within `[-1, 1]` (correlation cannot leave that range), and expose:

```csharp
        /// <summary>Correlation a template must reach before its position is believed.</summary>
        public float MinimumTemplateScore { get; }

        /// <summary>
        /// Whether a template that fails to match may reject a candidate the embedding accepted.
        /// Off until real device scores have been measured; on is a deliberate second step.
        /// </summary>
        public bool TemplateGatesRecognition { get; }
```

Then change the `Verify` signature in `ILocalFeatureVerifier`, `NullLocalFeatureVerifier`, `AkazeVerifier` (public and explicit-interface) and `FakeLocalFeatureVerifier` from `int minimumMatches` to `in LocalVerificationThresholds thresholds`, reading `thresholds.MinimumMatches` where the int was used. In `FakeLocalFeatureVerifier` replace `LastMinimumMatches` with:

```csharp
        public LocalVerificationThresholds LastThresholds { get; private set; }
```

set it in `Verify`, and in `ObjectRecognitionService.VerifyLocally` replace `_settings.MinimumLocalMatchCount` with `LocalVerificationThresholds.From(_settings)`, hoisted out of the loop.

- [ ] **Step 4: Run to verify it passes**

Run: `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" run-tests --test-mode EditMode`
Expected: 366 passing (364 + 3 new - 1 deleted), 0 failing.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Core/Recognition Assets/Scripts/Domain/Recognition/RecognitionTypes.cs Assets/Scripts/Infrastructure/Vision/AkazeVerifier.cs Assets/Tests
git commit -m "refactor: give verifiers a threshold value instead of a bare match count"
```

---

### Task 4: An object stays recognizable when its local features cannot be restored

The bug found while designing: `LoadSamples` drops the whole record — embedding included — when `Restore` yields nothing, so swapping backends would make every already-registered object unrecognizable.

**Files:**
- Modify: `Assets/Scripts/Core/Recognition/ObjectRecognitionService.cs` (`LoadSamples` around line 160-172, `VerifyLocally` around line 498)
- Test: `Assets/Tests/EditMode/ObjectRecognitionServiceTests.cs`

**Interfaces:**
- Consumes: `LocalVerificationThresholds` from Task 3.
- Produces: no new API. `VerifyLocally` returns `LocalVerificationResult.Verified()` for an object with no references.

- [ ] **Step 1: Write the failing tests**

Add to `ObjectRecognitionServiceTests.cs`:

```csharp
        [Test]
        public void AnObjectWhoseLocalFeaturesCannotBeRestoredIsStillRecognized()
        {
            // A backend that cannot read the persisted blob must cost the object its position,
            // never its embedding - otherwise switching backends unregisters everything.
            _verifier.RestoreFails = true;
            Build(Settings(requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(1f, 0f);

            var result = RunOneCycle(now: 0d);

            Assert.That(result.Status, Is.EqualTo(RecognitionTickStatus.Recognized));
            Assert.That(result.ObjectId, Is.EqualTo(ObjectA));
            Assert.That(result.HasLocation, Is.False);
        }

        [Test]
        public void AnObjectWithNoReferencesNeverReachesTheVerifier()
        {
            _verifier.RestoreFails = true;
            Build(Settings(requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(1f, 0f);

            RunOneCycle(now: 0d);

            Assert.That(_verifier.VerifyCallCount, Is.Zero);
        }
```

- [ ] **Step 2: Run to verify they fail**

Run: `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" run-tests --test-mode EditMode`
Expected: `AnObjectWhoseLocalFeaturesCannotBeRestoredIsStillRecognized` fails with `Expected: Recognized But was: NotRecognized`.

- [ ] **Step 3: Write the implementation**

In `LoadSamples`, replace the block that skips a featureless record:

```csharp
                if (features == null || !features.HasFeatures)
                {
                    features?.Dispose();
                    continue;
                }

                _samples.Add(sample);
                AddReference(record.ObjectId, features);
```

with:

```csharp
                // A backend that cannot read this row's blob costs the object its position, not
                // its identity: the embedding still recognises it, and a template can be seeded
                // later. Dropping the record here would unregister the object outright.
                _samples.Add(sample);
                if (features == null || !features.HasFeatures)
                {
                    features?.Dispose();
                    continue;
                }

                AddReference(record.ObjectId, features);
```

In `VerifyLocally`, an object with no references must confirm rather than reject:

```csharp
            if (_activeFrame == null)
            {
                return LocalVerificationResult.Rejected;
            }

            // No references means no backend could read this object's blob. Recognition then rests
            // on the embedding alone, which is what the device already does without AKAZE.
            if (!_references.TryGetValue(objectId, out var references) || references.Count == 0)
            {
                return LocalVerificationResult.Verified();
            }
```

- [ ] **Step 4: Run to verify they pass**

Run: `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" run-tests --test-mode EditMode`
Expected: 368 passing, 0 failing.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Core/Recognition/ObjectRecognitionService.cs Assets/Tests/EditMode/ObjectRecognitionServiceTests.cs
git commit -m "fix: keep an object recognizable when its local features will not restore"
```

---

### Task 5: TemplateVerifier, in an assembly that can be tested

**Files:**
- Create: `Assets/Scripts/Infrastructure/Vision/Templates/ObjecTalk.Infrastructure.Vision.Templates.asmdef`
- Create: `Assets/Scripts/Infrastructure/Vision/Templates/TemplateSample.cs`
- Create: `Assets/Scripts/Infrastructure/Vision/Templates/TemplateVerifier.cs`
- Modify: `Assets/Tests/EditMode/ObjecTalk.EditModeTests.asmdef` (add the reference)
- Modify: `Assets/Scripts/Infrastructure/Vision/AkazeVerifier.cs` (refuse template rows)
- Test: `Assets/Tests/EditMode/TemplateVerifierTests.cs`

**Interfaces:**
- Consumes: `TemplateMatch.TryLocate`, `TemplatePreparation.ToGrayscaleSquare`, `TemplatePreparation.CentreCrop`, `LocalVerificationThresholds`, `ILocalFeatureVerifier`, `ILocalFeatureSample`, `LocalFeatureBlob`, `LocalVerificationResult`.
- Produces: `TemplateVerifier : ILocalFeatureVerifier`, and `TemplateVerifier.TemplateDescriptorType` (`int`, the marker written into `LocalFeatureBlob`'s descriptor type so the two backends can tell their rows apart).

The asmdef:

```json
{
  "name": "ObjecTalk.Infrastructure.Vision.Templates",
  "rootNamespace": "ObjecTalk.Infrastructure.Vision",
  "references": [
    "ObjecTalk.Domain.Recognition",
    "ObjecTalk.Core.Recognition"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/TemplateVerifierTests.cs`:

```csharp
using NUnit.Framework;
using ObjecTalk.Core.Recognition;
using ObjecTalk.Infrastructure.Vision;
using UnityEngine;

namespace ObjecTalk.Tests.EditMode
{
    public class TemplateVerifierTests
    {
        private TemplateVerifier _verifier;

        private static readonly LocalVerificationThresholds Lenient =
            new LocalVerificationThresholds(10, 0.3f, false);

        [SetUp]
        public void SetUp()
        {
            _verifier = new TemplateVerifier();
            _verifier.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            _verifier.Dispose();
        }

        /// <summary>A textured blob on a flat field, at a known place in a 128x128 frame.</summary>
        private static Texture2D Frame(int blockLeft, int blockTop, int blockSize)
        {
            const int Size = 128;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false);
            var pixels = new Color32[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var inBlock = x >= blockLeft && x < blockLeft + blockSize
                                  && y >= blockTop && y < blockTop + blockSize;
                    var v = (byte)(inBlock ? Mathf.Min(255, 120 + ((x - blockLeft) * 4) + ((y - blockTop) * 6)) : 30);
                    pixels[(y * Size) + x] = new Color32(v, v, v, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        [Test]
        public void ExtractProducesAReferenceThatReportsFeatures()
        {
            using var frame = Frame(48, 48, 32);

            using var sample = _verifier.Extract(frame);

            Assert.That(sample.HasFeatures, Is.True);
        }

        [Test]
        public void VerifyLocatesTheObjectItWasGiven()
        {
            using var registration = Frame(48, 48, 32);
            using var live = Frame(48, 48, 32);
            using var reference = _verifier.Extract(registration);

            var result = _verifier.Verify(live, reference, Lenient);

            Assert.That(result.IsVerified, Is.True);
            Assert.That(result.HasLocation, Is.True);
            Assert.That(result.RelativeX, Is.EqualTo(0.5f).Within(0.08f));
            Assert.That(result.RelativeY, Is.EqualTo(0.5f).Within(0.08f));
        }

        [Test]
        public void VerifyFollowsAnObjectThatMoved()
        {
            using var registration = Frame(48, 48, 32);
            using var live = Frame(20, 48, 32);   // moved left, same height
            using var reference = _verifier.Extract(registration);

            var result = _verifier.Verify(live, reference, Lenient);

            Assert.That(result.HasLocation, Is.True);
            Assert.That(result.RelativeX, Is.LessThan(0.45f), "the object moved left");
            Assert.That(result.RelativeY, Is.EqualTo(0.5f).Within(0.1f));
        }

        [Test]
        public void VerifyStillConfirmsWhenItCannotLocateAndTheGateIsOff()
        {
            using var registration = Frame(48, 48, 32);
            using var live = Frame(48, 48, 32);
            using var reference = _verifier.Extract(registration);
            var unreachable = new LocalVerificationThresholds(10, 1.01f, gatesRecognition: false);

            var result = _verifier.Verify(live, reference, unreachable);

            Assert.That(result.IsVerified, Is.True, "the gate is off, so identity is untouched");
            Assert.That(result.HasLocation, Is.False);
        }

        [Test]
        public void VerifyRejectsWhenItCannotLocateAndTheGateIsOn()
        {
            using var registration = Frame(48, 48, 32);
            using var live = Frame(48, 48, 32);
            using var reference = _verifier.Extract(registration);
            var unreachable = new LocalVerificationThresholds(10, 1.01f, gatesRecognition: true);

            var result = _verifier.Verify(live, reference, unreachable);

            Assert.That(result.IsVerified, Is.False);
        }

        [Test]
        public void SerializeAndRestoreRoundTripATemplate()
        {
            using var registration = Frame(48, 48, 32);
            using var live = Frame(20, 48, 32);
            using var original = _verifier.Extract(registration);

            using var restored = _verifier.Restore(original.Serialize());

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.HasFeatures, Is.True);
            var fromOriginal = _verifier.Verify(live, original, Lenient);
            var fromRestored = _verifier.Verify(live, restored, Lenient);
            Assert.That(fromRestored.RelativeX, Is.EqualTo(fromOriginal.RelativeX).Within(1e-4f));
        }

        [Test]
        public void RestoreRefusesABlobWrittenByAnotherBackend()
        {
            // AKAZE descriptors are not a template. Reading them as one would produce a confident
            // position from noise, which is worse than reporting none.
            var foreign = new LocalFeatureBlob(new byte[] { 0 }, new byte[] { 0 }, 1, 1, 0, 1, 1);

            var restored = _verifier.Restore(foreign);

            Assert.That(restored == null || !restored.HasFeatures, Is.True);
            restored?.Dispose();
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" run-tests --test-mode EditMode`
Expected: `TemplateVerifier` does not exist. Add `"ObjecTalk.Infrastructure.Vision.Templates"` to the EditMode asmdef's `references` before this compiles.

- [ ] **Step 3: Write the implementation**

`TemplateSample.cs` holds a `byte[]` template plus its side length, reports `HasFeatures` when the array is non-empty, and `Serialize()` returns a `LocalFeatureBlob` whose descriptor bytes are the template, `descriptorRows` and `descriptorColumns` are the side length, and `descriptorType` is `TemplateVerifier.TemplateDescriptorType` (use `unchecked((int)0x7E30_0001)` — a value AKAZE's `Mat.Type()` cannot produce). Keypoint bytes are a single zero, matching what `NullLocalFeatureVerifier.Sample` already does to satisfy `LocalFeatureBlob`'s guards.

`TemplateVerifier.cs`:
- `Initialize()` / `Dispose()` are no-ops; there is no native resource.
- `Extract(Texture2D)`: `image.GetRawTextureData<byte>()` into a reused `byte[]`, then `TemplatePreparation.ToGrayscaleSquare(pixels, image.width, image.height, ImageGeometry.DefaultCropFraction, TemplateMatch.DefaultSearchSize)`, then `TemplatePreparation.CentreCrop(..., TemplateMatch.DefaultTemplateFraction)`. Throw `ArgumentException` on a non-RGBA32 texture, exactly as `AkazeVerifier.UpdateGrayscale` does.
- `Restore(LocalFeatureBlob)`: return null unless `descriptorType == TemplateDescriptorType` and the byte count equals `rows * columns`.
- `Verify(Texture2D image, ILocalFeatureSample reference, in LocalVerificationThresholds thresholds)`: prepare the live view at `TemplateMatch.DefaultSearchSize` (grayscale square, no centre crop — the query is the whole prepared view), call `TemplateMatch.TryLocate` with `TemplateMatch.DefaultScales` and `thresholds.MinimumScore`. On success `LocalVerificationResult.VerifiedAt(x, y)`. On failure `thresholds.GatesRecognition ? LocalVerificationResult.Rejected : LocalVerificationResult.Verified()`.
- Reuse the pixel and template buffers across calls; allocate on first use and when the frame size changes, following `AkazeVerifier`'s `EnsureImageBuffers` pattern.

In `AkazeVerifier.Restore`, return null when the blob's descriptor type equals `TemplateVerifier.TemplateDescriptorType`. `AkazeVerifier` lives in `Assembly-CSharp` and the Templates assembly is `autoReferenced: true`, so the constant is visible; if it is not, duplicate the literal with a comment naming its source.

- [ ] **Step 4: Run to verify they pass**

Run: `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" run-tests --test-mode EditMode`
Expected: 375 passing, 0 failing.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Infrastructure/Vision Assets/Tests/EditMode/TemplateVerifierTests.cs Assets/Tests/EditMode/TemplateVerifierTests.cs.meta Assets/Tests/EditMode/ObjecTalk.EditModeTests.asmdef
git commit -m "feat: TemplateVerifier locates objects without OpenCV"
```

---

### Task 6: Seeding templates for objects registered before this existed

**Files:**
- Modify: `Assets/Scripts/Core/Recognition/ObjectRecognitionService.cs`
- Test: `Assets/Tests/EditMode/ObjectRecognitionServiceTests.cs`

**Interfaces:**
- Consumes: `AddReference` (private, exists), `ILocalFeatureSample`.
- Produces: `ObjectRecognitionService.SeedReferences(string objectId, IReadOnlyList<ILocalFeatureSample> references)`. Takes a list because Phase 2 stores one template per registration capture. It is a no-op when the object already has references, so a real persisted template always wins over a seeded one.

- [ ] **Step 1: Write the failing tests**

```csharp
        [Test]
        public void SeededReferencesGiveAnObjectAPositionItCouldNotRestore()
        {
            _verifier.RestoreFails = true;
            Build(Settings(requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(1f, 0f);
            _verifier.VerifyLocation = (0.62f, 0.28f);

            _service.SeedReferences(ObjectA, new ILocalFeatureSample[] { new FakeLocalFeatureSample("seeded") });
            var result = RunOneCycle(now: 0d);

            Assert.That(result.Status, Is.EqualTo(RecognitionTickStatus.Recognized));
            Assert.That(result.HasLocation, Is.True);
            Assert.That(result.RelativeX, Is.EqualTo(0.62f).Within(1e-6f));
        }

        [Test]
        public void SeedingLeavesARestoredReferenceAlone()
        {
            Build(Settings(requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));

            _service.SeedReferences(ObjectA, new ILocalFeatureSample[] { new FakeLocalFeatureSample("seeded") });
            _extractor.ScriptResult(1f, 0f);
            RunOneCycle(now: 0d);

            Assert.That(_verifier.VerifiedAgainst.Count, Is.EqualTo(1),
                "the persisted reference is authoritative; seeding must not add a second one");
        }

        [Test]
        public void SeedingAnUnknownObjectIsIgnored()
        {
            Build(Settings());
            SeedObjects((ObjectA, Unit(1f, 0f)));

            Assert.DoesNotThrow(() => _service.SeedReferences(
                "no-such-object", new ILocalFeatureSample[] { new FakeLocalFeatureSample("seeded") }));
        }
```

- [ ] **Step 2: Run to verify they fail**

Run: `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" run-tests --test-mode EditMode`
Expected: `SeedReferences` is not defined.

- [ ] **Step 3: Write the implementation**

Add to `ObjectRecognitionService`, beside `AddReference`:

```csharp
        /// <summary>
        /// Supplies references for an object whose persisted blob this backend could not read -
        /// the presentation layer builds them from the registration snapshot it already holds.
        /// A restored reference is authoritative, so an object that already has one is untouched,
        /// and an object this service has never loaded is ignored rather than invented.
        /// </summary>
        public void SeedReferences(string objectId, IReadOnlyList<ILocalFeatureSample> references)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(objectId) || references == null || references.Count == 0)
            {
                return;
            }

            if (_references.TryGetValue(objectId, out var existing) && existing.Count > 0)
            {
                return;
            }

            var known = false;
            for (var i = 0; i < _samples.Count; i++)
            {
                if (string.Equals(_samples[i].Label, objectId, StringComparison.Ordinal))
                {
                    known = true;
                    break;
                }
            }

            if (!known)
            {
                return;
            }

            for (var i = 0; i < references.Count; i++)
            {
                if (references[i] != null && references[i].HasFeatures)
                {
                    AddReference(objectId, references[i]);
                }
            }
        }
```

Add `using System.Collections.Generic;` if it is not already there.

- [ ] **Step 4: Run to verify they pass**

Run: `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" run-tests --test-mode EditMode`
Expected: 378 passing, 0 failing.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Core/Recognition/ObjectRecognitionService.cs Assets/Tests/EditMode/ObjectRecognitionServiceTests.cs
git commit -m "feat: seed references for objects whose blob this backend cannot read"
```

---

### Task 7: Wire the backend into the scene

**Files:**
- Modify: `Assets/Scripts/Presentation/Controllers/EmbeddingObjectMatcher.cs` — `BuildLocalFeatureVerifier` (around line 374), `BuildRecognitionSettings` (around line 401), the object load loop (around line 1911).

**Interfaces:**
- Consumes: `TemplateVerifier`, `ObjectRecognitionService.SeedReferences`, `RecognitionSettings`'s new arguments.
- Produces: nothing later tasks depend on.

This class is not covered by tests (it lives in `Assembly-CSharp` with the OpenCV types); the gate here is a clean compile plus the device pass in Task 8.

- [ ] **Step 1: Add the backend selector**

```csharp
    public enum LocalFeatureBackend
    {
        /// <summary>Correlation against the registration snapshot. Works on the device.</summary>
        Template = 0,

        /// <summary>AKAZE. Editor only - OpenCV ships no arm64 native library.</summary>
        Akaze = 1,

        /// <summary>No local verification at all; the embedding decides alone.</summary>
        None = 2,
    }
```

and a serialized field beside the other recognition knobs:

```csharp
    [Tooltip("局所特徴のバックエンド。Template は実機で動く相関マッチ、Akaze は Editor 専用。")]
    public LocalFeatureBackend localFeatureBackend = LocalFeatureBackend.Template;
```

- [ ] **Step 2: Switch on it in BuildLocalFeatureVerifier**

Keep the existing try/catch on the AKAZE branch only:

```csharp
    private ILocalFeatureVerifier BuildLocalFeatureVerifier()
    {
        switch (localFeatureBackend)
        {
            case LocalFeatureBackend.None:
                return new NullLocalFeatureVerifier();

            case LocalFeatureBackend.Akaze:
                try
                {
                    var akaze = new AkazeVerifier();
                    akaze.Initialize();
                    return akaze;
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        "AKAZE local-feature verification is unavailable; recognition will rely on the "
                        + $"embedding model alone. ({e.GetType().Name}: {e.Message})");
                    return new NullLocalFeatureVerifier();
                }

            default:
                var templates = new TemplateVerifier();
                templates.Initialize();
                return templates;
        }
    }
```

- [ ] **Step 3: Pass the new settings**

In `BuildRecognitionSettings`, use the constructor overload from Task 3, passing `RecognitionSettings.DefaultMinimumTemplateScore` and `false` for the gate. Expose both as serialized fields so the device pass in Task 8 can tune them without a rebuild:

```csharp
    [Range(0f, 1f)]
    [Tooltip("テンプレートの位置を信用する最低相関。下回ると顔は登録時の位置に戻る。")]
    public float minimumTemplateScore = RecognitionSettings.DefaultMinimumTemplateScore;

    [Tooltip("テンプレートが一致しないとき認識自体を却下するか。実機でスコアを測るまでOFF。")]
    public bool templateGatesRecognition = false;
```

- [ ] **Step 4: Seed templates from the registration snapshots**

In the object load loop, after `_objectsById[item.object_id] = new RegisteredObject { ... }` and after the recognition service has loaded its samples, decode the stored JPEG once per object and seed it. `item.ImageData` is already in memory:

```csharp
    /// <summary>
    /// Objects registered before templates existed have AKAZE descriptors in their rows, which the
    /// template backend cannot read. Their registration snapshot is still on the object row, so a
    /// template is built from that instead - no schema change, no re-registration.
    /// </summary>
    private void SeedTemplatesFromSnapshots(List<ObjectItem> userObjects)
    {
        if (_recognitionService == null || _localFeatureVerifier == null || userObjects == null)
        {
            return;
        }

        for (var i = 0; i < userObjects.Count; i++)
        {
            var item = userObjects[i];
            if (item?.ImageData == null || item.ImageData.Length == 0)
            {
                continue;
            }

            var snapshot = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                if (!snapshot.LoadImage(item.ImageData))
                {
                    continue;
                }

                var sample = _localFeatureVerifier.Extract(snapshot);
                if (sample == null || !sample.HasFeatures)
                {
                    sample?.Dispose();
                    continue;
                }

                _recognitionService.SeedReferences(item.object_id, new[] { sample });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Template] Could not seed '{item.object_id}': {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                Destroy(snapshot);
            }
        }
    }
```

`LoadImage` yields an RGBA32 texture, which is what `Extract` requires. Hold the verifier in a field (`_localFeatureVerifier`) if it is not already kept — check whether `BuildLocalFeatureVerifier`'s result is stored anywhere reachable and add the field if not. Call `SeedTemplatesFromSnapshots(userObjects)` after `LoadSamples`, and only when `localFeatureBackend == LocalFeatureBackend.Template`.

- [ ] **Step 5: Compile and run the suite**

Run: `"C:/Users/cadix/AppData/Local/Programs/uloop/bin/uloop.exe" run-tests --test-mode EditMode`
Expected: 378 passing, 0 failing, no new warnings from our files.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Presentation/Controllers/EmbeddingObjectMatcher.cs
git commit -m "feat: select the template backend and seed it from registration snapshots"
```

---

### Task 8: Device pass

The spike ran on frames derived from the same photograph. This is where that assumption is tested. Nothing before this proves the feature works.

**Files:** none — measurement and tuning only. Any change this produces is a follow-up commit.

**Interfaces:** none.

- [ ] **Step 1: Free memory, then build**

The `libil2cpp.so` link is the peak and the supervisor kills the build if the machine is tight; close editors and browsers first, then:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.1.7f1-x86_64/Editor/Unity.exe" -batchmode -quit \
  -projectPath "D:/newest_toritori/ObjecTalk/ObjecTalk-toritori(1)" -buildTarget Android \
  -executeMethod ObjecTalk.EditorTools.DevBuild.BuildAndroidDev \
  -logFile "C:/Users/cadix/AppData/Local/Temp/claude/android-template.log" -accept-apiupdate
```

Expect `[DevBuild] SUCCESS` and roughly 32 minutes cold. `errors=1` from the source generator is known and non-fatal.

- [ ] **Step 2: Install and confirm the backend chose templates**

```bash
export PATH="$PATH:/c/Users/cadix/AppData/Local/Android/Sdk/platform-tools"
adb devices                     # the build restarts the adb server; the SC-03L may reappear on USB
adb -s RF8M506SDLJ install -r -d "Builds/Android/ObjecTalk-dev.apk"
adb -s RF8M506SDLJ logcat -c
adb -s RF8M506SDLJ shell am start -n com.DefaultCompany.ObjecTalktoritori1/com.unity3d.player.appui.AppUIGameActivity
```

Expected in `adb logcat -s Unity`: **no** "AKAZE local-feature verification is unavailable" warning — that branch is no longer reached — and no `[Template] Could not seed` warnings for the five registered objects.

- [ ] **Step 3: Watch the face follow**

Enter Talk mode and point the camera at a registered object. Expected: recognition fires as before, and the eyes and mouth sit on the object and follow it as it moves across the frame, easing rather than stepping. Move the object to a corner: the face should stop at the preview edge rather than disappear.

- [ ] **Step 4: Measure real scores before touching the gate**

Temporarily log the correlation score on each recognized tick, then record what true matches and near-misses actually score on the device. Set `minimumTemplateScore` from that distribution rather than the synthetic 0.35, and only then consider `templateGatesRecognition`. Turning the gate on without this measurement is the one way this work can make recognition worse than it is today.

- [ ] **Step 5: Record the outcome**

Append the measured scores, the chosen threshold, and whether the gate was enabled to `docs/superpowers/specs/2026-09-11-template-object-localization-design.md`, and commit. If tracking looks worse than the spike promised, set `localFeatureBackend` back to `None` — that is the retreat the field exists for — and say so in the commit.

---

## Self-Review

**Spec coverage.** Section 1 (Domain maths) → Tasks 1-2. Section 2 (verifier, three template routes, blob dialects) → Tasks 5-6. Section 3 (loading survives an unrestorable reference) → Task 4. Section 4 (thresholds, two switches) → Tasks 3, 5, 7. Section 5 (backend selection and retreat) → Task 7. Section 6 (testing, including the corrected claim that `TemplateVerifier` is testable) → Tasks 1-6 each carry their tests; the device pass is Task 8. Non-goals are respected: no schema change, no re-registration, the embedding and stabilizer paths are untouched, no rotation.

**Type consistency.** `LocalVerificationThresholds` is created in Task 3 and consumed under that name in Tasks 4-5, 7. `SeedReferences` is named identically in Tasks 6 and 7. `TemplateMatch.DefaultSearchSize` / `DefaultTemplateFraction` / `DefaultScales` are defined in Task 1 and used in Task 5. `TemplateVerifier.TemplateDescriptorType` is produced in Task 5 and consumed by `AkazeVerifier` in the same task.

**Known gap, deliberate.** `RecognitionSettings`'s constructor overload is described rather than written out in Task 3, because the existing struct has three chained constructors and the implementer must read them before adding a fourth. Every other code step carries its code.
