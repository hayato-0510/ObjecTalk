# Object-Aware Realtime Voice Conversation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Start a private, hands-free Japanese OpenAI Realtime voice conversation for the currently recognized ObjecTalk object, with a 10-second recognition-loss grace period and a 2-second confirmed handoff to another object.

**Architecture:** Keep recognition, conversation policy, OpenAI transport, and UI in separate assemblies. Unity creates a WebRTC offer and sends it through an authenticated ObjecTalk backend; the backend verifies the Firebase ID token, builds server-owned instructions, creates the OpenAI Realtime call, and returns the SDP answer without exposing the OpenAI API key.

**Tech Stack:** Unity 6000.1.7f1, C#/.NET Standard 2.1, Unity WebRTC 3.0.0, Unity Test Framework/NUnit, UnityWebRequest, Newtonsoft.Json, Node.js 20, Express, Firebase Admin SDK, Node built-in test runner, OpenAI Realtime API.

**Spec:** `docs/superpowers/specs/2026-09-04-object-aware-realtime-voice-conversation-design.md`

## Global Constraints

- Android is the primary runtime target; Editor behavior must remain testable without a microphone or live API.
- `RecognitionLossGraceSeconds` is exactly `10` and `ObjectSwitchConfirmationSeconds` is exactly `2` by default.
- Automatic reconnect delays are exactly `1`, `2`, and `4` seconds, with no fourth automatic attempt.
- Conversation audio, transcripts, and history must not be persisted to SQLite, Firebase, local files, PlayerPrefs, or backend logs.
- OpenAI API keys and Realtime credentials must never be stored in Unity assets, Resources, StreamingAssets, logs, or builds.
- The backend must verify a Firebase ID token before creating an OpenAI call.
- `PersonalityPrompt` is optional; the backend-owned Japanese default persona is used when it is empty.
- Existing object registration and recognition must continue when conversation setup or transport fails.
- Do not copy `D:\toritori-2` or `D:\toritori-openai2` wholesale; reuse behavior only and implement against the current OpenAI Realtime event schema.
- Preserve unrelated user changes in the dirty worktree; stage and commit only files named by the active task.

---

## File Structure

Create focused assemblies and keep OpenAI-specific code outside the domain:

```text
Assets/Scripts/Domain/Conversation/
  ObjecTalk.Domain.Conversation.asmdef
  ConversationContext.cs
  ConversationState.cs
  RecognizedObjectState.cs
  ConversationFailure.cs

Assets/Scripts/Core/Conversation/
  ObjecTalk.Core.Conversation.asmdef
  IClock.cs
  IVoiceConversationService.cs
  IConversationObjectRepository.cs
  ConversationStateMachine.cs
  ObjectConversationCoordinator.cs

Assets/Scripts/Infrastructure/Conversation/
  ObjecTalk.Infrastructure.Conversation.asmdef
  ConversationObjectRepository.cs
  FirebaseIdTokenProvider.cs
  OpenAI/RealtimeDtos.cs
  OpenAI/RealtimeEventParser.cs
  OpenAI/RealtimeCallClient.cs
  OpenAI/RealtimeWebRtcTransport.cs
  OpenAI/OpenAIRealtimeConversationService.cs
  Audio/MicrophonePermissionService.cs

Assets/Scripts/Presentation/Talk/
  ObjecTalk.Presentation.Talk.asmdef
  TalkConfiguration.cs
  TalkController.cs
  TalkView.cs

Assets/Tests/EditMode/Conversation/
  ConversationStateMachineTests.cs
  ObjectConversationCoordinatorTests.cs
  ConversationObjectRepositoryTests.cs
  RealtimeEventParserTests.cs
  RealtimeCallClientTests.cs
  OpenAIRealtimeConversationServiceTests.cs

Assets/Tests/PlayMode/Conversation/
  TalkLifecycleTests.cs

Backend/
  package.json
  package-lock.json
  .gitignore
  .env.example
  Dockerfile
  src/app.js
  src/server.js
  src/config/environment.js
  src/auth/firebaseAuth.js
  src/conversation/buildInstructions.js
  src/realtime/createRealtimeCall.js
  test/app.test.js
  test/buildInstructions.test.js
```

Modify only these existing source and configuration files:

```text
Assets/Scripts/Presentation/Controllers/EmbeddingObjectMatcher.cs
Assets/Scripts/Database/DataManager.cs
Assets/Tests/EditMode/ObjecTalk.EditModeTests.asmdef
Assets/Tests/PlayMode/ObjecTalk.PlayModeTests.asmdef
Assets/Scenes/ObjectDetectionScene.unity
Packages/manifest.json
Packages/packages-lock.json
ProjectSettings/ProjectSettings.asset
README.md
```

