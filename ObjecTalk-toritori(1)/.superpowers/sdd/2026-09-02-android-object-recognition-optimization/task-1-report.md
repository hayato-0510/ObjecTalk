## Task 1 Report

### Implementation summary
- Added a focused runtime assembly at `Assets/Scripts/Domain/Recognition/ObjecTalk.Domain.Recognition.asmdef` with `noEngineReferences: true` so the new recognition domain stays independent from Barracuda, OpenCV, and UI code.
- Added `RecognitionSample`, `RecognitionCandidate`, and `RecognitionSettings` in `Assets/Scripts/Domain/Recognition/RecognitionTypes.cs`.
- Added `EmbeddingMath.NormalizeInPlace(float[])`, `EmbeddingMath.Cosine(float[], float[])`, and `EmbeddingMath.FindBest(IReadOnlyList<RecognitionSample>, float[], float, float)` in `Assets/Scripts/Domain/Recognition/EmbeddingMath.cs`.
- Added an Edit Mode test assembly and `EmbeddingMathTests` covering zero-vector rejection, normalization to unit length, unequal-length cosine rejection, max-score aggregation across three views, and ambiguity-margin rejection.

### Tests and exact results
- Unity filtered Edit Mode RED attempt 1:
  - Command reached Unity startup but failed before test discovery.
  - Exact log lines from `.superpowers/sdd/2026-09-02-android-object-recognition-optimization/task-1-red.log`:
    - `[Licensing::Module] Timed-out after 60.01s, waiting for channel: "LicenseClient-hayato"`
    - `IPC channel to LicensingClient doesn't exist; aborting`
    - `Exiting without the bug reporter. Application will terminate with return code 199`
- Unity filtered Edit Mode RED retry:
  - Same pre-test licensing failure in `.superpowers/sdd/2026-09-02-android-object-recognition-optimization/task-1-red-retry.log`.
  - Exact repeated lines:
    - `[Licensing::Module] Timed-out after 60.01s, waiting for channel: "LicenseClient-hayato"`
    - `IPC channel to LicensingClient doesn't exist; aborting`
    - `Exiting without the bug reporter. Application will terminate with return code 199`
- Static compile-oriented validation:
  - `dotnet build '.superpowers\sdd\2026-09-02-android-object-recognition-optimization\task-1-static-compile.csproj'`
  - Result: `Build succeeded. 0 Warning(s) 0 Error(s).`

### RED/GREEN evidence
- RED:
  - Test-first file added before production code: `Assets/Tests/EditMode/EmbeddingMathTests.cs`.
  - Unity RED execution was attempted twice, but both runs were blocked before compile/test execution by LicensingClient IPC timeout/exit 199.
- GREEN:
  - Production types/math were implemented after the blocked RED attempt.
  - Static build validation passed with zero warnings and zero errors.
- Unresolved:
  - No Unity Test Runner GREEN evidence was obtainable in this environment because Unity never reached test discovery.

### Files changed
- `Assets/Scripts/Domain/Recognition/ObjecTalk.Domain.Recognition.asmdef`
- `Assets/Scripts/Domain/Recognition/ObjecTalk.Domain.Recognition.asmdef.meta`
- `Assets/Scripts/Domain/Recognition/RecognitionTypes.cs`
- `Assets/Scripts/Domain/Recognition/RecognitionTypes.cs.meta`
- `Assets/Scripts/Domain/Recognition/EmbeddingMath.cs`
- `Assets/Scripts/Domain/Recognition/EmbeddingMath.cs.meta`
- `Assets/Tests/EditMode/ObjecTalk.EditModeTests.asmdef`
- `Assets/Tests/EditMode/ObjecTalk.EditModeTests.asmdef.meta`
- `Assets/Tests/EditMode/EmbeddingMathTests.cs`
- `Assets/Tests/EditMode/EmbeddingMathTests.cs.meta`

### Self-review
- The runtime assembly is isolated and has no Unity engine references.
- The math path rejects null, empty, non-finite, zero-norm, and dimension-mismatched vectors without allocating inside the cosine loop.
- `FindBest` aggregates per object label by maximum cosine score and rejects ambiguous winners when the lead over second place is below `ambiguityMargin`.
- `RecognitionSettings.RequiredSamplesPerObject` captures the “exactly three samples” policy for later higher-level enforcement, while thresholds remain explicit inputs rather than hard-coded constants in the matcher.
- I intentionally did not stage folder `.meta` files or the report because the controller narrowed the commit to exactly 10 Task 1 files.

