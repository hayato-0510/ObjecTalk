# Android Object Recognition Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the synchronous Barracuda recognition path with a tested Sentis-based, three-view object recognition pipeline optimized for Android 10+.

**Architecture:** Split pure scoring and scheduling logic from Unity image/model adapters, persist versioned samples separately from object metadata, and let one orchestrator own asynchronous recognition and resource lifecycle. Sentis produces normalized embeddings; cosine screening selects one object and bounded AKAZE verification confirms it.

**Tech Stack:** Unity 6000.1.7f1, C#, Unity Test Framework/NUnit, Unity Sentis 2.4.x (`com.unity.ai.inference`), OpenCvSharp, sqlite-net, Android ARM64/IL2CPP.

**Spec:** `docs/superpowers/specs/2026-09-02-android-object-recognition-optimization-design.md`

## Global Constraints

- Minimum Android API level is 29; build ARM64 only with IL2CPP.
- Preserve users, objects, tags, face placement, and profile data; only obsolete recognition features may be removed.
- Exactly three valid recognition samples are required for each newly registered object.
- Model input is 224 x 224 RGB and output is a finite, L2-normalized embedding.
- AKAZE receives an explicit CPU-readable image no larger than 320 x 320; never cast `WebCamTexture` to `Texture2D`.
- No overlapping inference requests and no hard-coded matching thresholds outside the serialized recognition settings.
- Use TDD for every production behavior: add one failing test, observe the expected failure, implement minimally, then rerun all relevant tests.
- Do not overwrite or commit unrelated existing worktree changes.

---

## File structure

- `Assets/Scripts/Domain/Recognition/RecognitionTypes.cs`: sample, candidate, settings, and state value types.
- `Assets/Scripts/Domain/Recognition/EmbeddingMath.cs`: normalization, cosine similarity, and three-view aggregation.
- `Assets/Scripts/Domain/Recognition/TemporalRecognitionFilter.cs`: recognition enter/exit hysteresis.
- `Assets/Scripts/Domain/Recognition/AdaptiveRecognitionScheduler.cs`: non-overlap and adaptive cadence policy.
- `Assets/Scripts/Infrastructure/AI/SentisEmbeddingExtractor.cs`: Sentis worker selection, incremental inference, and output ownership.
- `Assets/Scripts/Infrastructure/Vision/CameraFramePreprocessor.cs`: center crop, GPU resize, CPU verification-buffer copy.
- `Assets/Scripts/Infrastructure/Vision/AkazeVerifier.cs`: bounded local-feature extraction and verification.
- `Assets/Scripts/Infrastructure/Database/RecognitionSampleRepository.cs`: versioned sample schema, migration, serialization validation.
- `Assets/Scripts/Core/Services/ObjectRecognitionService.cs`: recognition orchestration only.
- `Assets/Scripts/Presentation/Controllers/EmbeddingObjectMatcher.cs`: UI flow and lifecycle adapter; no inference internals.
- `Assets/Tests/EditMode/*`: deterministic domain, persistence, configuration, and model-contract tests.
- `Assets/Tests/PlayMode/*`: lifecycle, registration, preprocessing, and resource-release integration tests.
- `Tools/export_mobilenet_v3_embedding.py`: reproducible FP16 ONNX exporter with embedded preprocessing metadata.
- `docs/android-recognition-device-test.md`: device profiling and accuracy checklist.

### Task 1: Establish test assemblies and recognition value types

**Files:**
- Create: `Assets/Scripts/ObjecTalk.Runtime.asmdef`
- Create: `Assets/Tests/EditMode/ObjecTalk.EditModeTests.asmdef`
- Create: `Assets/Tests/EditMode/EmbeddingMathTests.cs`
- Create: `Assets/Scripts/Domain/Recognition/RecognitionTypes.cs`
- Create: `Assets/Scripts/Domain/Recognition/EmbeddingMath.cs`