---

### Task 1: Conversation domain and state machine

**Files:**
- Create: `Assets/Scripts/Domain/Conversation/ObjecTalk.Domain.Conversation.asmdef`
- Create: `Assets/Scripts/Domain/Conversation/ConversationContext.cs`
- Create: `Assets/Scripts/Domain/Conversation/ConversationState.cs`
- Create: `Assets/Scripts/Domain/Conversation/RecognizedObjectState.cs`
- Create: `Assets/Scripts/Domain/Conversation/ConversationFailure.cs`
- Create: `Assets/Scripts/Core/Conversation/ObjecTalk.Core.Conversation.asmdef`
- Create: `Assets/Scripts/Core/Conversation/IClock.cs`
- Create: `Assets/Scripts/Core/Conversation/IVoiceConversationService.cs`
- Create: `Assets/Scripts/Core/Conversation/IConversationObjectRepository.cs`
- Create: `Assets/Scripts/Core/Conversation/ConversationStateMachine.cs`
- Test: `Assets/Tests/EditMode/Conversation/ConversationStateMachineTests.cs`
- Modify: `Assets/Tests/EditMode/ObjecTalk.EditModeTests.asmdef`

**Interfaces:**
- Produces: `ConversationContext`, `RecognizedObjectState`, `ConversationState`, `ConversationFailure`, `IClock`, `IVoiceConversationService`, `IConversationObjectRepository`, and `ConversationStateMachine`.
- `IVoiceConversationService.StartAsync(ConversationContext, CancellationToken)` and `StopAsync(ConversationStopReason, CancellationToken)` are used by Task 2.
- `ConversationStateMachine.TryTransition(ConversationState next, long generation)` is used by Task 7.

- [ ] **Step 1: Write the failing state-machine tests**

```csharp
[Test]
public void AllowsNormalLifecycle()
{
    var machine = new ConversationStateMachine();
    long generation = machine.BeginGeneration();
    Assert.That(machine.TryTransition(ConversationState.RequestingPermission, generation), Is.True);
    Assert.That(machine.TryTransition(ConversationState.Connecting, generation), Is.True);
    Assert.That(machine.TryTransition(ConversationState.Listening, generation), Is.True);
    Assert.That(machine.TryTransition(ConversationState.Speaking, generation), Is.True);
    Assert.That(machine.TryTransition(ConversationState.Listening, generation), Is.True);
    Assert.That(machine.TryTransition(ConversationState.Stopping, generation), Is.True);
    Assert.That(machine.TryTransition(ConversationState.Idle, generation), Is.True);
}

[Test]
public void RejectsEventsFromOldGeneration()
{
    var machine = new ConversationStateMachine();
    long oldGeneration = machine.BeginGeneration();
    long currentGeneration = machine.BeginGeneration();
    Assert.That(currentGeneration, Is.GreaterThan(oldGeneration));
    Assert.That(machine.TryTransition(ConversationState.Connecting, oldGeneration), Is.False);
    Assert.That(machine.State, Is.EqualTo(ConversationState.Idle));
}
```

- [ ] **Step 2: Run the EditMode test and verify it fails**

Run the repository's Unity batchmode EditMode command, filtering `ConversationStateMachineTests`.

Expected: compilation fails because `ConversationStateMachine` and domain types do not exist.

- [ ] **Step 3: Add domain values and service interfaces**

```csharp
public sealed class ConversationContext
{
    public ConversationContext(string objectId, string objectName, string personalityPrompt,
        string userId, string language)
    {
        ObjectId = objectId ?? throw new ArgumentNullException(nameof(objectId));
        ObjectName = objectName ?? throw new ArgumentNullException(nameof(objectName));
        PersonalityPrompt = personalityPrompt ?? string.Empty;
        UserId = userId ?? throw new ArgumentNullException(nameof(userId));
        Language = string.IsNullOrWhiteSpace(language) ? "ja" : language;
    }

    public string ObjectId { get; }
    public string ObjectName { get; }
    public string PersonalityPrompt { get; }
    public string UserId { get; }
    public string Language { get; }
}

public enum ConversationState
{
    Idle, RequestingPermission, Connecting, Listening, Speaking, Reconnecting, Stopping
}

public enum ConversationStopReason
{
    ObjectLost, ObjectChanged, UserRequested, ApplicationPaused, AuthenticationFailed, FatalError
}
```

