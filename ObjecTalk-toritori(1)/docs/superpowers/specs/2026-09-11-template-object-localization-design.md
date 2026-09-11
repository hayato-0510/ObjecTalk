# Template Object Localization — Design

**Goal:** locate a recognized object inside the live camera frame on the device, so the face
overlay can follow it, and use the same measurement to confirm the object is really there.

**Supersedes the recommendation in** `docs/superpowers/plans/2026-09-08-face-overlay-object-tracking.md`.
That plan chose Option A — restore an arm64 OpenCV and derive a homography from AKAZE. Option A is
dead, and AKAZE turns out to be the wrong tool anyway. Both findings are evidenced below.

---

## Why Option A is dead

Two native libraries want opposite Android ABIs, and Android installs exactly one ABI per APK:

| Dependency | Ships | Needed by |
|---|---|---|
| OpenCV+Unity `libOpenCvSharpExtern.so` | `armeabi-v7a`, `x86` | AKAZE verification |
| `com.unity.webrtc` `libwebrtc.aar` | `arm64-v8a`, `x86_64` | realtime voice conversation |

An ARMv7 player was built and installed on the SC-03L on 2026-09-10: AKAZE came alive and the app
died with `DllNotFoundException: webrtc`. Voice is the feature that has to win, so the player stays
ARM64 and AKAZE stays behind `NullLocalFeatureVerifier`.

The plan's "just update the asset" step has no supply: the bundled OpenCvSharp reports version
`"320"` (OpenCV 3.2.0) and the `.so` files are dated November 2017. No arm64 binary exists for that
vintage; reaching one means migrating to OpenCvSharp4, which is a different project.

## Why AKAZE is the wrong tool regardless

A spike measured three candidate localizers against the five objects actually registered on the
SC-03L, using their real registration photographs. Live frames were synthesized by sliding and
degrading the capture window — real backgrounds and lighting, exact ground truth. Seven appearance
changes per object (closer, further, tilted, dimmed, warm white balance, and two combined
move+zoom+tilt cases), 35 trials:

| Method | Located | Median error | p90 | Worst | Time |
|---|---|---|---|---|---|
| **NCC multi-scale template** | **35/35** | **0.003** | 0.066 | 0.334 | 8.0 ms |
| AKAZE keypoint centroid (today) | 31/35 | 0.096 | 0.203 | 0.443 | 23.9 ms |
| Hue/saturation backprojection | 26/35 | 0.120 | 0.197 | 0.379 | 2.3 ms |

Error is a fraction of the view width. Normalized cross-correlation is invariant to gain and
offset, so lighting changes cost it literally nothing; the multi-scale search absorbs distance
changes. Backprojection is the fastest but loses the object entirely when the white balance shifts,
which rules it out for a hall with unknown lighting.

A resolution sweep settles affordability without OpenCV:

| Search resolution | Multiply-accumulates / tick | Median error | p90 | Located |
|---|---|---|---|---|
| 320x320 | 2,566 M | 0.020 | 0.041 | 35/35 |
| 96x96 | 20.8 M | 0.021 | 0.042 | 35/35 |
| **64x64** | **4.1 M** | **0.012** | 0.048 | 35/35 |
| 48x48 | 1.3 M | 0.021 | 0.046 | 35/35 |

Accuracy is flat from 320 down to 48 while cost falls by three orders of magnitude. At 64x64 the
work is about 4M multiply-accumulates, once per recognition tick (2-5 Hz) — comfortable in plain
C#, with Burst available later if measurement says otherwise.

**Spike limitations, stated plainly.** Live frames were derived from the same photograph, so
texture matches exactly; a genuine re-capture is harder. The one real two-session pair in the data
(the same gum bottle registered twice) gave AKAZE only 2 good matches, far under its threshold of
10 — there was no ground truth to score the other methods against on that pair. Rotation is NCC's
weak axis (12 degrees of tilt costs 0.031 where a pure lighting change costs 0.000). None of this
is settled until it runs on the device.

---

## Design

### 1. The matching lives in Domain

`Assets/Scripts/Domain/Recognition/TemplateMatch.cs` — engine-free, no OpenCV, no UnityEngine, the
same rule as `FeatureCentroid` and `LocalFeatureMatchPolicy`:

```csharp
bool TryLocate(
    ReadOnlySpan<byte> template, int templateSize,
    ReadOnlySpan<byte> query, int querySize,
    ReadOnlySpan<float> scales, float minimumScore,
    out float relativeX, out float relativeY, out float score)
```

Normalized cross-correlation over the scale list, best peak wins, reported as a 0..1
bottom-left-origin position — the convention `FaceOverlayLayout` and `FeatureCentroid` already use.
Below `minimumScore` it returns false and reports no position.

`Assets/Scripts/Domain/Recognition/TemplatePreparation.cs` — raw pixels to a 64x64 grayscale
template, reusing `ImageGeometry`'s centre-crop fraction so a template and a live view are framed
identically.