**Interfaces:**
- Produces: `RecognitionSample`, `RecognitionCandidate`, `RecognitionSettings`, `EmbeddingMath.NormalizeInPlace(float[])`, `EmbeddingMath.Cosine(float[], float[])`, and `EmbeddingMath.FindBest(IReadOnlyList<RecognitionSample>, float[], float threshold, float ambiguityMargin)`.

- [ ] **Step 1: Add the runtime and Edit Mode asmdefs** with references to Unity Test Framework, OpenCvSharp, SQLiteNet, and the inference package where required; keep pure domain files free of Unity inference dependencies.
- [ ] **Step 2: Write failing tests** proving zero vectors are rejected, finite vectors normalize to unit length, unequal lengths return no score, three views belonging to one object aggregate by maximum score, and a best candidate is rejected when its lead over second place is below `ambiguityMargin`.

```csharp
[Test]
public void FindBest_RejectsAmbiguousObjects()
{
    var samples = Samples(("a", new[] { 1f, 0f }), ("b", new[] { .99f, .01f }));
    Assert.That(EmbeddingMath.FindBest(samples, new[] { 1f, 0f }, .8f, .02f).HasValue, Is.False);
}
```

- [ ] **Step 3: Run Edit Mode tests and verify RED.**

Run Unity in batch mode with `-runTests -testPlatform EditMode -testFilter ObjecTalk.Tests.EmbeddingMathTests -testResults Logs/editmode-task1.xml -quit`. Expected: compile failure because the recognition types and math API do not exist.

- [ ] **Step 4: Implement the minimal types and math**, rejecting null, empty, non-finite, zero-norm, or dimension-mismatched vectors without allocating inside cosine loops.
- [ ] **Step 5: Rerun the filtered test and full Edit Mode suite.** Expected: all pass with no compiler errors.
- [ ] **Step 6: Commit only Task 1 files** with `test: establish recognition domain primitives`.

### Task 2: Add temporal stability and adaptive scheduling

**Files:**
- Create: `Assets/Tests/EditMode/TemporalRecognitionFilterTests.cs`
- Create: `Assets/Tests/EditMode/AdaptiveRecognitionSchedulerTests.cs`
- Create: `Assets/Scripts/Domain/Recognition/TemporalRecognitionFilter.cs`
- Create: `Assets/Scripts/Domain/Recognition/AdaptiveRecognitionScheduler.cs`

**Interfaces:**
- Consumes: `RecognitionCandidate` and `RecognitionSettings` from Task 1.
- Produces: `TemporalRecognitionFilter.Observe(RecognitionCandidate? candidate, double nowSeconds)` and `AdaptiveRecognitionScheduler.TryBegin(double nowSeconds)`, `Complete(double nowSeconds, double elapsedSeconds)`, `Cancel()`.

- [ ] **Step 1: Write failing temporal-filter tests** showing that recognition starts only after the configured consecutive-hit count, switches identity only after new repeated evidence, and exits only after the configured loss grace period.
- [ ] **Step 2: Write failing scheduler tests** showing that work cannot overlap, fast work approaches the configured minimum interval, slow work backs off toward the maximum, and cancel clears pending state without allowing an old completion to mutate the new generation.
- [ ] **Step 3: Run both fixtures and verify RED** because the two policy classes are absent.
- [ ] **Step 4: Implement both classes as pure C#**, inject time through method arguments, clamp intervals to settings, and use a monotonically increasing generation integer for cancellation.
- [ ] **Step 5: Run both fixtures and the full Edit Mode suite.** Expected: all pass.
- [ ] **Step 6: Commit Task 2 files** with `feat: stabilize and pace recognition results`.

### Task 3: Introduce versioned three-view persistence and migration

**Files:**
- Modify: `Assets/Scripts/Database/DataManager.cs`
- Create: `Assets/Scripts/Infrastructure/Database/RecognitionSampleRepository.cs`
- Create: `Assets/Tests/EditMode/RecognitionSampleRepositoryTests.cs`