Define `RecognizedObjectState` with `ObjectId`, `ObjectName`, `IsRecognized`, `Confidence`, and UTC `ObservedAt`; define `ConversationFailure` with a stable error code, retryability, and user-facing Japanese message.

- [ ] **Step 4: Implement the minimal deterministic state machine**

Use an explicit transition table. `BeginGeneration()` increments the generation and resets state to `Idle`. Reject transitions whose generation does not match `CurrentGeneration`; permit `Stopping → Idle` and normal transitions only. `ForceIdle(generation)` is idempotent for the active generation.

- [ ] **Step 5: Run the focused EditMode tests**

Expected: all `ConversationStateMachineTests` pass.

- [ ] **Step 6: Commit the domain boundary**

```powershell
git add -- Assets/Scripts/Domain/Conversation Assets/Scripts/Core/Conversation Assets/Tests/EditMode/Conversation/ConversationStateMachineTests.cs Assets/Tests/EditMode/ObjecTalk.EditModeTests.asmdef
git commit -m "feat: add conversation domain state machine"
```

---

### Task 2: Object recognition conversation policy

**Files:**
- Create: `Assets/Scripts/Core/Conversation/ObjectConversationCoordinator.cs`
- Test: `Assets/Tests/EditMode/Conversation/ObjectConversationCoordinatorTests.cs`

**Interfaces:**
- Consumes: `IClock.UtcNow`, `IConversationObjectRepository.GetContext(string objectId)`, and `IVoiceConversationService` from Task 1.
- Produces: `Observe(RecognizedObjectState state)`, `Tick()`, and `StopAsync(ConversationStopReason)`.
- Uses exact defaults `TimeSpan.FromSeconds(10)` and `TimeSpan.FromSeconds(2)`.

- [ ] **Step 1: Write failing tests with fake clock, repository, and voice service**

```csharp
[Test]
public async Task KeepsConversationForTenSecondLossGrace()
{
    await coordinator.ObserveAsync(Seen("apple"));
    clock.Advance(TimeSpan.FromSeconds(9.9));
    await coordinator.ObserveAsync(Lost());
    await coordinator.TickAsync();
    Assert.That(voice.StopCalls, Is.Empty);

    clock.Advance(TimeSpan.FromSeconds(0.2));
    await coordinator.TickAsync();
    Assert.That(voice.StopCalls.Single(), Is.EqualTo(ConversationStopReason.ObjectLost));
}

[Test]
public async Task SwitchesOnlyAfterTwoSecondsOfSameCandidate()
{
    await coordinator.ObserveAsync(Seen("apple"));
    await coordinator.ObserveAsync(Seen("book"));
    clock.Advance(TimeSpan.FromSeconds(1.9));
    await coordinator.TickAsync();
    Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "apple" }));

    clock.Advance(TimeSpan.FromSeconds(0.2));
    await coordinator.ObserveAsync(Seen("book"));
    Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "apple", "book" }));
}
```

Also test same-object reacquisition, changing candidates, recognition failure while no session exists, and conversation-service failure isolation.

- [ ] **Step 2: Run the focused tests and verify failure**

Expected: compilation fails because `ObjectConversationCoordinator` does not exist.

- [ ] **Step 3: Implement coordinator state and timing**

Track `activeObjectId`, `activeLastSeenAt`, `candidateObjectId`, and `candidateFirstSeenAt`. Start immediately for the first stable recognized object. Update `activeLastSeenAt` only for the active object. Reset candidate timing whenever the candidate changes or disappears. Stop after `now - activeLastSeenAt >= lossGrace`. Switch after `now - candidateFirstSeenAt >= switchConfirmation`.

Catch conversation-service failures, raise a typed failure event, and keep accepting future recognition observations.

- [ ] **Step 4: Run the focused tests**

Expected: all coordinator tests pass, including the exact 10-second and 2-second boundaries.

- [ ] **Step 5: Commit**

```powershell
git add -- Assets/Scripts/Core/Conversation/ObjectConversationCoordinator.cs Assets/Tests/EditMode/Conversation/ObjectConversationCoordinatorTests.cs
git commit -m "feat: coordinate conversations from recognition"
```

---

### Task 3: Expose object identity and load conversation context