**Defaults:** 64x64 search, scales 0.6 / 0.8 / 1.0 / 1.25 / 1.6, template = the centre 50% of the
prepared view.

### 2. It plugs in as a verifier

`TemplateVerifier : ILocalFeatureVerifier` in Infrastructure, with no OpenCV dependency. The
interface already expresses "confirm, and say where" — `LocalVerificationResult` carries
`IsVerified` plus an optional position — so no new abstraction is needed.

Three ways a template reaches it:

- `Extract(Texture2D)` — registration. Future registrations persist templates through the existing
  blob channel; no schema change, the bytes simply mean something different for this backend.
- `Restore(blob)` — legacy rows hold AKAZE descriptors, not templates, and cannot produce one.
- `SeedTemplate(objectId, IReadOnlyList<ILocalFeatureSample>)` — **new**, on
  `ObjectRecognitionService`, not on the verifier. The presentation layer decodes
  `ObjectItem.ImageData` (already loaded at startup for every object), calls
  `verifier.Extract` on it, and passes the samples in; the service registers them through the same
  `AddReference` path a restored blob uses, so nothing downstream can tell the two apart. It takes
  a list because Phase 2 stores one template per registration capture. This is the Phase 1 path for
  the objects registered today.

**Telling the two blob dialects apart.** A row written by one backend is meaningless to the other,
and a persisted row carries no marker today. `LocalFeatureBlob` already records
`descriptor_type` / `descriptor_rows` / `descriptor_columns`; `TemplateVerifier` writes a
distinguishable `descriptor_type` and refuses rows that do not carry it, returning a featureless
sample so section 3's path takes over. `AkazeVerifier` gains the mirror check. Without this a
backend switch reads the other's bytes as its own and produces confident nonsense.

### 3. Loading must survive a reference it cannot restore

`ObjectRecognitionService.LoadSamples` currently skips the whole record when
`_verifier.Restore(blob)` yields no features — **including the embedding**. Swapping in a backend
that cannot read legacy blobs would therefore make every already-registered object unrecognizable.

The fix: keep the sample when local features are unavailable, and register no reference for it.
`VerifyLocally` returns "verified, no location" for an object with no references, which is exactly
what `NullLocalFeatureVerifier` produces on the device today. Legacy objects keep being recognized
and pick up a template from the seed path.

This is a correctness fix in its own right and gets its own regression test.

### 4. Thresholds

`Verify` takes `int minimumMatches` today, which means nothing to a correlation score. Passing the
whole `RecognitionSettings` would show a verifier the similarity threshold and the tick cadence,
which are none of its business. Instead a purpose-built value:

```csharp
readonly struct LocalVerificationThresholds
{
    int MinimumMatches { get; }
    float MinimumScore { get; }
}
```

built from `RecognitionSettings` and passed as `Verify(image, reference, in thresholds)`. AKAZE
reads `MinimumMatches`; templates read `MinimumScore`.

The existing intent that thresholds come from settings rather than call sites is preserved, and
`MinimumLocalMatchCountComesFromSettings` still holds.

**Two different jobs, two switches.** A correlation score answers two questions that must not be
conflated:

1. *Do I trust this position?* Below `MinimumTemplateScore` the match is too weak to place the
   face, so `Verify` reports verified with no location and the overlay falls back to the registered
   spot. This is always on — reporting a position you do not believe is worse than reporting none.
2. *Is the object really there?* Rejecting a candidate the embedding accepted changes recognition
   itself. This is governed by a separate `TemplateGatesRecognition` flag on
   `RecognitionSettings`, **default false**.

With the gate off, the template backend confirms every candidate the embedding produced — exactly
what `NullLocalFeatureVerifier` does on the device today — so shipping it cannot make recognition
worse than it currently is. The gate is turned on only after the device pass has measured what
scores real frames produce for true and false candidates.

`MinimumTemplateScore` defaults to **0.35**: the combined move+zoom+tilt cases scored 0.38 and the
weakest true match in the spike sat at 0.31. That number comes from synthetic frames and is a
starting point for the device pass, not a settled value.

### 5. Selection and rollback

`EmbeddingObjectMatcher.BuildLocalFeatureVerifier` gains a serialized backend choice:

```csharp
enum LocalFeatureBackend { Template, Akaze, None }   // Template is the default
```

Templates need no native library, so no try/catch is required; the AKAZE branch keeps its existing
OpenCV fallback. Flipping the field in the Editor runs NCC and AKAZE against the same object and
the same frames, and gives a one-field retreat if the device disagrees with the spike.

**What the retreat does and does not restore.** Switching back to `Akaze` restores AKAZE for every
object registered before the switch, because their rows still hold descriptors. Objects registered
*while* `Template` was active have template rows instead, so AKAZE reads them as featureless and
those objects fall to "verified, no location" — recognized, face at the registered spot. That is
the pre-`ca90759` behaviour, not a breakage, and it heals on re-registration. On the device the
question is moot: AKAZE cannot run there at all.

### 6. Testing

