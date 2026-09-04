# Registration Capture UI and Frame Preprocessing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the 3-view object-registration flow legible (a "n / 3 枚目" progress readout, three progress dots, an on-preview crop guide, instruction copy), correct the sideways camera preview, and feed the embedding a tighter upright center crop.

**Architecture:** Frame orientation is corrected in the controller (`EmbeddingObjectMatcher`) by blitting the `WebCamTexture` into an upright intermediate `RenderTexture` that recognition, registration, and the preview all share, so `IFramePreprocessor` is unchanged. `CameraFramePreprocessor` gains a `cropFraction` constructor argument (default `0.75`) used by both its embedding and verification crops. `EmbeddingModelSpec.DefaultPreprocessingVersion` is bumped, and the already-wired `RecognitionSampleRepository.InvalidateObsolete` call discards every pre-change sample on next run.

**Tech Stack:** Unity 6000.1.7f1, C#/.NET Standard 2.1, Unity Test Framework/NUnit, `uloop` CLI for compile and tests, TextMeshPro, Unity UI (`RawImage`, `AspectRatioFitter`), SQLite-net.

**Spec:** `docs/superpowers/specs/2026-09-04-registration-capture-ui-design.md`

## Global Constraints

- `IFramePreprocessor.PrepareEmbedding` and `IFramePreprocessor.CopyVerificationFrame` signatures must not change.
- The default center-crop fraction is exactly `0.75`.
- `EmbeddingModelSpec.DefaultPreprocessingVersion` becomes exactly `"rgb01-imagenet-mean-std-crop75-upright-v2"`.
- Required registration views stay at `RecognitionSettings.RequiredSamplesPerObject` (`3`); do not hard-code a different count.
- The preview image and the image handed to the embedding must share the same orientation and crop region.
- Pre-change embedding samples are discarded, not migrated. Object names, images, and eye/mouth placement survive (handled by the existing `InvalidateObsolete`, which only nulls feature blobs).
- No custom shader that could be stripped from the IL2CPP build: use `Graphics.Blit`'s default material or a shader registered in Always Included Shaders / loaded from `Resources`.
- Pure geometry and copy live in `Assets/Scripts/Domain/Recognition/` and are covered by EditMode tests. MonoBehaviour wiring is verified by `uloop compile` plus the on-device smoke test in Task 7.
- Android (Samsung SC-03L / Android 12 / arm64) is the runtime target; the Editor must still compile and run its tests without a camera.
- `uloop` is at `C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe` (not on PATH). It is single-flight; never run two `uloop` commands at once.
- Commit only the files a task names. The Unity scene and most assets are git-untracked and stay that way.

---

## File Structure

```text
Assets/Scripts/Domain/Recognition/
  ImageGeometry.cs                     MODIFY  cropFraction overloads + DefaultCropFraction
  FrameOrientation.cs                  CREATE  pure rotation/mirror model + corner mapping
  EmbeddingModelSpec.cs                MODIFY  DefaultPreprocessingVersion bump
  BackendSelectionPolicy.cs            MODIFY  RecognitionMessages: view-prompt + instruction copy
  RegistrationGuideLayout.cs           CREATE  pure crop-guide rect math

Assets/Scripts/Infrastructure/Vision/Preprocessing/
  CameraFramePreprocessor.cs           MODIFY  cropFraction constructor argument

Assets/Scripts/Presentation/Controllers/
  EmbeddingObjectMatcher.cs            MODIFY  _orientedFrame blit; progress dots / guide frame / instruction wiring

Assets/Tests/EditMode/
  ImageGeometryTests.cs                MODIFY  cropFraction cases
  FrameOrientationTests.cs             CREATE
  EmbeddingModelSpecTests.cs           MODIFY  new preprocessing-version value
  RecognitionMessagesTests.cs          CREATE (or extend BackendSelectionPolicyTests.cs)
  RegistrationGuideLayoutTests.cs      CREATE

Assets/Tests/PlayMode/
  CameraFramePreprocessorTests.cs      MODIFY  0.75 crop expectations

Assets/Scenes/ObjectDetectionScene.unity  MODIFY (in the Editor)  guide frame, dots, instruction text, AspectRatioFitter, field wiring
```

Task order: geometry and copy primitives first (Tasks 1, 2), then the controller's orientation blit (Task 3), then the crop + version bump that depends on Task 1 (Task 4), then the registration UI code that depends on Tasks 2 and the copy (Task 5), then the scene wiring that depends on Task 5's serialized fields (Task 6), then on-device verification (Task 7).

---

### Task 1: `ImageGeometry` crop-fraction overloads

**Files:**
- Modify: `Assets/Scripts/Domain/Recognition/ImageGeometry.cs`
- Test: `Assets/Tests/EditMode/ImageGeometryTests.cs`

**Interfaces:**
- Consumes: existing `PixelRect`, `NormalizedRect`, `ValidateSourceSize`.
- Produces:
  - `const float ImageGeometry.DefaultCropFraction = 0.75f`
  - `PixelRect ImageGeometry.CenterCrop(int sourceWidth, int sourceHeight, float cropFraction)`
  - `NormalizedRect ImageGeometry.NormalizedCenterCrop(int sourceWidth, int sourceHeight, float cropFraction)`
  - The existing no-fraction overloads keep behaving as `cropFraction = 1.0f`.

- [ ] **Step 1: Write the failing tests**

Add to `ImageGeometryTests.cs`:

```csharp
[Test]
public void CenterCrop_WithFraction_ReturnsCenteredSquareOfThatFractionOfTheShortSide()
{
    var crop = ImageGeometry.CenterCrop(640, 480, 0.75f);

    Assert.That(crop.Width, Is.EqualTo(360));   // 0.75 * 480
    Assert.That(crop.Height, Is.EqualTo(360));
    Assert.That(crop.X, Is.EqualTo((640 - 360) / 2));
    Assert.That(crop.Y, Is.EqualTo((480 - 360) / 2));
}

[Test]
public void CenterCrop_WithFractionOne_MatchesTheNoFractionOverload()
{
    var withOne = ImageGeometry.CenterCrop(640, 480, 1f);
    var plain = ImageGeometry.CenterCrop(640, 480);

    Assert.That(withOne, Is.EqualTo(plain));
}

[Test]
public void NormalizedCenterCrop_WithFraction_ScalesAndOffsetsBySquareOverSource()
{
    var rect = ImageGeometry.NormalizedCenterCrop(640, 480, 0.75f);

    Assert.That(rect.ScaleX, Is.EqualTo(360f / 640f).Within(1e-5f));
    Assert.That(rect.ScaleY, Is.EqualTo(360f / 480f).Within(1e-5f));
    Assert.That(rect.OffsetX, Is.EqualTo(140f / 640f).Within(1e-5f));
    Assert.That(rect.OffsetY, Is.EqualTo(60f / 480f).Within(1e-5f));
}

[TestCase(0f)]
[TestCase(-0.5f)]
[TestCase(1.5f)]
public void CenterCrop_WithFractionOutsideZeroToOne_Throws(float cropFraction)
{
    Assert.That(
        () => ImageGeometry.CenterCrop(640, 480, cropFraction),
        Throws.TypeOf<ArgumentOutOfRangeException>());
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode EditMode --filter-value ImageGeometryTests`