**Interfaces:**
- Produces database table `recognition_samples` keyed by `(object_id, sample_index)` with `model_version`, embedding dimension/blob, keypoint blob, descriptor rows/columns/type/blob, image width, and image height.
- Produces: `SaveSamples(string objectId, IReadOnlyList<RecognitionSampleRecord>)`, `LoadValidSamples(string userId, RecognitionDataVersion expected)`, `InvalidateObsolete(RecognitionDataVersion expected)`, and `DeleteSamples(string objectId)`.

- [ ] **Step 1: Refactor `DatabaseManager` construction behind an internal path-taking constructor** and write a failing test using a temporary SQLite file; the public constructor must continue using `Application.persistentDataPath`.
- [ ] **Step 2: Write failing migration tests** proving old feature blobs are cleared while `users`, `objects`, tags, images, eye/mouth data, and prompts remain unchanged.
- [ ] **Step 3: Write failing validation tests** for wrong model version, invalid embedding byte length, excessive dimensions, invalid OpenCV metadata, corrupt descriptor length, and a valid exactly-three-sample round trip.
- [ ] **Step 4: Run the repository fixture and verify RED** for the missing table/repository behavior.
- [ ] **Step 5: Implement the new table and transactional repository.** Delete sample rows from `DeleteObject`; make migration idempotent; cap embedding dimension at the exported model dimension and verification image dimensions at 320.
- [ ] **Step 6: Run repository and full Edit Mode tests.** Expected: all pass and temporary databases are removed in teardown.
- [ ] **Step 7: Commit Task 3 files** with `feat: persist versioned recognition samples`.

### Task 4: Export and validate the FP16 embedding model

**Files:**
- Modify: `Packages/manifest.json`
- Modify: `Packages/packages-lock.json` through Unity Package Manager resolution
- Create: `Tools/export_mobilenet_v3_embedding.py`
- Create generated asset: `Assets/Models/mobilenetv3-small-embedding-fp16.onnx`
- Create generated meta: `Assets/Models/mobilenetv3-small-embedding-fp16.onnx.meta`
- Create: `Assets/Tests/EditMode/EmbeddingModelContractTests.cs`
- Remove after migration is green: `Assets/Models/mobilenetv2-12.onnx` and its meta

**Interfaces:**
- Produces ONNX input `image` shaped `[1,3,224,224]`, output `embedding`, model version `mobilenetv3-small-imagenet-fp16-v1`, and preprocessing constants RGB `[0,1]`, mean `[0.485,0.456,0.406]`, standard deviation `[0.229,0.224,0.225]`.

- [ ] **Step 1: Replace `com.unity.barracuda` with `com.unity.ai.inference` 2.4.x** and let Unity resolve the lockfile; do not edit unrelated package entries.
- [ ] **Step 2: Add an exporter script** that loads TorchVision MobileNetV3-Small ImageNet weights, removes the classifier, applies global average pooling and L2 normalization, exports opset 17 with fixed names/shapes, converts weights to FP16, and writes model/version/preprocessing metadata. Pin exporter dependency versions in the script header and record the upstream weights/license URL.
- [ ] **Step 3: Write a failing Editor model-contract test** that loads the imported model and asserts one 224-square RGB input, one finite embedding output, expected output dimension, expected names, and model metadata version.
- [ ] **Step 4: Run the contract test and verify RED** before the new ONNX asset exists.
- [ ] **Step 5: Generate/import the model and implement only the import metadata needed for the contract.** Keep the generated ONNX below 15 MB and record its SHA-256 beside the exporter script.
- [ ] **Step 6: Run the contract test twice**, once in Editor CPU mode and once using GPU compute when available. Expected: unit-length outputs within tolerance and matching dimensions.
- [ ] **Step 7: Commit Task 4 files** with `feat: add Sentis embedding model`.

### Task 5: Implement reusable preprocessing and bounded AKAZE verification

