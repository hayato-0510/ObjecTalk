# Face Overlay — Object Tracking Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development or superpowers:executing-plans. Needs on-device iteration; every task ends with a look on the SC-03L.

**Goal:** Make the recognition face overlay (eyes + mouth) sit on the *actual object in the camera frame* and follow it as the object or camera moves, instead of a fixed spot on the preview.

**Status when written (2026-09-08):** `FaceOverlayAnimator` already flaps the mouth to the voice and blinks (commits `6633ee9` / `a580618`), and `EmbeddingObjectMatcher.ShowFaceOverlay` places the eye/mouth images at `obj.RelativeEyePosition` / `obj.RelativeMouthPosition` — the position the user set *during registration*, mapped onto the whole preview rect. It does not know where the object is *now*, so the face floats. This is the remaining "違和感". Deferred past the open-campus exhibition (the staffed booth + push-to-talk make it a nice-to-have).

**Spec:** `docs/superpowers/specs/2026-09-04-object-aware-realtime-voice-conversation-design.md` (conversation) and the recognition design doc.

---

## Why this is not trivial

The recognition pipeline is a whole-frame **embedding classifier** (`ObjectRecognitionService` → `RecognitionTickResult` with `Status` / `ObjectId` / `Score`). There is **no localisation** — no bounding box, no keypoints. `FaceOverlayLayout.AnchoredPosition` just maps the registered relative position onto the preview rect. To place the face on the object we need to know the object's pixel region every frame.

AKAZE local-feature verification *could* give a homography (object pose) but it is disabled on device — `Assets/OpenCV+Unity/Plugins/Android/` has no arm64 `libOpenCvSharpExtern.so`, so `OpenCvSharp.NativeMethods` throws and the app falls back to `NullLocalFeatureVerifier` (see the `android-build-notes` memory).

## Options

| # | Approach | Effort | Notes |
|---|---|---|---|
| A | **Restore arm64 OpenCV + use AKAZE homography** | Medium | Update the OpenCV+Unity asset to an arm64 build (the asset's own "option A", noted in android-build-notes). Then `LocalFeatureVerifier.Verify` already computes matches; extend it to return a homography → project the registered eye/mouth positions through it → screen-space overlay. Recognition already runs AKAZE for the *one* candidate that clears the neural threshold, so the per-frame cost is bounded. Best fit — reuses existing machinery. |
| B | **Add a lightweight object detector** (bounding box) | High | e.g. a small SSD/YOLO-tiny in Unity Sentis, run every N frames, place the face inside the box (still need in-box eye/mouth offsets from registration). New model to source, export, validate, and fit in the ~15 MB budget; another inference per tick. |
| C | **Detect once, then optical-flow / KLT track** | High | One detection (A or B) to seed, then track the region frame-to-frame with feature flow. Lowest steady-state cost, most code (tracker lifecycle, re-seed on loss). OpenCV again → same arm64 blocker as A. |
| D | **Ship without tracking**; tune the fixed placement | None | Keep the current behaviour; make the fixed spot look as good as possible (crop alignment, `overlayEyeFraction` / `overlayMouthFraction`). The exhibition build. |

## Recommended

**A**, gated on restoring arm64 OpenCV — it is the smallest addition that actually tracks, and the AKAZE path already exists. If the arm64 OpenCV update turns out to be blocked or heavy, fall back to **B** with a tiny Sentis detector. Do **not** do **C** first.

## Tasks (Option A)

### Task 1 — Restore arm64 OpenCV, confirm AKAZE runs on device
- [ ] Update `Assets/OpenCV+Unity` to a build that ships `arm64-v8a/libOpenCvSharpExtern.so` (Asset Store update, or add the .so).
- [ ] Remove / keep-as-safety the `NullLocalFeatureVerifier` fallback in `EmbeddingObjectMatcher.BuildLocalFeatureVerifier` — it should no longer be hit.
- [ ] **Device:** register an object, confirm logcat shows AKAZE verifying (no `TypeInitializationException`), recognition still fires.

### Task 2 — Homography from the local-feature match
- [ ] Extend `ILocalFeatureVerifier` / `LocalFeatureVerifier` to return a match result that carries the estimated homography (or a null one when the match is too weak), not just a bool. Keep the bool path for the recognition gate.
- [ ] EditMode: a pure test that, given synthetic correspondences (translation + scale), the returned transform maps registered points to the expected screen points; a weak/degenerate match returns "no transform".

### Task 3 — Project the registered eye/mouth positions
- [ ] `EmbeddingObjectMatcher.ShowFaceOverlay`: when a homography is available, map `RelativeEyePosition` / `RelativeMouthPosition` from registration-image space through it into the current preview, and drive the overlay `RectTransform.anchoredPosition` (and optionally rotation/scale) from that instead of `FaceOverlayLayout.AnchoredPosition`.
- [ ] Fall back to the current fixed placement when there is no homography (weak match, or AKAZE unavailable).
- [ ] Smooth the projected position (the same exponential smoothing `FaceOverlayAnimator` uses) so it does not jitter with per-frame match noise.
- [ ] EditMode: `FaceOverlayLayout` (or a new helper) gets a "project through transform" function, unit-tested engine-free.

### Task 4 — Device tuning + fallback behaviour
- [ ] **Device:** face should stick to the object through slow pan / rotate / distance change; on a lost match it should fade or snap back to the fixed spot rather than fly off.
- [ ] Decide the loss behaviour (hold last position briefly, then fade) and the max per-frame move (clamp so a bad match cannot teleport the face).
- [ ] Re-check `FaceOverlayAnimator` still rides correctly on the now-moving `RectTransform` (it only touches `localScale`, so it should).

### Task 5 — Verify + document
- [ ] `uloop compile` clean; EditMode + PlayMode green.
- [ ] Update the recognition design doc / `objectdetectionscene-quirks` memory.

## Open questions

- Is arm64 OpenCV worth carrying (build size, licensing of the asset) purely for this? If recognition never needs AKAZE for accuracy on the real objects, Option B keeps OpenCV out.
- Registration currently stores eye/mouth as a *relative position on the crop*. For projection we may also need the registration crop's own geometry — check `RegisteredObject` / the DB schema has enough.