**Files:**
- Modify: `Assets/Scripts/Presentation/Controllers/EmbeddingObjectMatcher.cs:20-38,510-535,1010-1045`
- Modify: `Assets/Scripts/Database/DataManager.cs:27-70,202-230`
- Create: `Assets/Scripts/Infrastructure/Conversation/ObjecTalk.Infrastructure.Conversation.asmdef`
- Create: `Assets/Scripts/Infrastructure/Conversation/ConversationObjectRepository.cs`
- Test: `Assets/Tests/EditMode/Conversation/ConversationObjectRepositoryTests.cs`
- Modify: `Assets/Tests/EditMode/ObjecTalk.EditModeTests.asmdef`

**Interfaces:**
- Consumes: `DatabaseManager.GetObjectWithDetails(string objectId)` and logged-in ObjecTalk `User.user_id`.
- Produces: `EmbeddingObjectMatcher.RecognitionChanged`, carrying an object ID rather than only a display name.
- Produces: `ConversationObjectRepository.GetContext(string objectId)` returning `ConversationContext`.

- [ ] **Step 1: Write a failing repository test**

```csharp
[Test]
public void BuildsContextFromPersistedObject()
{
    database.SaveObjectWithFeatures(new ObjectItem {
        object_id = "obj-1", user_id = "user-1", Name = "りんご",
        PersonalityPrompt = "明るく好奇心旺盛"
    }, new List<string>());
    var repository = new ConversationObjectRepository(database, () => "user-1");
    ConversationContext context = repository.GetContext("obj-1");
    Assert.That(context.ObjectName, Is.EqualTo("りんご"));
    Assert.That(context.PersonalityPrompt, Is.EqualTo("明るく好奇心旺盛"));
    Assert.That(context.UserId, Is.EqualTo("user-1"));
}
```

Add a test rejecting an object owned by another user and one normalizing a null personality to an empty string.

- [ ] **Step 2: Run the test and verify it fails**

Expected: `ConversationObjectRepository` is missing.

- [ ] **Step 3: Implement the repository adapter**

Inject `DatabaseManager` and a current-user-ID function. Use a temporary SQLite database in the EditMode test, so no Firebase session is required. Return null when the object is absent or owned by another user. Never log `PersonalityPrompt`.

- [ ] **Step 4: Extend recognition output with the matched object ID**

Replace the name-only event payload with `RecognizedObjectState`. At the point where `EmbeddingObjectMatcher` receives a recognition result, retain the matched object ID alongside the display metadata and invoke `RecognitionChanged`. Preserve `OnRecognitionStateChanged` temporarily as a compatibility event if existing serialized consumers use it.

- [ ] **Step 5: Run recognition and conversation EditMode/PlayMode tests**

Expected: existing recognition tests and new repository tests all pass.

- [ ] **Step 6: Commit**

```powershell
git add -- Assets/Scripts/Presentation/Controllers/EmbeddingObjectMatcher.cs Assets/Scripts/Database/DataManager.cs Assets/Scripts/Infrastructure/Conversation Assets/Tests/EditMode/Conversation/ConversationObjectRepositoryTests.cs Assets/Tests/EditMode/ObjecTalk.EditModeTests.asmdef
git commit -m "feat: expose recognized objects to conversation"
```

---

### Task 4: Authenticated backend and server-owned instructions

**Files:**
- Create: `Backend/package.json`
- Create: `Backend/package-lock.json`
- Create: `Backend/.gitignore`
- Create: `Backend/.env.example`
- Create: `Backend/Dockerfile`
- Create: `Backend/src/config/environment.js`
- Create: `Backend/src/auth/firebaseAuth.js`
- Create: `Backend/src/conversation/buildInstructions.js`
- Create: `Backend/src/realtime/createRealtimeCall.js`
- Create: `Backend/src/app.js`
- Create: `Backend/src/server.js`
- Test: `Backend/test/buildInstructions.test.js`
- Test: `Backend/test/app.test.js`

**Interfaces:**
- Consumes: Firebase bearer ID token and `{ objectId, objectName, personalityPrompt, language, sdp }`.
- Produces: `POST /api/realtime/calls` returning `application/sdp` on success.
- Produces: `GET /health` returning `{ "status": "ok" }` without dependency secrets.

- [ ] **Step 1: Define Node dependencies and scripts**

```json
{
  "name": "objectalk-realtime-backend",
  "private": true,
  "type": "module",
  "engines": { "node": ">=20" },
  "scripts": {
    "start": "node src/server.js",
    "test": "node --test"
  },
  "dependencies": {
    "express": "^5.1.0",
    "firebase-admin": "^13.4.0"
  }
}
```