**Files:**
- Create: `Assets/Tests/EditMode/ImageGeometryTests.cs`
- Create: `Assets/Tests/PlayMode/CameraFramePreprocessorTests.cs`
- Create: `Assets/Tests/PlayMode/ObjecTalk.PlayModeTests.asmdef`
- Create: `Assets/Scripts/Infrastructure/Vision/CameraFramePreprocessor.cs`
- Replace: `Assets/Scripts/Infrastructure/Vision/FeatureMatcher.cs` with `Assets/Scripts/Infrastructure/Vision/AkazeVerifier.cs`
- Modify: `Assets/Scripts/Domain/Interfaces/IFeatureMatcher.cs`

**Interfaces:**
- Produces: `ImageGeometry.CenterCrop(int sourceWidth, int sourceHeight)`; `CameraFramePreprocessor.PrepareEmbedding(Texture source)`; `CopyVerificationFrame(Texture source)` returning a reusable readable `Texture2D`; `IAkazeVerifier.Extract(Texture2D)` and `Verify(Texture2D, LocalFeatureSample, int minimumMatches)`.

- [ ] **Step 1: Write failing crop tests** for landscape, portrait, square, odd dimensions, and invalid dimensions.
- [ ] **Step 2: Write failing Play Mode tests** using generated color-quadrant textures to prove no stretch, RGB orientation, output sizes 224 and at most 320, stable buffer identity across frames, and disposal idempotence.
- [ ] **Step 3: Add a source scan assertion** that production recognition code contains no `as Texture2D` conversion of a camera texture.
- [ ] **Step 4: Run tests and verify RED** for missing preprocessing APIs.
- [ ] **Step 5: Implement GPU center-crop/resize**, explicit GPU-to-CPU readback only for the reusable verification texture, and AKAZE extraction/verification against that texture. Dispose all temporary Mats in `finally` blocks.
- [ ] **Step 6: Run the new tests and full suites.** Use the Unity Profiler test assertion to verify the second through tenth preprocessing calls do not create new managed image buffers.
- [ ] **Step 7: Commit Task 5 files** with `fix: bound and reuse camera preprocessing`.

### Task 6: Implement incremental Sentis inference with backend fallback

**Files:**
- Replace: `Assets/Scripts/Infrastructure/AI/FeatureExtractor.cs` with `Assets/Scripts/Infrastructure/AI/SentisEmbeddingExtractor.cs`
- Modify: `Assets/Scripts/Domain/Interfaces/IFeatureExtractor.cs`
- Create: `Assets/Tests/EditMode/BackendSelectionPolicyTests.cs`
- Create: `Assets/Tests/PlayMode/SentisEmbeddingExtractorTests.cs`

**Interfaces:**
- Produces: `IEmbeddingExtractor.Initialize(ModelAsset model, EmbeddingModelSpec spec)`, `Begin(Texture input, int generation)`, `Step()` returning pending/completed/failed, `CopyNormalizedResult(Span<float>)`, `Cancel(int generation)`, and `Dispose()`.

- [ ] **Step 1: Write failing policy tests** proving GPUCompute is selected only when compute shaders are supported, initialization failure allows exactly one CPU attempt, and dual failure returns a disabled state with a Japanese error key.
- [ ] **Step 2: Write failing Play Mode tests** proving a second `Begin` is rejected while busy, execution advances incrementally, stale generations publish no result, output is finite/unit length, output arrays are reused, and dispose is idempotent.
- [ ] **Step 3: Run tests and verify RED** because the Sentis extractor does not exist.
- [ ] **Step 4: Implement the extractor** with Sentis `Worker`, the supported incremental scheduling API for package 2.4.x, GPU/CPU backend creation, reusable tensors, and explicit tensor readback only on completion.
- [ ] **Step 5: Run CPU tests, then GPU-capable Play Mode tests.** Expected: all pass; log the selected backend once, not per frame.
- [ ] **Step 6: Commit Task 6 files** with `feat: run embeddings incrementally with fallback`.

### Task 7: Rebuild the recognition service around the new components