Expected: compile error — `CenterCrop` has no 3-argument overload.

- [ ] **Step 3: Implement the overloads**

In `ImageGeometry.cs`, add the constant next to `EmbeddingImageSize`:

```csharp
/// <summary>Fraction of the short side kept by the registration/recognition center crop.</summary>
public const float DefaultCropFraction = 0.75f;
```

Add the overloads (place them right after the existing `CenterCrop` / `NormalizedCenterCrop`):

```csharp
public static PixelRect CenterCrop(int sourceWidth, int sourceHeight, float cropFraction)
{
    ValidateSourceSize(sourceWidth, sourceHeight);
    if (cropFraction <= 0f || cropFraction > 1f)
    {
        throw new ArgumentOutOfRangeException(
            nameof(cropFraction), "The crop fraction must be in the range (0, 1].");
    }

    var shortSide = Math.Min(sourceWidth, sourceHeight);
    var side = (int)Math.Round(shortSide * cropFraction, MidpointRounding.AwayFromZero);
    side = Math.Max(1, Math.Min(side, shortSide));
    return new PixelRect((sourceWidth - side) / 2, (sourceHeight - side) / 2, side, side);
}

public static NormalizedRect NormalizedCenterCrop(int sourceWidth, int sourceHeight, float cropFraction)
{
    var crop = CenterCrop(sourceWidth, sourceHeight, cropFraction);
    return new NormalizedRect(
        crop.Width / (float)sourceWidth,
        crop.Height / (float)sourceHeight,
        crop.X / (float)sourceWidth,
        crop.Y / (float)sourceHeight);
}
```

- [ ] **Step 4: Run the tests and verify they pass**

Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode EditMode --filter-value ImageGeometryTests`

Expected: PASS, including the pre-existing `CenterCrop_*` tests.

- [ ] **Step 5: Commit**

```bash
git add ObjecTalk-toritori\(1\)/Assets/Scripts/Domain/Recognition/ImageGeometry.cs ObjecTalk-toritori\(1\)/Assets/Tests/EditMode/ImageGeometryTests.cs
git commit -m "feat: add crop-fraction overloads to ImageGeometry"
```

---

### Task 2: `FrameOrientation` pure model

**Files:**
- Create: `Assets/Scripts/Domain/Recognition/FrameOrientation.cs`
- Create: `Assets/Tests/EditMode/FrameOrientationTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks. `UnityEngine.Vector2` only (already referenced by the assembly).
- Produces:
  - `readonly struct FrameOrientation` with `int RotationDegrees` (0/90/180/270) and `bool Mirrored`.
  - `static FrameOrientation FrameOrientation.FromWebCam(int videoRotationAngle, bool videoVerticallyMirrored)` — normalizes the angle to one of 0/90/180/270.
  - `(int width, int height) FrameOrientation.OrientedSize(int sourceWidth, int sourceHeight)` — swaps width/height for 90 and 270.
  - `Vector2[] FrameOrientation.SourceUVsForOrientedCorners()` — returns the 4 source UVs (bottom-left, bottom-right, top-right, top-left of the *oriented* quad) to sample when drawing an upright frame. Used by the controller's blit in Task 3.
  - `bool FrameOrientation.IsIdentity` — true when `RotationDegrees == 0 && !Mirrored`.

- [ ] **Step 1: Write the failing tests**

`Assets/Tests/EditMode/FrameOrientationTests.cs`:

```csharp
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;
using UnityEngine;

namespace ObjecTalk.Tests.EditMode
{
    public class FrameOrientationTests
    {
        [TestCase(0, 0)]
        [TestCase(90, 90)]
        [TestCase(180, 180)]
        [TestCase(270, 270)]
        [TestCase(-90, 270)]
        [TestCase(450, 90)]
        [TestCase(360, 0)]
        public void FromWebCam_NormalizesTheAngle(int input, int expected)
        {
            var orientation = FrameOrientation.FromWebCam(input, false);
            Assert.That(orientation.RotationDegrees, Is.EqualTo(expected));
        }

        [Test]
        public void FromWebCam_CarriesTheMirrorFlag()
        {
            Assert.That(FrameOrientation.FromWebCam(0, true).Mirrored, Is.True);
        }

        [Test]
        public void OrientedSize_SwapsWidthAndHeightForQuarterTurns()
        {
            var ninety = FrameOrientation.FromWebCam(90, false);
            Assert.That(ninety.OrientedSize(1280, 720), Is.EqualTo((720, 1280)));

            var oneEighty = FrameOrientation.FromWebCam(180, false);
            Assert.That(oneEighty.OrientedSize(1280, 720), Is.EqualTo((1280, 720)));
        }

        [Test]
        public void Identity_LeavesCornerUVsAtTheUnitSquare()
        {
            var uvs = FrameOrientation.FromWebCam(0, false).SourceUVsForOrientedCorners();

            Assert.That(uvs[0], Is.EqualTo(new Vector2(0, 0)));
            Assert.That(uvs[1], Is.EqualTo(new Vector2(1, 0)));
            Assert.That(uvs[2], Is.EqualTo(new Vector2(1, 1)));
            Assert.That(uvs[3], Is.EqualTo(new Vector2(0, 1)));
        }

        [Test]
        public void Rotate90_MapsTheOrientedBottomLeftToTheSourceTopLeft()
        {
            // A 90 degree device rotation: the upright image's bottom-left corner
            // samples the source's top-left.
            var uvs = FrameOrientation.FromWebCam(90, false).SourceUVsForOrientedCorners();

            Assert.That(uvs[0], Is.EqualTo(new Vector2(0, 1)));  // oriented BL  <- source TL
            Assert.That(uvs[1], Is.EqualTo(new Vector2(0, 0)));  // oriented BR  <- source BL
            Assert.That(uvs[2], Is.EqualTo(new Vector2(1, 0)));  // oriented TR  <- source BR
            Assert.That(uvs[3], Is.EqualTo(new Vector2(1, 1)));  // oriented TL  <- source TR
        }

        [Test]
        public void Mirrored_FlipsTheCornerUVsHorizontally()
        {
            var plain = FrameOrientation.FromWebCam(0, false).SourceUVsForOrientedCorners();
            var mirrored = FrameOrientation.FromWebCam(0, true).SourceUVsForOrientedCorners();

            Assert.That(mirrored[0].x, Is.EqualTo(1f - plain[0].x));
            Assert.That(mirrored[1].x, Is.EqualTo(1f - plain[1].x));
        }
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode EditMode --filter-value FrameOrientationTests`