Run `npm install` in `Backend` to create the lockfile. `.gitignore` must contain `.env`, `node_modules/`, and `npm-debug.log*`; `.env.example` contains names only: `OPENAI_API_KEY`, `FIREBASE_PROJECT_ID`, `PORT`, `OPENAI_REALTIME_MODEL`, and `OPENAI_REALTIME_VOICE`.

- [ ] **Step 2: Write failing instruction-builder tests**

```javascript
test('uses the safe default when personality is empty', () => {
  const instructions = buildInstructions({ objectName: 'りんご', personalityPrompt: '' });
  assert.match(instructions, /りんご/);
  assert.match(instructions, /短く親しみやすく/);
});

test('rejects overlong personality input', () => {
  assert.throws(() => buildInstructions({
    objectName: '本', personalityPrompt: 'あ'.repeat(501)
  }), /personalityPrompt/);
});
```

- [ ] **Step 3: Implement validated, server-owned instructions**

Accept object names from 1 to 100 characters and personalities from 0 to 500 characters after trimming and control-character removal. Build a fixed Japanese instruction with safety rules after the personality text so user-controlled text cannot replace the server rules. Do not log input text.

- [ ] **Step 4: Write failing authenticated-route tests**

Inject `verifyIdToken` and `createRealtimeCall` into `createApp`. Test `401` without bearer token, `400` for invalid input, `429` after the configured per-user limit, successful `application/sdp`, and sanitized `502` when OpenAI fails.

- [ ] **Step 5: Implement Firebase verification and the call route**

`verifyFirebaseBearer(req)` parses exactly one `Bearer <token>` header and calls `firebase-admin/auth.verifyIdToken(token, true)`. The route constructs the Realtime session on the server and forwards a multipart request to `https://api.openai.com/v1/realtime/calls` with `sdp` and JSON `session`. The OpenAI bearer API key is read only from `process.env.OPENAI_API_KEY`.

Set session type to `realtime`, output modalities to audio, server VAD enabled, Japanese instructions from `buildInstructions`, and the model/voice from validated server environment. Return only the SDP answer and content type to Unity.

- [ ] **Step 6: Add bounded rate limiting without transcript logging**

Implement an in-memory development limiter keyed by verified Firebase UID and IP, returning `429` when the configured window is exceeded. Structure the limiter behind a function so a shared production store can replace it. Request logging must contain request ID, hashed UID, status, latency, and error category only.

- [ ] **Step 7: Run backend tests**

Run: `npm test`

Expected: all authentication, validation, prompt, rate-limit, and failure-sanitization tests pass.

- [ ] **Step 8: Commit**

```powershell
git add -- Backend
git commit -m "feat: add authenticated realtime call backend"
```

---

### Task 5: Realtime event parser and Unity call client

**Files:**
- Create: `Assets/Scripts/Infrastructure/Conversation/OpenAI/RealtimeDtos.cs`
- Create: `Assets/Scripts/Infrastructure/Conversation/OpenAI/RealtimeEventParser.cs`
- Create: `Assets/Scripts/Infrastructure/Conversation/OpenAI/RealtimeCallClient.cs`
- Create: `Assets/Scripts/Infrastructure/Conversation/FirebaseIdTokenProvider.cs`
- Test: `Assets/Tests/EditMode/Conversation/RealtimeEventParserTests.cs`
- Test: `Assets/Tests/EditMode/Conversation/RealtimeCallClientTests.cs`

**Interfaces:**
- Consumes: `POST {BackendBaseUrl}/api/realtime/calls`, Firebase ID token, local SDP offer, and `ConversationContext`.
- Produces: `Task<string> ExchangeSdpAsync(...)` returning the remote SDP answer.
- Produces: typed `RealtimeEvent` values for speech start/stop, user transcript, assistant transcript, response start/done, and error.

- [ ] **Step 1: Write failing parser tests with representative JSON events**

```csharp
[TestCase("{\"type\":\"input_audio_buffer.speech_started\"}", RealtimeEventKind.SpeechStarted)]
[TestCase("{\"type\":\"input_audio_buffer.speech_stopped\"}", RealtimeEventKind.SpeechStopped)]
[TestCase("{\"type\":\"response.done\",\"response\":{\"id\":\"r1\"}}", RealtimeEventKind.ResponseDone)]
public void ParsesKnownEvents(string json, RealtimeEventKind expected)
{
    Assert.That(parser.TryParse(json, out var evt), Is.True);
    Assert.That(evt.Kind, Is.EqualTo(expected));
}
```

Add tests for completed user transcription, assistant transcript deltas/done events, API errors, unknown events, missing fields, and invalid JSON. Keep event-name constants in one file so current official schema changes are localized.