**Files:**
- Modify: `Assets/Scripts/Core/Services/ObjectRecognitionService.cs`
- Modify: `Assets/Scripts/Domain/Interfaces/IObjectRecognitionService.cs`
- Modify: `Assets/Scripts/Domain/Models/RegisteredObject.cs`
- Create: `Assets/Tests/EditMode/ObjectRecognitionServiceTests.cs`
- Create: `Assets/Tests/PlayMode/ObjectRecognitionLifecycleTests.cs`

**Interfaces:**
- Consumes Tasks 1-6 abstractions.
- Produces: `StartRecognition(Texture frame, double now)`, `Tick(double now)`, `BeginRegistration(string objectId)`, `CaptureRegistrationView(Texture frame)`, `CompleteRegistration()`, `Pause()`, `Resume()`, and state/result events.

- [ ] **Step 1: Write failing service tests** proving settings flow unchanged into scoring/AKAZE, only the best neural candidate reaches AKAZE, ambiguous candidates do not, inference never overlaps, and recognition state events publish only stabilized changes.
- [ ] **Step 2: Write failing registration tests** proving zero, one, or two valid captures cannot commit; invalid/featureless captures request retry; exactly three valid captures commit atomically.
- [ ] **Step 3: Write failing lifecycle tests** proving pause cancels the active generation, stops accepting frames, and releases owned buffers; resume creates a clean generation.
- [ ] **Step 4: Run tests and verify RED** against the old synchronous API.
- [ ] **Step 5: Implement the orchestrator** without direct UI, SQLite, `WebCamTexture`, or scene dependencies. Use injected interfaces and keep all thresholds in `RecognitionSettings`.
- [ ] **Step 6: Run service, lifecycle, and full test suites.** Expected: all pass with no hard-coded `0.8f` in production matching calls.
- [ ] **Step 7: Commit Task 7 files** with `refactor: orchestrate asynchronous object recognition`.

### Task 8: Update the three-view UI flow and application lifecycle

**Files:**
- Modify: `Assets/Scripts/Presentation/Controllers/EmbeddingObjectMatcher.cs`
- Modify relevant scene/prefab containing `EmbeddingObjectMatcher` after locating it by script GUID
- Create: `Assets/Tests/PlayMode/EmbeddingObjectMatcherFlowTests.cs`

**Interfaces:**
- Consumes `IObjectRecognitionService` from Task 7 and `RecognitionSampleRepository` from Task 3.
- Produces user-visible Japanese states for captures `1/3`, `2/3`, `3/3`, retry, re-registration required, camera permission denied, model unavailable, and recognition unavailable.

- [ ] **Step 1: Write failing Play Mode flow tests** for three captures, retry without increment, cancel cleanup, successful transactional save, obsolete-feature notice, and recognition result UI updates.
- [ ] **Step 2: Write failing lifecycle tests** for panel disable, `OnApplicationPause(true/false)`, permission denial, permission restoration, and object destruction.
- [ ] **Step 3: Run flow tests and verify RED** against the one-capture synchronous controller.
- [ ] **Step 4: Reduce `EmbeddingObjectMatcher` to presentation responsibilities**, wire serialized `RecognitionSettings`, remove Barracuda imports and duplicate thresholds, and invoke service ticks without blocking `Update()`.
- [ ] **Step 5: Update the owning scene/prefab serialized fields** without replacing unrelated scene content. Verify all object references in the Inspector.
- [ ] **Step 6: Run flow and full Play Mode suites.** Expected: all pass and no missing-script/missing-reference errors.
- [ ] **Step 7: Commit only the controller, its tests, and specifically changed scene/prefab** with `feat: add three-view registration flow`.

### Task 9: Apply Android build settings and verify stripping

**Files:**
- Modify: `ProjectSettings/ProjectSettings.asset` through Unity Player Settings where possible
- Create if required: `Assets/link.xml`
- Create: `Assets/Tests/EditMode/AndroidRecognitionBuildSettingsTests.cs`