**Domain, where the weight goes.** `TemplateMatchTests`: recovery of a known offset, scale change,
invariance to gain and offset (the property the lighting result rests on), rejection below
threshold, a template larger than the query. `TemplatePreparationTests`: crop geometry, grayscale
conversion, origin convention.

**Core.** A record whose local features fail to restore keeps its embedding and stays recognizable
(section 3's regression). A seeded template yields a position. A score under
`MinimumTemplateScore` reports no location but still confirms the candidate while
`TemplateGatesRecognition` is false, and rejects it once the flag is true — both directions
tested, since the whole point of the flag is that the two behaviours differ.

**Infrastructure, and it *is* testable.** `AkazeVerifier` is untestable because OpenCV+Unity ships
without an assembly definition, which strands anything touching it in `Assembly-CSharp` — a
constraint the EditMode test assembly cannot reference past. `TemplateVerifier` has no OpenCV
dependency, so it gets its own `ObjecTalk.Infrastructure.Vision.Templates` assembly (referencing
`ObjecTalk.Domain.Recognition` and `ObjecTalk.Core.Recognition`, engine references on for
`Texture2D`), the EditMode assembly references it, and the `Texture2D`-to-bytes glue is covered by
real tests against a constructed texture. This is a reason to prefer the new backend beyond the
measurements.

Only `EmbeddingObjectMatcher`'s backend selection stays untested, as the whole class already is.

**Device is the real test.** Build ARM64, install on the SC-03L, point the camera at a registered
object, and watch the face follow it and stay put when the object does.

---

---

## Device result (2026-09-11): the method does not work, and this is why

Measured on the SC-03L against a registered object moved slowly by hand.

**The pipeline is correct.** A template matched against the very picture it was built from scores
**1.000** and lands at **(0.500, 0.500)** — dead centre, exactly right. Crop geometry, grayscale
conversion, the three-way vertical-axis convention, rescaling and the correlation itself are all
sound. No implementation fault survives that number.

**The match does not happen on a re-capture.** Live correlation sat at a median of **0.416**, and
the reported position jumped a median of **0.175** of the frame between consecutive ticks, ranging
over most of the view. A hand-held object cannot move that far in 0.2 seconds, so the peak is not
on the object.

**Raising the threshold cannot rescue it.** Sorting the samples by score, the highest band (0.48+)
jumped a median of 0.320 — no steadier than the lowest band (0.323). This is not a threshold that
needs tuning; it is a match that is not happening.

**Removing background did not help.** The template fraction was cut from 0.50 to 0.32, so the
template carried far less of the surface the object was photographed on:

| | fraction 0.50 | fraction 0.32 |
|---|---|---|
| correlation median | 0.416 | 0.410 |
| correlation max | 0.553 | **0.459** |
| jump median | 0.175 | 0.097 |
| jump max | 0.647 | **0.742** |
| spread x | 0.477 | **0.704** |

The score did not improve and the best peak got *worse* — a tighter template carries less to
identify the object with. The background-contamination hypothesis is falsified.

**Conclusion.** With a self-match of 1.000 and a live match of 0.41, the registration photograph
and the live view of the same object are genuinely only that similar under raw grayscale
correlation. Viewpoint, lighting and background change on a real re-capture defeat the method. The
spike could not see this because its "live" frames were cut from the same photograph — the
limitation recorded above, now settled negatively.

**What ships.** `localFeatureBackend` is set to `None` in `ObjectDetectionScene`: on the device the
face returns to its registered position, which is the behaviour from before this work and is
steadier than a face chasing a wandering peak. Everything else stays — the Domain maths, the
verifier, the tests, and the backend switch — so the Editor can still run `Template` or `Akaze`,
and an arm64 OpenCV or a better localizer would drop into the same seam.

**Worth keeping regardless.** Two real defects were found on the way: `LoadSamples` dropped an
object's embedding when its local features would not restore, which would have unregistered every
object on any backend switch; and template rows could not be persisted at all because
`descriptor_type` is read as an OpenCV Mat type rather than an opaque tag.

---

## Non-goals

No database schema change. No re-registration of existing objects. No change to the embedding
comparison, the ambiguity margin, or the temporal stabilizer. No rotation or scale in the overlay —
tracking stays translation-only, matching the face-tracking work committed in `ca90759`. AKAZE is
not removed.

## Risks

**The spike may be optimistic.** Its live frames share pixels with the template. If the device
disagrees, the backend field retreats to `Akaze` or `None` without rebuilding anything but the
scene reference.

**One template, one viewpoint.** Phase 1 gives each object a single template from its registration
snapshot. If viewpoint change proves to be the limit, Phase 2 stores a template per registration
capture; the interface takes a list of templates from the start so that is an addition, not a
rewrite.

**Verification can reach the critical path, but not yet.** Section 4 keeps
`TemplateGatesRecognition` off by default, so the first version cannot make recognition worse than
today's embedding-only device behaviour. Turning it on is a second, separately verified step
driven by measured scores from real frames — a threshold guessed from synthetic data would be a
good way to break a working exhibition.