- [ ] **Step 2: Run parser tests and verify failure**

Expected: parser and DTO types are missing.

- [ ] **Step 3: Implement parser and immutable DTOs**

Use `JObject.Parse` inside a guarded method. Return `false` for malformed JSON; return `RealtimeEventKind.Unknown` for a syntactically valid unknown event. Never place complete raw JSON into exceptions or logs.

- [ ] **Step 4: Write failing call-client tests using an HTTP adapter fake**

Test bearer token attachment, JSON metadata plus SDP request body, cancellation, non-2xx mapping, and that response bodies are not included in user-facing failures.

- [ ] **Step 5: Implement `FirebaseIdTokenProvider` and `RealtimeCallClient`**

`FirebaseIdTokenProvider.GetAsync(bool forceRefresh, CancellationToken)` wraps `Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser.TokenAsync(forceRefresh)`. `RealtimeCallClient` uses an injected `IRealtimeHttpClient` in tests and a `UnityWebRequestRealtimeHttpClient` in production; define both beside `RealtimeCallClient`. It sends the Firebase token only to `BackendBaseUrl`, validates that the returned content type is SDP, and enforces a 15-second timeout.

- [ ] **Step 6: Run focused EditMode tests**

Expected: parser and call-client tests pass without network access.

- [ ] **Step 7: Commit**

```powershell
git add -- Assets/Scripts/Infrastructure/Conversation/OpenAI/RealtimeDtos.cs Assets/Scripts/Infrastructure/Conversation/OpenAI/RealtimeEventParser.cs Assets/Scripts/Infrastructure/Conversation/OpenAI/RealtimeCallClient.cs Assets/Scripts/Infrastructure/Conversation/FirebaseIdTokenProvider.cs Assets/Tests/EditMode/Conversation/RealtimeEventParserTests.cs Assets/Tests/EditMode/Conversation/RealtimeCallClientTests.cs
git commit -m "feat: add realtime protocol adapters"
```

---

### Task 6: WebRTC audio transport and microphone permission

**Files:**
- Modify: `Packages/manifest.json`
- Modify: `Packages/packages-lock.json`
- Create: `Assets/Scripts/Infrastructure/Conversation/Audio/MicrophonePermissionService.cs`
- Create: `Assets/Scripts/Infrastructure/Conversation/OpenAI/RealtimeWebRtcTransport.cs`
- Modify: `ProjectSettings/ProjectSettings.asset:418`
- Test: `Assets/Tests/PlayMode/Conversation/TalkLifecycleTests.cs`
- Modify: `Assets/Tests/PlayMode/ObjecTalk.PlayModeTests.asmdef`

**Interfaces:**
- Consumes: `RealtimeCallClient.ExchangeSdpAsync`, microphone permission, and parsed Realtime events.
- Produces: `ConnectAsync`, `CancelResponseAsync`, `DisconnectAsync`, remote audio playback, and typed events.

- [ ] **Step 1: Add Unity WebRTC 3.0.0 dependency**

Add `"com.unity.webrtc": "3.0.0"` to `Packages/manifest.json` and let Unity resolve `packages-lock.json`. Confirm the package supports the project's Android architectures before proceeding.

- [ ] **Step 2: Write PlayMode lifecycle tests against a transport adapter**

```csharp
[UnityTest]
public IEnumerator DisconnectDisposesTracksPeerAndDataChannel()
{
    var adapter = new FakePeerConnectionAdapter();
    var transport = CreateTransport(adapter);
    yield return transport.ConnectAsync(Context, CancellationToken.None).AsCoroutine();
    yield return transport.DisconnectAsync(CancellationToken.None).AsCoroutine();
    Assert.That(adapter.LocalTrackDisposed, Is.True);
    Assert.That(adapter.RemoteTrackDisposed, Is.True);
    Assert.That(adapter.DataChannelDisposed, Is.True);
    Assert.That(adapter.PeerDisposed, Is.True);
}
```

Also test cancellation during SDP exchange and ignoring data-channel messages after disconnect.

- [ ] **Step 3: Implement microphone permission service**

On Android use `UnityEngine.Android.Permission.Microphone`; on iOS use `Application.RequestUserAuthorization(UserAuthorization.Microphone)`; in Editor return the actual authorization result. Add a non-empty Japanese microphone usage description to Project Settings. Return `Granted`, `Denied`, or `DeniedPermanently` without starting capture.

- [ ] **Step 4: Implement WebRTC connection lifecycle**