Expected: compile error — `FrameOrientation` does not exist.

- [ ] **Step 3: Implement `FrameOrientation`**

`Assets/Scripts/Domain/Recognition/FrameOrientation.cs`:

```csharp
using System;
using UnityEngine;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// How a raw camera frame is rotated and mirrored relative to an upright image. Pure data
    /// plus the corner-UV mapping used to draw the frame the right way up. Engine-free maths so
    /// it can be asserted without a graphics device.
    /// </summary>
    public readonly struct FrameOrientation : IEquatable<FrameOrientation>
    {
        private FrameOrientation(int rotationDegrees, bool mirrored)
        {
            RotationDegrees = rotationDegrees;
            Mirrored = mirrored;
        }

        public int RotationDegrees { get; }

        public bool Mirrored { get; }

        public bool IsIdentity => RotationDegrees == 0 && !Mirrored;

        public static FrameOrientation FromWebCam(int videoRotationAngle, bool videoVerticallyMirrored)
        {
            var normalized = ((videoRotationAngle % 360) + 360) % 360;
            var quarter = (int)Math.Round(normalized / 90f) % 4;
            return new FrameOrientation(quarter * 90, videoVerticallyMirrored);
        }

        public (int width, int height) OrientedSize(int sourceWidth, int sourceHeight)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceWidth), "The source frame has no pixels.");
            }

            return RotationDegrees == 90 || RotationDegrees == 270
                ? (sourceHeight, sourceWidth)
                : (sourceWidth, sourceHeight);
        }

        /// <summary>
        /// Source UVs to sample for the oriented quad's corners, in the order
        /// bottom-left, bottom-right, top-right, top-left. Feed these to a textured quad drawn
        /// over the unit rectangle to produce an upright, un-mirrored frame.
        /// </summary>
        public Vector2[] SourceUVsForOrientedCorners()
        {
            // Corners of the oriented (upright) quad, before any rotation, as source UVs.
            var uv = new[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1),
            };

            var shift = RotationDegrees / 90; // number of 90-degree CCW steps to undo
            var rotated = new Vector2[4];
            for (var i = 0; i < 4; i++)
            {
                rotated[i] = uv[(i + shift) % 4];
            }

            if (Mirrored)
            {
                for (var i = 0; i < 4; i++)
                {
                    rotated[i] = new Vector2(1f - rotated[i].x, rotated[i].y);
                }
            }

            return rotated;
        }

        public bool Equals(FrameOrientation other) =>
            RotationDegrees == other.RotationDegrees && Mirrored == other.Mirrored;

        public override bool Equals(object obj) => obj is FrameOrientation other && Equals(other);

        public override int GetHashCode() => (RotationDegrees * 397) ^ (Mirrored ? 1 : 0);

        public override string ToString() => $"FrameOrientation({RotationDegrees}, mirrored={Mirrored})";
    }
}
```

- [ ] **Step 4: Run the tests and verify they pass**

Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode EditMode --filter-value FrameOrientationTests`

Expected: PASS. If `Rotate90_*` or `Identity_*` fail, adjust the `shift` direction (the corner rotation is `+shift` vs `-shift`) until the identity case and the 90 case both hold, then re-run.

- [ ] **Step 5: Commit**

```bash
git add ObjecTalk-toritori\(1\)/Assets/Scripts/Domain/Recognition/FrameOrientation.cs ObjecTalk-toritori\(1\)/Assets/Tests/EditMode/FrameOrientationTests.cs
git commit -m "feat: add FrameOrientation camera-frame model"
```

---

### Task 3: Upright camera frame in `EmbeddingObjectMatcher`

**Files:**
- Modify: `Assets/Scripts/Presentation/Controllers/EmbeddingObjectMatcher.cs`

**Interfaces:**
- Consumes: `FrameOrientation` (Task 2). Existing `_recognitionService.StartRecognition(Texture, double)`, `_recognitionService.CaptureRegistrationView(Texture)`, `webcamTexture`, `registerCameraDisplay`, `talkCameraDisplay`.
- Produces: a private `RenderTexture _orientedFrame` that is upright and un-mirrored, plus a helper `Texture ActiveCameraFrame()` returning `_orientedFrame` (or `webcamTexture` on fallback). Task 5 reads `ActiveCameraFrame()` nowhere new — this task rewires the two existing call sites.

- [ ] **Step 1: Read the current camera wiring**

Read `EmbeddingObjectMatcher.cs` around: the field block (lines ~59-135), `EnablePanelRoutine` / `OnEnable` camera setup (lines ~300-367), `Update` (lines ~437-456), `OnDestroy` camera teardown (lines ~494-505), and `OnRegisterStart` capture path (lines ~663-726). Note that `StartRecognition` is called with `webcamTexture` at line ~450 and `CaptureRegistrationView` with `webcamTexture` at line ~706.

- [ ] **Step 2: Add the oriented-frame fields and helpers**

Add fields near `webcamTexture`:

```csharp
private RenderTexture _orientedFrame;
private Material _orientBlitMaterial;
private Mesh _orientQuad;
private FrameOrientation _frameOrientation;
private int _orientedSourceWidth;
private int _orientedSourceHeight;
```

Add these methods (private, near the camera helpers):

```csharp
// The frame recognition, registration, and the preview all read. _orientedFrame once it is
// built for the current camera size/orientation; the raw texture until then.
private Texture ActiveCameraFrame()
{
    return _orientedFrame != null ? (Texture)_orientedFrame : webcamTexture;
}