**Interfaces:**
- Produces Android minimum API 29, ARM64-only, IL2CPP, Vulkan-first with OpenGL ES 3 fallback, and stripping configuration that retains reflection-created SQLite/Sentis/OpenCV types.

- [ ] **Step 1: Write a failing Editor test** reading PlayerSettings and asserting API 29 minimum, ARM64 only, IL2CPP, no API 28 pin, graphics API order, multithreaded rendering, and non-disabled managed stripping.
- [ ] **Step 2: Run the test and verify RED** against current API 23/default backend settings.
- [ ] **Step 3: Apply settings via Unity Editor APIs** and add the narrowest `link.xml` only for types shown missing in a development IL2CPP build.
- [ ] **Step 4: Run the settings test and make an Android development IL2CPP build.** Expected: build succeeds with no missing Sentis kernels, SQLite mappings, or OpenCV native libraries.
- [ ] **Step 5: Install and smoke-test the build** on one Android 10+ ARM64 device: permission denial/retry, registration, recognition, pause/resume, and deletion.
- [ ] **Step 6: Commit Task 9 files** with `build: target Android 10 ARM64 recognition`.

### Task 10: Profile, calibrate, document, and remove legacy code

**Files:**
- Create: `Assets/Scripts/Infrastructure/Diagnostics/RecognitionMetrics.cs`
- Create: `Assets/Tests/EditMode/RecognitionMetricsTests.cs`
- Create: `docs/android-recognition-device-test.md`
- Remove: obsolete Barracuda interfaces/implementations and old recognition fields after reference scan
- Modify: `README.md`

**Interfaces:**
- Produces a bounded in-memory metrics snapshot containing backend, cycle count, median and p95 cycle duration, rejected/verified counts, and allocation/memory observations without per-frame logging.

- [ ] **Step 1: Write failing metrics tests** for bounded sample storage and correct median/p95 calculations over deterministic inputs.
- [ ] **Step 2: Run metrics tests and verify RED**, then implement the bounded collector and rerun GREEN.
- [ ] **Step 3: Add the device checklist** covering hardware/OS/GPU, backend selected, 30-minute run, median/p95 cycle time, main-thread frame time, GC Alloc, memory growth, temperature/throttling observation, and a held-out accuracy matrix varying object, angle, distance, lighting, and background.
- [ ] **Step 4: Run `rg` reference scans** for `Unity.Barracuda`, `NNModel`, `WorkerFactory`, `mobilenetv2-12`, direct camera-to-Texture2D casts, and hard-coded recognition thresholds. Remove only now-unreferenced legacy assets/code.
- [ ] **Step 5: Run all Edit Mode and Play Mode tests plus an Android IL2CPP build.** Expected: zero test failures, zero compile errors, and no missing native/plugin dependencies.
- [ ] **Step 6: Profile the representative Android device.** Accept only if median recognition cycle is at most 100 ms, UI has no visible inference stall, steady-state recognition introduces no recurring managed image/tensor allocations, and the 30-minute run has no crash or material progressive slowdown. Record actual results; do not invent unmeasured values.
- [ ] **Step 7: Calibrate serialized neural threshold, ambiguity margin, AKAZE minimum matches, enter count, and exit grace from the held-out set**, then rerun the accuracy and 30-minute tests if any performance-affecting value changes.
- [ ] **Step 8: Update README setup/model-regeneration instructions and commit** with `docs: record Android recognition verification`.

## Final verification gate

- [ ] Run `git diff --check` and inspect `git status --short`; confirm unrelated pre-existing changes are neither staged nor modified by this work.
- [ ] Run the complete Unity Edit Mode and Play Mode suites and retain XML results under `Logs/`.
- [ ] Produce a clean Android ARM64 IL2CPP development build from the final commit.
- [ ] Confirm the model SHA-256, package lock, schema/model version, and preprocessing constants agree.
- [ ] Confirm the device checklist contains measured results for at least one Android 10+ / 4 GB-class device and clearly marks any untested device classes.