Create `RTCPeerConnection`, create a local `AudioStreamTrack` from Unity's microphone-backed audio source, add the track, create `oai-events` data channel, create/set the local offer, send SDP through `RealtimeCallClient`, set the remote answer, and route the remote `AudioStreamTrack` into a dedicated `AudioSource`.

Do not create an `AudioClip` per received packet. Do not manually base64-encode PCM audio. Use WebRTC tracks for both directions and the data channel only for Realtime events and response cancellation.

- [ ] **Step 5: Implement deterministic disposal**

One cancellation token source owns each connection generation. `DisconnectAsync` stops playback, detaches callbacks, disposes local and remote tracks, closes/disposes the data channel, closes/disposes the peer connection, and clears references in `finally`. Calling it twice must be safe.

- [ ] **Step 6: Run PlayMode lifecycle tests and compile Android**

Expected: lifecycle tests pass and an Android development build compiles without microphone or WebRTC linker errors.

- [ ] **Step 7: Commit**

```powershell
git add -- Packages/manifest.json Packages/packages-lock.json ProjectSettings/ProjectSettings.asset Assets/Scripts/Infrastructure/Conversation/Audio Assets/Scripts/Infrastructure/Conversation/OpenAI/RealtimeWebRtcTransport.cs Assets/Tests/PlayMode/Conversation/TalkLifecycleTests.cs Assets/Tests/PlayMode/ObjecTalk.PlayModeTests.asmdef
git commit -m "feat: stream realtime audio over WebRTC"
```

---

### Task 7: Conversation service, interruption, and reconnect policy

**Files:**
- Create: `Assets/Scripts/Infrastructure/Conversation/OpenAI/OpenAIRealtimeConversationService.cs`
- Test: `Assets/Tests/EditMode/Conversation/OpenAIRealtimeConversationServiceTests.cs`

**Interfaces:**
- Consumes: `ConversationStateMachine`, `MicrophonePermissionService`, and `RealtimeWebRtcTransport`.
- Produces: the Task 1 `IVoiceConversationService` contract and UI-safe events.

- [ ] **Step 1: Write failing service tests**

Test these exact behaviors:

```csharp
[Test]
public async Task RetriesTransientFailureAtOneTwoAndFourSecondsOnly()
{
    transport.Failures.Enqueue(Transient());
    transport.Failures.Enqueue(Transient());
    transport.Failures.Enqueue(Transient());
    transport.Failures.Enqueue(Transient());
    await service.StartAsync(Context, CancellationToken.None);
    Assert.That(clock.Delays, Is.EqualTo(new[] { 1, 2, 4 }));
    Assert.That(transport.ConnectCalls, Is.EqualTo(4));
    Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Idle));
}
```

Also test no retry on authentication/permission failures, speech-start cancelling active AI output, stale generation events being ignored, switch stopping old transport before starting new transport, and idempotent stop.

- [ ] **Step 2: Run focused tests and verify failure**

Expected: service class is missing.

- [ ] **Step 3: Implement start/stop and state publication**

Request permission, advance through state machine states, connect the transport, and expose only typed state/transcript/failure events. Never log transcript contents, context personality, Firebase tokens, SDP, or Realtime event bodies.

- [ ] **Step 4: Implement interruption and retry**

On speech-start while `Speaking`, call `CancelResponseAsync` and immediately stop remote playback. Retry only failures marked retryable using the injected delay provider for 1, 2, and 4 seconds. Abort retries when the active generation changes or the coordinator cancels the operation.

- [ ] **Step 5: Run service and coordinator tests**

Expected: all conversation EditMode tests pass.

- [ ] **Step 6: Commit**

```powershell
git add -- Assets/Scripts/Infrastructure/Conversation/OpenAI/OpenAIRealtimeConversationService.cs Assets/Tests/EditMode/Conversation/OpenAIRealtimeConversationServiceTests.cs
git commit -m "feat: manage realtime conversation lifecycle"
```

---

### Task 8: Talk presentation and scene integration

**Files:**
- Create: `Assets/Scripts/Presentation/Talk/ObjecTalk.Presentation.Talk.asmdef`
- Create: `Assets/Scripts/Presentation/Talk/TalkConfiguration.cs`
- Create: `Assets/Scripts/Presentation/Talk/TalkController.cs`
- Create: `Assets/Scripts/Presentation/Talk/TalkView.cs`
- Modify: `Assets/Scenes/ObjectDetectionScene.unity`
- Test: `Assets/Tests/PlayMode/Conversation/TalkLifecycleTests.cs`