private void EnsureOrientedFrame()
{
    if (webcamTexture == null || webcamTexture.width <= 16 || webcamTexture.height <= 16)
    {
        return; // camera has not delivered a real frame yet
    }

    var orientation = FrameOrientation.FromWebCam(
        (int)webcamTexture.videoRotationAngle, webcamTexture.videoVerticallyMirrored);

    if (_orientedFrame != null
        && _orientedSourceWidth == webcamTexture.width
        && _orientedSourceHeight == webcamTexture.height
        && _frameOrientation.Equals(orientation))
    {
        return;
    }

    ReleaseOrientedFrame();
    _frameOrientation = orientation;
    _orientedSourceWidth = webcamTexture.width;
    _orientedSourceHeight = webcamTexture.height;
    var (w, h) = orientation.OrientedSize(webcamTexture.width, webcamTexture.height);
    _orientedFrame = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
    {
        name = "ObjecTalk Oriented Camera Frame",
        wrapMode = TextureWrapMode.Clamp,
        filterMode = FilterMode.Bilinear,
    };
    _orientedFrame.Create();

    if (_orientBlitMaterial == null)
    {
        _orientBlitMaterial = new Material(Shader.Find("Unlit/Texture"));
    }
    if (_orientQuad == null)
    {
        _orientQuad = BuildUnitQuad();
    }

    ApplyCameraDisplay(registerCameraDisplay);
    ApplyCameraDisplay(talkCameraDisplay);
}

private void RenderOrientedFrame()
{
    if (_orientedFrame == null || webcamTexture == null)
    {
        return;
    }

    if (_frameOrientation.IsIdentity)
    {
        Graphics.Blit(webcamTexture, _orientedFrame);
        return;
    }

    var uv = _frameOrientation.SourceUVsForOrientedCorners();
    _orientQuad.SetUVs(0, new List<Vector2>(uv));
    _orientQuad.UploadMeshData(false);

    var previous = RenderTexture.active;
    RenderTexture.active = _orientedFrame;
    _orientBlitMaterial.mainTexture = webcamTexture;
    _orientBlitMaterial.SetPass(0);
    GL.PushMatrix();
    GL.LoadOrtho();
    Graphics.DrawMeshNow(_orientQuad, Vector3.zero, Quaternion.identity);
    GL.PopMatrix();
    RenderTexture.active = previous;
}

private static Mesh BuildUnitQuad()
{
    return new Mesh
    {
        vertices = new[]
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0),
            new Vector3(1, 1, 0), new Vector3(0, 1, 0),
        },
        uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
        triangles = new[] { 0, 1, 2, 0, 2, 3 },
    };
}

private void ApplyCameraDisplay(RawImage display)
{
    if (display == null)
    {
        return;
    }

    display.texture = ActiveCameraFrame();
    display.color = Color.white;

    var fitter = display.GetComponent<AspectRatioFitter>();
    if (fitter != null && _orientedFrame != null && _orientedFrame.height > 0)
    {
        fitter.aspectRatio = _orientedFrame.width / (float)_orientedFrame.height;
    }
}

private void ReleaseOrientedFrame()
{
    if (_orientedFrame != null)
    {
        _orientedFrame.Release();
        Destroy(_orientedFrame);
        _orientedFrame = null;
    }
}
```

Add `using UnityEngine.UI;` is already present; add `using System.Collections.Generic;` if not already imported (it is, line 5).

- [ ] **Step 3: Rewire the camera setup, per-frame update, and teardown**

In the camera setup (where `registerCameraDisplay.texture = webcamTexture` is assigned, ~lines 320-329): replace the two direct `.texture = webcamTexture` assignments with nothing here — the displays are set by `ApplyCameraDisplay` once the oriented frame exists. Immediately after `webcamTexture.Play()` leave as-is.

In `Update`, before the recognition/registration work, call `EnsureOrientedFrame()` then `RenderOrientedFrame()` when `webcamTexture != null && webcamTexture.didUpdateThisFrame`. Change the recognition call from `_recognitionService.StartRecognition(webcamTexture, Time.timeAsDouble)` to `_recognitionService.StartRecognition(ActiveCameraFrame(), Time.timeAsDouble)`.

In `OnRegisterStart` (the capture handler ~line 706), change `_recognitionService.CaptureRegistrationView(webcamTexture)` to `_recognitionService.CaptureRegistrationView(ActiveCameraFrame())`. Keep the `tempSnapshot` block reading `webcamTexture` for now (the placement still image); it will be updated in Step 4 to use `ActiveCameraFrame()` so the placement still matches the recognized crop.

In `OnDestroy` (camera teardown), call `ReleaseOrientedFrame()` and destroy `_orientBlitMaterial` / `_orientQuad` if not null.

- [ ] **Step 4: Point the placement snapshot at the oriented frame**

In `OnRegisterStart`, the `tempSnapshot` capture currently does `tempSnapshot.SetPixels(webcamTexture.GetPixels())`. Replace it with a read from `ActiveCameraFrame()`:

```csharp
var active = ActiveCameraFrame();
var readable = RenderTexture.GetTemporary(active.width, active.height, 0, RenderTextureFormat.ARGB32);
Graphics.Blit(active, readable);
var previousActive = RenderTexture.active;
RenderTexture.active = readable;
tempSnapshot = new Texture2D(readable.width, readable.height, TextureFormat.RGB24, false);
tempSnapshot.ReadPixels(new Rect(0, 0, readable.width, readable.height), 0, 0);
tempSnapshot.Apply();
RenderTexture.active = previousActive;
RenderTexture.ReleaseTemporary(readable);
```

Keep the existing `try/catch` around it that calls `DestroySnapshot()` on failure.

- [ ] **Step 5: Compile**

Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" compile`

Expected: `ErrorCount: 0`. Fix any missing-using or signature errors before continuing.

- [ ] **Step 6: Run the recognition PlayMode tests**

Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode PlayMode --filter-value EmbeddingObjectMatcherFlowTests --skip-compile`
Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode PlayMode --filter-value ObjectRecognitionLifecycleTests --skip-compile`

Expected: PASS. These do not instantiate the MonoBehaviour, so they confirm the recognition flow still compiles and links against the changed controller assembly.

- [ ] **Step 7: Commit**

```bash
git add ObjecTalk-toritori\(1\)/Assets/Scripts/Presentation/Controllers/EmbeddingObjectMatcher.cs
git commit -m "feat: feed recognition and preview an upright camera frame"
```

---

### Task 4: 75% crop in `CameraFramePreprocessor` and preprocessing-version bump

**Files:**
- Modify: `Assets/Scripts/Infrastructure/Vision/Preprocessing/CameraFramePreprocessor.cs`
- Modify: `Assets/Scripts/Domain/Recognition/EmbeddingModelSpec.cs`
- Test: `Assets/Tests/PlayMode/CameraFramePreprocessorTests.cs`
- Test: `Assets/Tests/EditMode/EmbeddingModelSpecTests.cs`

**Interfaces:**
- Consumes: `ImageGeometry.CenterCrop(w, h, cropFraction)` / `NormalizedCenterCrop(w, h, cropFraction)` and `ImageGeometry.DefaultCropFraction` (Task 1).
- Produces:
  - `CameraFramePreprocessor(int embeddingSize, int maximumVerificationSize, float cropFraction)` — third argument, default `ImageGeometry.DefaultCropFraction` via a chained constructor.
  - The parameterless and two-argument constructors keep working and default `cropFraction` to `ImageGeometry.DefaultCropFraction`.
  - `EmbeddingModelSpec.Default.PreprocessingVersion == "rgb01-imagenet-mean-std-crop75-upright-v2"`.