### Concerns
- Unity Edit Mode execution is still blocked by LicensingClient IPC timeout/exit 199, so actual Unity RED/GREEN remains unverified.
- Because the commit was constrained to exactly 10 files, the new `Assets/Tests.meta`, `Assets/Tests/EditMode.meta`, and `Assets/Scripts/Domain/Recognition.meta` folder metadata remain outside the commit and may need follow-up if the controller wants Unity asset metadata committed as well.
- The static compile helper project under `.superpowers/sdd/2026-09-02-android-object-recognition-optimization/task-1-static-compile.csproj` was used only for validation and was not part of the task commit.

## Fix round 1

### Changes
- `RecognitionSample` now clones and normalizes its input embedding before storing it, so recognition storage owns an internal normalized copy and never mutates caller-owned arrays.
- `EmbeddingMath.FindBest` now enforces that the query embedding is already finite, non-zero, and L2-normalized; non-unit queries raise `ArgumentException` instead of being silently scored.
- `EmbeddingMath.Cosine` now returns a score only when both vectors are finite, equal-length, and unit-normalized, while still avoiding allocations inside the cosine loop.
- Added the three Unity folder metadata files to source control: `Assets/Tests.meta`, `Assets/Tests/EditMode.meta`, and `Assets/Scripts/Domain/Recognition.meta`.

### API semantics
- Storage boundary: `RecognitionSample(label, embedding)` accepts any finite, non-zero embedding dimension and stores a normalized private copy.
- Scoring boundary: `FindBest(samples, query, threshold, ambiguityMargin)` requires `query` to already be L2-normalized and throws if that contract is violated.
- Caller ownership: sample construction does not mutate the input array; explicit mutation is only performed by `NormalizeInPlace`.

### RED evidence
- Because Unity remains blocked by licensing, the achievable red/green loop for this fix round used a local NUnit-backed runner at `.superpowers/sdd/2026-09-02-android-object-recognition-optimization/task-1-test-runner.csproj`.
- RED command:
  - `dotnet run --project '.superpowers\sdd\2026-09-02-android-object-recognition-optimization\task-1-test-runner.csproj'`
- RED output preserved at `.superpowers/sdd/2026-09-02-android-object-recognition-optimization/task-1-fix-red.log`.
- RED failures before implementation:
  - `FAIL FindBest_RejectsNonUnitQuery`
  - `NUnit.Framework.AssertionException:   Expected: <System.ArgumentException>  But was:  null`
  - `FAIL RecognitionSample_NormalizesPrivateCopyWithoutMutatingCallerArray`
  - `NUnit.Framework.AssertionException:   Expected: not same as < 3.0f, 4.0f >`

### GREEN command/output
- GREEN test command:
  - `dotnet run --project '.superpowers\sdd\2026-09-02-android-object-recognition-optimization\task-1-test-runner.csproj'`
- GREEN output preserved at `.superpowers/sdd/2026-09-02-android-object-recognition-optimization/task-1-fix-green.log`.
- GREEN result:
  - `TOTAL 7 FAILED 0`
- Static build command:
  - `dotnet build '.superpowers\sdd\2026-09-02-android-object-recognition-optimization\task-1-static-compile.csproj'`
- Static build output preserved at `.superpowers/sdd/2026-09-02-android-object-recognition-optimization/task-1-static-build.log`.
- Static build result:
  - `Build succeeded.`
  - `0 Warning(s)`
  - `0 Error(s)`

### Files
- Modified `Assets/Scripts/Domain/Recognition/RecognitionTypes.cs`
- Modified `Assets/Scripts/Domain/Recognition/EmbeddingMath.cs`
- Modified `Assets/Tests/EditMode/EmbeddingMathTests.cs`
- Added `Assets/Tests.meta`
- Added `Assets/Tests/EditMode.meta`
- Added `Assets/Scripts/Domain/Recognition.meta`
- Updated `.superpowers/sdd/2026-09-02-android-object-recognition-optimization/task-1-report.md`

### Concerns
- Unity Test Runner remains blocked by the established LicensingClient IPC timeout, so these fix-round assertions were verified with the local runner rather than Unity Edit Mode.
- The local runner and helper `.csproj` files exist only for task-workspace verification and are not intended as product code.