**Interfaces:**
- Consumes: `EmbeddingObjectMatcher.RecognitionChanged`, coordinator, and conversation service events.
- Produces: user-visible Japanese states without containing API or recognition logic.

- [ ] **Step 1: Write failing PlayMode presentation tests**

Instantiate a minimal Talk root with fake recognition and conversation service. Verify recognized object starts a session, state changes update the correct label, `OnApplicationPause(true)` stops with `ApplicationPaused`, and disabling the root unsubscribes and stops once.

- [ ] **Step 2: Implement configuration and view model mapping**

`TalkConfiguration` stores only non-secret values: backend development/production base URLs, 10-second grace, 2-second switch confirmation, retry delays, language `ja`, and debug flag. Refuse non-HTTPS production URLs; allow loopback HTTP only in Editor/development builds.

`TalkView` maps states to Japanese UI text: `接続中`, `聞いています`, `<物体名>が話しています`, `再接続中`, and actionable failure messages. Transcript display is transient and cleared when the session stops.

- [ ] **Step 3: Implement controller lifecycle**

Subscribe in `OnEnable`, unsubscribe in `OnDisable`, call coordinator `TickAsync` from a guarded update loop, and funnel pause, disable, scene transition, and destroy through the same idempotent stop method.

- [ ] **Step 4: Wire the existing Talk panel in `ObjectDetectionScene.unity`**

Add a `TalkConversationRoot` under the existing Talk camera panel. Attach controller/view/configuration references, use the existing result text where appropriate, and add separate status text only if needed for simultaneous object name and connection state. Do not replace camera or recognition components.

- [ ] **Step 5: Run PlayMode tests and inspect the scene in Unity**

Expected: no missing scripts or serialized references; recognition remains usable when the fake conversation service fails.

- [ ] **Step 6: Commit**

```powershell
git add -- Assets/Scripts/Presentation/Talk Assets/Scenes/ObjectDetectionScene.unity Assets/Tests/PlayMode/Conversation/TalkLifecycleTests.cs
git commit -m "feat: integrate realtime conversation with talk UI"
```

---

### Task 9: Security, regression, and operational verification

**Files:**
- Modify: `README.md`
- Create: `Backend/README.md`
- Modify: conversation files only when verification exposes a defect

**Interfaces:**
- Validates the complete feature; produces no new runtime API.

- [ ] **Step 1: Run all Unity EditMode tests**

Run the established batchmode EditMode suite for the project.

Expected: all existing recognition/database tests and all new conversation tests pass.

- [ ] **Step 2: Run all Unity PlayMode tests**

Run the established batchmode PlayMode suite.

Expected: all existing recognition lifecycle tests and new Talk lifecycle tests pass.

- [ ] **Step 3: Run backend tests and dependency audit**

```powershell
Push-Location Backend
npm test
npm audit --omit=dev
Pop-Location
```

Expected: tests pass and no high/critical production vulnerability remains unresolved.

- [ ] **Step 4: Scan tracked files for secrets and forbidden persistence**

```powershell
git grep -n -I -E 'sk-[A-Za-z0-9_-]{10,}|OPENAI_API_KEY=' -- . ':(exclude)Backend/.env.example'
git grep -n -I -E 'File\.WriteAllBytes|PlayerPrefs\.(SetString|SetInt)|Save.*Transcript|Save.*Conversation' -- Assets/Scripts/Infrastructure/Conversation Assets/Scripts/Presentation/Talk Backend/src
```

Expected: no secret value and no conversation/audio persistence call is found. Environment-variable reads and the name-only `.env.example` are allowed.

- [ ] **Step 5: Perform Android development-build smoke tests**

Verify microphone grant and denial, automatic start after recognition, interruption, 10-second loss behavior, 2-second object switch, Wi-Fi/mobile transition, pause/resume, and a 20-minute conversation. Record pass/fail and device/OS version in `Backend/README.md` or the project handoff notes without recording transcripts.

- [ ] **Step 6: Document local and production setup**

Document Node 20 setup, Firebase service-account configuration through environment/secret manager, OpenAI key configuration, local loopback caveats for Android devices, HTTPS production URL configuration, health check, Docker build/run, and key-rotation procedure. Do not include real keys, tokens, project secrets, or transcript samples.

- [ ] **Step 7: Final verification and commit**

Run `git diff --check`, Unity tests, and `npm test` once more after documentation changes.

```powershell
git add -- README.md Backend/README.md
git commit -m "docs: add realtime conversation operations guide"
```

Expected: clean verification output for all feature-owned files; unrelated pre-existing worktree changes remain untouched.