- [ ] **Step 1: Update the EmbeddingModelSpec test**

Open `Assets/Tests/EditMode/EmbeddingModelSpecTests.cs`. If a test asserts the preprocessing version string, change its expected value to `"rgb01-imagenet-mean-std-crop75-upright-v2"`. If no such test exists, add:

```csharp
[Test]
public void Default_UsesTheCrop75UprightPreprocessingVersion()
{
    Assert.That(
        EmbeddingModelSpec.Default.PreprocessingVersion,
        Is.EqualTo("rgb01-imagenet-mean-std-crop75-upright-v2"));
}
```

- [ ] **Step 2: Update the preprocessor test expectations**

In `Assets/Tests/PlayMode/CameraFramePreprocessorTests.cs`, find the tests that assert the verification-frame size or the center-crop region. The verification size derives from the cropped square: with `cropFraction = 0.75`, a 1280x720 source crops to `round(720 * 0.75) = 540`, then `min(540, 320) = 320`. Adjust any hard-coded crop-size expectation to reflect the 0.75 factor. Add one explicit test:

```csharp
[UnityTest]
public IEnumerator PrepareEmbedding_CropsSeventyFivePercentOfTheShortSide()
{
    var source = new RenderTexture(1280, 720, 0);
    source.Create();
    var preprocessor = new CameraFramePreprocessor();

    var embedding = preprocessor.PrepareEmbedding(source);

    Assert.That(embedding.width, Is.EqualTo(ImageGeometry.EmbeddingImageSize));
    Assert.That(embedding.height, Is.EqualTo(ImageGeometry.EmbeddingImageSize));

    preprocessor.Dispose();
    source.Release();
    yield break;
}
```

(The 224x224 output size is unchanged; the crop is a UV region on the blit, so the assertion here is that the pipeline still produces the model's input size after the tighter crop. The crop *fraction* itself is covered by `ImageGeometryTests` in Task 1.)

- [ ] **Step 3: Run the tests and verify they fail**

Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode EditMode --filter-value EmbeddingModelSpecTests`
Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode PlayMode --filter-value CameraFramePreprocessorTests`

Expected: EmbeddingModelSpec test fails on the version string; preprocessor tests still pass unless one hard-coded a crop size (fix those expectations now).

- [ ] **Step 4: Bump the preprocessing version**

In `EmbeddingModelSpec.cs`:

```csharp
private const string DefaultPreprocessingVersion = "rgb01-imagenet-mean-std-crop75-upright-v2";
```

- [ ] **Step 5: Add the `cropFraction` constructor argument**

In `CameraFramePreprocessor.cs`:

- Add field `private readonly float _cropFraction;`.
- Change the parameterless constructor to chain with the fraction:

```csharp
public CameraFramePreprocessor()
    : this(ImageGeometry.EmbeddingImageSize, ImageGeometry.MaximumVerificationImageSize, ImageGeometry.DefaultCropFraction)
{
}

public CameraFramePreprocessor(int embeddingSize, int maximumVerificationSize)
    : this(embeddingSize, maximumVerificationSize, ImageGeometry.DefaultCropFraction)
{
}

public CameraFramePreprocessor(int embeddingSize, int maximumVerificationSize, float cropFraction)
{
    // ...existing validation of embeddingSize and maximumVerificationSize...
    if (cropFraction <= 0f || cropFraction > 1f)
    {
        throw new ArgumentOutOfRangeException(nameof(cropFraction), "The crop fraction must be in the range (0, 1].");
    }

    _embeddingSize = embeddingSize;
    _maximumVerificationSize = maximumVerificationSize;
    _cropFraction = cropFraction;
}
```

- In `PrepareEmbedding`, change `ImageGeometry.NormalizedCenterCrop(source.width, source.height)` to `ImageGeometry.NormalizedCenterCrop(source.width, source.height, _cropFraction)`.
- In `CopyVerificationFrame`, change both `ImageGeometry.CenterCrop(source.width, source.height)` and `ImageGeometry.NormalizedCenterCrop(source.width, source.height)` to pass `_cropFraction`.

- [ ] **Step 6: Run the tests and verify they pass**

Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode EditMode --filter-value EmbeddingModelSpecTests`
Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode PlayMode --filter-value CameraFramePreprocessorTests`
Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode EditMode`

Expected: all PASS. The full EditMode run catches any test elsewhere that pinned the old preprocessing-version string (for example `EmbeddingObjectMatcherFlowTests`'s `CurrentVersion` reads `EmbeddingModelSpec.Default.PreprocessingVersion`, so it tracks automatically; a test with a literal string does not).

- [ ] **Step 7: Verify the obsolete-sample wipe is already wired**

Read `Assets/Scripts/Core/Recognition/ObjectRecognitionService.cs` around `LoadSamples` (line ~117): confirm it calls `_store.InvalidateObsolete(_dataVersion)` before `LoadValidSamples`. Read `RecognitionSampleRepository.InvalidateObsolete` (line ~420): confirm it `DELETE`s rows where any version column differs and nulls the legacy `objects` / `object_features` blob columns. No code change — this is the "discard existing samples" mechanism; the version bump in Step 4 triggers it on next run. If `RecognitionSampleRepositoryTests.cs` lacks a test that `InvalidateObsolete` removes mismatched rows and keeps matching ones, add one now against an in-memory `SQLiteConnection`.

- [ ] **Step 8: Commit**

```bash
git add ObjecTalk-toritori\(1\)/Assets/Scripts/Infrastructure/Vision/Preprocessing/CameraFramePreprocessor.cs ObjecTalk-toritori\(1\)/Assets/Scripts/Domain/Recognition/EmbeddingModelSpec.cs ObjecTalk-toritori\(1\)/Assets/Tests/PlayMode/CameraFramePreprocessorTests.cs ObjecTalk-toritori\(1\)/Assets/Tests/EditMode/EmbeddingModelSpecTests.cs ObjecTalk-toritori\(1\)/Assets/Tests/EditMode/RecognitionSampleRepositoryTests.cs
git commit -m "feat: crop the embedding frame to 75% and bump the preprocessing version"
```

---

### Task 5: Registration progress UI in `EmbeddingObjectMatcher`

**Files:**
- Modify: `Assets/Scripts/Domain/Recognition/BackendSelectionPolicy.cs` (the `RecognitionMessages` class)
- Create: `Assets/Scripts/Domain/Recognition/RegistrationGuideLayout.cs`
- Modify: `Assets/Scripts/Presentation/Controllers/EmbeddingObjectMatcher.cs`
- Test: `Assets/Tests/EditMode/RecognitionMessagesTests.cs` (create) — or add cases to `BackendSelectionPolicyTests.cs`
- Test: `Assets/Tests/EditMode/RegistrationGuideLayoutTests.cs` (create)

**Interfaces:**
- Consumes: `RecognitionSettings.RequiredSamplesPerObject`, `ImageGeometry.DefaultCropFraction`, the `HandleTickResult` `CaptureAccepted` branch, `BeginRegistrationSession`.
- Produces:
  - `string RecognitionMessages.RegistrationViewPrompt(int nextView, int totalViews)` — e.g. `"1 / 3 枚目 — 物体を枠の中央に収めて撮影"`.
  - `string RecognitionMessages.RegistrationAngleHint` — `"角度を少し変えてください"`.
  - `static Vector2 RegistrationGuideLayout.FrameSize(Vector2 previewSize, float cropFraction)` — the guide rect's size: a centered square of `cropFraction * min(previewSize.x, previewSize.y)`.
  - Serialized fields on `EmbeddingObjectMatcher`: `Image[] registrationDots`, `RectTransform captureGuideFrame`, `TMP_Text registrationInstructionText`. (`registrationStatusText` already exists.)

- [ ] **Step 1: Write the failing copy + layout tests**

`Assets/Tests/EditMode/RecognitionMessagesTests.cs`:

```csharp
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class RecognitionMessagesTests
    {
        [Test]
        public void RegistrationViewPrompt_ReadsAsNofTotalWithFramingGuidance()
        {
            var text = RecognitionMessages.RegistrationViewPrompt(1, 3);

            Assert.That(text, Does.Contain("1 / 3"));
            Assert.That(text, Does.Contain("枠"));
        }

        [Test]
        public void RegistrationAngleHint_IsNonEmpty()
        {
            Assert.That(RecognitionMessages.RegistrationAngleHint, Is.Not.Empty);
        }
    }
}
```

`Assets/Tests/EditMode/RegistrationGuideLayoutTests.cs`:

```csharp
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;
using UnityEngine;

namespace ObjecTalk.Tests.EditMode
{
    public class RegistrationGuideLayoutTests
    {
        [Test]
        public void FrameSize_IsACenteredSquareOfTheFractionOfTheShortSide()
        {
            var size = RegistrationGuideLayout.FrameSize(new Vector2(1080, 1920), 0.75f);

            Assert.That(size.x, Is.EqualTo(810f).Within(0.01f)); // 0.75 * 1080
            Assert.That(size.y, Is.EqualTo(810f).Within(0.01f));
        }

        [Test]
        public void FrameSize_ClampsFractionToOne()
        {
            var size = RegistrationGuideLayout.FrameSize(new Vector2(600, 800), 2f);

            Assert.That(size.x, Is.EqualTo(600f).Within(0.01f));
        }
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode EditMode --filter-value RecognitionMessagesTests`
Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode EditMode --filter-value RegistrationGuideLayoutTests`

Expected: compile errors — the members do not exist.

- [ ] **Step 3: Add the copy and the layout helper**

In `BackendSelectionPolicy.cs`, inside `RecognitionMessages`:

```csharp
/// <summary>Which view the user is about to take, with framing guidance, e.g. "1 / 3 枚目 — ...".</summary>
public static string RegistrationViewPrompt(int nextView, int totalViews)
{
    if (nextView < 1)
    {
        throw new ArgumentOutOfRangeException(nameof(nextView), "The next view is 1-based.");
    }

    if (totalViews < nextView)
    {
        throw new ArgumentOutOfRangeException(nameof(totalViews), "The total cannot be below the next view.");
    }

    return $"{nextView} / {totalViews} 枚目 — 物体を枠の中央に収めて撮影";
}

/// <summary>Added to the instruction from the second view onward.</summary>
public const string RegistrationAngleHint = "角度を少し変えてください";
```

`Assets/Scripts/Domain/Recognition/RegistrationGuideLayout.cs`:

```csharp
using System;
using UnityEngine;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// Size of the on-preview capture guide: a centered square matching the fraction of the
    /// camera frame the embedding actually keeps. Pure maths so it is testable without a canvas.
    /// </summary>
    public static class RegistrationGuideLayout
    {
        public static Vector2 FrameSize(Vector2 previewSize, float cropFraction)
        {
            if (previewSize.x <= 0f || previewSize.y <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(previewSize), "The preview has no size.");
            }

            var fraction = Mathf.Clamp(cropFraction, 0f, 1f);
            var side = Mathf.Min(previewSize.x, previewSize.y) * fraction;
            return new Vector2(side, side);
        }
    }
}
```

- [ ] **Step 4: Run the tests and verify they pass**

Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode EditMode --filter-value RecognitionMessagesTests`
Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode EditMode --filter-value RegistrationGuideLayoutTests`

Expected: PASS.

- [ ] **Step 5: Add the serialized fields and wiring in `EmbeddingObjectMatcher`**

Under the `[Header("Registration Mode UI")]` block, add:

```csharp
[Tooltip("撮影進捗のドット3個。左から撮影済みを点灯する。")]
public Image[] registrationDots;

[Tooltip("プレビュー上のキャプチャ範囲ガイド枠。実際に embedding に渡る中央ボックスと一致させる。")]
public RectTransform captureGuideFrame;

[Tooltip("撮影の狙いを説明するテキスト。")]
public TMP_Text registrationInstructionText;

[Range(0.3f, 1f)]
[Tooltip("プレビュー上のガイド枠の大きさ（短辺に対する割合）。ImageGeometry.DefaultCropFraction に合わせる。")]
public float guideFractionOfPreview = ImageGeometry.DefaultCropFraction;
```

Add helper methods:

```csharp
private int _capturedViewCount;

private void SetRegistrationProgress(int capturedViews)
{
    _capturedViewCount = capturedViews;
    var total = RecognitionSettings.RequiredSamplesPerObject;
    var nextView = Mathf.Min(capturedViews + 1, total);

    if (registrationStatusText != null)
    {
        registrationStatusText.text = capturedViews >= total
            ? RecognitionMessages.RegistrationComplete
            : RecognitionMessages.RegistrationViewPrompt(nextView, total);
    }

    if (registrationInstructionText != null)
    {
        registrationInstructionText.text = capturedViews >= 1
            ? RecognitionMessages.RegistrationAngleHint
            : string.Empty;
    }

    if (registrationDots != null)
    {
        for (var i = 0; i < registrationDots.Length; i++)
        {
            if (registrationDots[i] != null)
            {
                registrationDots[i].enabled = i < capturedViews;
            }
        }
    }
}

private void SetGuideFrameActive(bool active)
{
    if (captureGuideFrame == null)
    {
        return;
    }

    captureGuideFrame.gameObject.SetActive(active);
    if (!active)
    {
        return;
    }

    var parent = captureGuideFrame.parent as RectTransform;
    var previewSize = parent != null ? parent.rect.size : captureGuideFrame.rect.size;
    if (previewSize.x > 0f && previewSize.y > 0f)
    {
        captureGuideFrame.sizeDelta = RegistrationGuideLayout.FrameSize(previewSize, guideFractionOfPreview);
        captureGuideFrame.anchoredPosition = Vector2.zero;
    }
}
```

- [ ] **Step 6: Call the helpers from the registration lifecycle**

- In `BeginRegistrationSession` (line ~648): replace `SetStatusText(RecognitionMessages.RegistrationProgress(0, RecognitionSettings.RequiredSamplesPerObject))` with `SetRegistrationProgress(0);` and add `SetGuideFrameActive(true);`.
- In `HandleTickResult`'s `CaptureAccepted` branch (line ~473): replace the `SetStatusText(RecognitionMessages.RegistrationProgress(...))` call with `SetRegistrationProgress(result.CapturedViewCount);`. Keep the `if (result.CapturedViewCount >= RecognitionSettings.RequiredSamplesPerObject)` check and `EnterPlacementFromCaptures()` / `SetRegisterButtonInteractable(true)` as they are.
- In `EnterPlacementFromCaptures` (line ~728) and `OnBackButtonClicked` (line ~795) and `OnDisable` (line ~386): add `SetGuideFrameActive(false);`.
- Leave the `line ~888` `RegistrationProgress` call (that is inside re-registration prompting for an existing object) unchanged, or switch it to `SetRegistrationProgress` too if it is on the same capture path — read the surrounding method first and match its intent.

- [ ] **Step 7: Compile and run the flow tests**

Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" compile`
Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode EditMode`
Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode PlayMode --filter-value EmbeddingObjectMatcherFlowTests --skip-compile`

Expected: `ErrorCount: 0`, all EditMode PASS, flow tests PASS.

- [ ] **Step 8: Commit**

```bash
git add ObjecTalk-toritori\(1\)/Assets/Scripts/Domain/Recognition/BackendSelectionPolicy.cs ObjecTalk-toritori\(1\)/Assets/Scripts/Domain/Recognition/RegistrationGuideLayout.cs ObjecTalk-toritori\(1\)/Assets/Scripts/Presentation/Controllers/EmbeddingObjectMatcher.cs ObjecTalk-toritori\(1\)/Assets/Tests/EditMode/RecognitionMessagesTests.cs ObjecTalk-toritori\(1\)/Assets/Tests/EditMode/RegistrationGuideLayoutTests.cs
git commit -m "feat: show 3-view registration progress and a crop guide"
```

---

### Task 6: Scene wiring in `ObjectDetectionScene.unity`

**Files:**
- Modify (in the Unity Editor): `Assets/Scenes/ObjectDetectionScene.unity` (git-untracked; not committed)

**Interfaces:**
- Consumes: the serialized fields added in Tasks 3 and 5 — `registrationDots`, `captureGuideFrame`, `registrationInstructionText`, `guideFractionOfPreview`.
- Produces: no code. A scene where register mode shows the progress readout, dots, guide frame, and instruction, and both camera `RawImage`s have an `AspectRatioFitter`.

- [ ] **Step 1: Launch the Editor on the main project**

Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" launch` from `D:\newest_toritori\ObjecTalk\ObjecTalk-toritori(1)`. Wait for `"Unity CLI Loop is ready."` (a cold open re-imports assets and can exceed the 600s launch timeout while still succeeding — re-run a light `uloop` command to confirm readiness).

- [ ] **Step 2: Add the guide frame**

Under `Canvas/MatcherRootPanel/RegisterCameraPanel` (the panel that holds `RegisterCameraDisplay`), add a UI `Image` named `CaptureGuideFrame`:
- Anchor: center. Pivot: `(0.5, 0.5)`. `anchoredPosition` `(0,0)`.
- Sprite: a 9-sliced border/outline sprite (reuse an existing frame sprite in `Assets/` if one exists; otherwise a plain 2-3px outline via `Image` type Sliced with a border sprite). Color: high-contrast, ~70% alpha.
- `raycastTarget` off.
- Its size is set at runtime by `SetGuideFrameActive`, so the authored size does not matter; give it a visible default.

Use `uloop execute-dynamic-code` with a `--code-file` script to create and parent the GameObject if hand-editing the scene is impractical.

- [ ] **Step 3: Add the progress dots**

Under `RegisterCameraPanel`, add an empty `RegistrationDots` with a `HorizontalLayoutGroup`, holding three `Image` children (`Dot0`, `Dot1`, `Dot2`), each a small filled circle sprite. The controller toggles `Image.enabled` per capture, so all three should start disabled or dimmed; a paired "empty" background per slot is optional polish, not required.

- [ ] **Step 4: Add the instruction text**

Under `RegisterCameraPanel`, add a `TMP_Text` named `RegistrationInstructionText` below the guide frame. Font: the project's Japanese TMP font asset if it resolves; otherwise leave default and note that the on-device tofu issue (tracked separately) will affect it. Empty string by default.

- [ ] **Step 5: Add `AspectRatioFitter` to the camera displays**

On `RegisterCameraDisplay` and `TalkCameraDisplay` (the `RawImage`s), add an `AspectRatioFitter` with Aspect Mode `Fit In Parent` (or `Envelope Parent` if the design prefers fill). The controller sets `aspectRatio` at runtime from the oriented frame.

- [ ] **Step 6: Wire the `EmbeddingObjectMatcher` fields**

Select the GameObject with the `EmbeddingObjectMatcher` component (`Canvas/MatcherRootPanel`). In the Inspector:
- `Registration Dots`: size 3, elements `Dot0`, `Dot1`, `Dot2`.
- `Capture Guide Frame`: `CaptureGuideFrame`.
- `Registration Instruction Text`: `RegistrationInstructionText`.
- `Registration Status Text`: confirm it points at the existing status `TMP_Text` (wire it if empty).
- `Guide Fraction Of Preview`: leave at `0.75`.

- [ ] **Step 7: Verify in the Editor and via PlayMode tests**

- Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" get-logs` — confirm no new missing-reference or missing-script errors on scene load.
- Run: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode PlayMode --filter-value ObjectDetectionSceneNavigationTests`
- Enter Play Mode, open the register flow, and screenshot: `"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" screenshot` — confirm the guide frame is centered and the "1 / 3 枚目" readout shows.

Expected: navigation tests PASS; no missing references; guide + readout visible.

- [ ] **Step 8: Note the scene change**

The scene file is git-untracked. Do not `git add` it. Record in the task report that `ObjectDetectionScene.unity` was edited and which GameObjects were added, so the on-device build in Task 7 is understood to depend on this scene state.

---

### Task 7: On-device integration verification

**Files:**
- None (verification only). Modify conversation files only if a defect is found.

**Interfaces:**
- Validates the whole feature end to end on hardware. Produces no runtime API.

- [ ] **Step 1: Close the Editor and build the APK**

- Stop the running Unity Editor (free RAM; batchmode + interactive together OOMs this 16 GB machine).
- Confirm C: has >= 6 GB free.
- Run (background): `"C:\Program Files\Unity\Hub\Editor\6000.1.7f1-x86_64\Editor\Unity.exe" -batchmode -quit -projectPath "D:\newest_toritori\ObjecTalk\ObjecTalk-toritori(1)" -buildTarget Android -executeMethod ObjecTalk.EditorTools.DevBuild.BuildAndroidDev -logFile <scratch>/android-build.log -accept-apiupdate`
- Wait for `Build Finished, Result: Success.` in the log. Expected APK: `Builds/Android/ObjecTalk-dev.apk` (~200 MB).

- [ ] **Step 2: Install and launch**

```bash
ADB="/c/Users/cadix/AppData/Local/Android/Sdk/platform-tools/adb.exe"
"$ADB" install -r "D:/newest_toritori/ObjecTalk/ObjecTalk-toritori(1)/Builds/Android/ObjecTalk-dev.apk"
"$ADB" logcat -c
"$ADB" shell monkey -p com.DefaultCompany.ObjecTalktoritori1 -c android.intent.category.LAUNCHER 1
```

- [ ] **Step 3: Start a filtered logcat monitor**

Stream `Unity:V` filtered to registration and recognition markers plus crash signatures (`exception`, `AndroidRuntime`, `FATAL`). Watch for: `SentisEmbeddingExtractor: using the ... backend`, `AKAZE local-feature verification is unavailable`, `★ 物体認識: ON`.

- [ ] **Step 4: Manual on-device checklist (hand to the user)**

1. Log in.
2. Enter the register flow. Confirm: the readout starts at `1 / 3 枚目`, the guide frame is centered and upright, the preview is upright (not sideways).
3. Take capture 1 — a dot lights, readout advances to `2 / 3 枚目`, the "角度を少し変えて" hint appears.
4. Take captures 2 and 3 — dots 2 and 3 light, flow advances to placement automatically.
5. Place eyes/mouth, name the object, confirm. Expect `Object '<名前>' saved to DB successfully` in logcat.
6. Point the camera at the object. Expect `★ 物体認識: ON (1) - 物体名: <名前>` in logcat.
7. Confirm any object registered before this change now reports `Loaded N objects, 0 recognizable` (its old samples were discarded by `InvalidateObsolete`).

- [ ] **Step 5: Record the result**

Update the on-device section of the memory file `android-build-notes` (or the project handoff notes) with: device/OS, APK commit, and pass/fail for each checklist item. Do not record transcripts or credentials.

- [ ] **Step 6: Final verification and commit**

Run once more, from `D:\newest_toritori\ObjecTalk`:
```bash
"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode EditMode
"C:\Users\cadix\AppData\Local\Programs\uloop\bin\uloop.exe" run-tests --test-mode PlayMode
git diff --stat
```

Expected: all tests PASS; the only tracked changes are the ones committed in Tasks 1-5. Commit any handoff-notes/doc update:

```bash
git add ObjecTalk-toritori\(1\)/docs
git commit -m "docs: record registration capture UI on-device verification"
```

---

## Self-Review

**Spec coverage:**

- "1 / 3 進捗を常時表示" → Task 5 (`SetRegistrationProgress`, `BeginRegistrationSession`).
- "進捗ドット3個" → Task 5 (`registrationDots`), Task 6 (scene).
- "AIが切り出す領域と一致するガイド枠" → Task 5 (`RegistrationGuideLayout`, `SetGuideFrameActive`, `guideFractionOfPreview` = `DefaultCropFraction`), Task 6 (scene).
- "説明文 + 2枚目以降の追加" → Task 5 (`RegistrationViewPrompt`, `RegistrationAngleHint`).
- "カメラプレビューを直立表示 (`videoRotationAngle` / `videoVerticallyMirrored`)" → Tasks 2, 3.
- "embedding/ローカル特徴の入力を中央75%の正方形に" → Tasks 1, 4.
- "プレビューとAI入力が向き・切り出しで一致" → Task 3 (`ActiveCameraFrame` shared by preview + recognition + registration + placement snapshot).
- "前処理変更で既存サンプルを全消去" → Task 4 Step 4 (version bump) + Step 7 (existing `InvalidateObsolete`; object names/images/placement survive because it only nulls feature blobs).
- "認識・登録が従来どおり動作" → Task 3 Step 6, Task 5 Step 7, Task 7 checklist.
- "`IFramePreprocessor` 署名不変" → Task 4 uses a constructor argument only.
- "シェーダ stripping 対策" → Task 3 uses `Shader.Find("Unlit/Texture")` (built-in, always available); if a stripped-shader issue appears on device in Task 7, switch to `Graphics.Blit` default material or register the shader in Always Included Shaders.

**Placeholder scan:** No "TBD"/"handle edge cases"/"similar to Task N". Each code step has the code. Task 6 scene steps are manual-in-Editor by nature and name the exact GameObjects, parents, and components.

**Type consistency:** `ActiveCameraFrame()` (Task 3) is `Texture`-returning and used by Tasks 3 and 5. `FrameOrientation.FromWebCam` / `OrientedSize` / `SourceUVsForOrientedCorners` / `IsIdentity` (Task 2) match their uses in Task 3. `ImageGeometry.CenterCrop(w,h,cropFraction)` / `NormalizedCenterCrop(w,h,cropFraction)` / `DefaultCropFraction` (Task 1) match Tasks 4 and 5. `RecognitionMessages.RegistrationViewPrompt(int,int)` / `RegistrationAngleHint` and `RegistrationGuideLayout.FrameSize(Vector2,float)` (Task 5) match their controller uses. Preprocessing version string `"rgb01-imagenet-mean-std-crop75-upright-v2"` is identical in the Global Constraints, Task 4 Step 1, and Task 4 Step 4.
