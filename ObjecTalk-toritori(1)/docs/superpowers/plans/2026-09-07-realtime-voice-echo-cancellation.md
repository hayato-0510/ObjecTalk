# Realtime Voice — Acoustic Echo Cancellation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. This plan needs on-device iteration — every task ends with a Samsung SC-03L check, not just a passing test.

**Goal:** Stop the assistant's own voice (played through the device speaker) from re-entering the microphone and being treated as user speech by server-side VAD, so a hands-free conversation does not talk to itself and the user can barge in.

**Status when written (2026-09-07):** Software-only mitigations are already in `feature/object-aware-realtime-voice` and reduced but did not eliminate the self-loop on the SC-03L:
- Mic muted (`AudioStreamTrack.Enabled = false`) from `response.created` / `output_audio_buffer.started` until `output_audio_buffer.stopped` + 400 ms grace (3 s fallback from `response.done`).
- Remote playback `.Play()` re-issued each `response.created`; remote `AudioSource` on its own child GameObject.
- Server VAD `threshold: 0.8`, `silence_duration_ms: 1000`, `prefix_padding_ms: 300`.

The last device result before this plan: "治らない" (still loops occasionally, right after a reply). The `output_audio_buffer.stopped` change is **not yet device-verified** — run that first; this plan is only needed if it is still insufficient.

**Spec:** `docs/superpowers/specs/2026-09-04-object-aware-realtime-voice-conversation-design.md`

---

## Why software mitigation is not enough

Unity's `Microphone.Start` feeds raw PCM into `Unity.WebRTC`'s `AudioStreamTrack` via an `AudioCustomFilter` (`OnAudioFilterRead`). This bypasses the `getUserMedia` path where libwebrtc's APM (AEC3 / AGC / noise suppression) would run. There is **no echo canceller anywhere in the capture chain**. On a phone in speaker mode the speaker→mic coupling is close to full-scale, so:

- Raising the VAD threshold enough to reject the echo also rejects a quiet or distant user.
- Muting during playback removes barge-in and still leaks the reply's acoustic tail when the mic reopens.

The real fix is an echo canceller that has the played audio as its reference signal.

---

## Options considered

| # | Approach | Effort | Risk | Notes |
|---|---|---|---|---|
| A | **Android `AcousticEchoCanceler` on Unity's capture session** | High | High | `android.media.audiofx.AcousticEchoCanceler.create(audioSession)` needs the `AudioRecord` session id. Unity's `Microphone` does not expose it. Requires a native/JNI plugin that either wraps `AudioRecord` itself or reads Unity's session id by reflection into `UnityEngine` internals (fragile across Unity versions). |
| B | **`AudioManager.MODE_IN_COMMUNICATION` + speakerphone** | Low | Medium | One `AndroidJavaObject` call, no plugin. On many devices this routes playback + capture through the platform voice pipeline, which applies hardware AEC. Effectiveness is device-dependent and Unity's `Microphone` may still open a non-communication `AudioRecord` source. Cheap to try, easy to ship as a toggle. |
| C | **Replace the capture path with a native WebRTC mic track** (`getUserMedia`-style) so libwebrtc APM runs | Very high | High | `Unity.WebRTC` 3.0.0 has no `MediaDevices.getUserMedia` audio; would need a custom native audio device module. Out of scope. |
| D | **Software AEC on the PCM** (e.g. SpeexDSP / WebRTC APM as a native lib) referencing the remote track's samples | High | Medium | Deterministic, testable offline against recorded reference/mic pairs, no platform dependence. Largest code surface (native lib packaging for arm64 + the reference/capture alignment). |
| E | **Push-to-talk** (hold to speak; mic hard-muted otherwise) | Low | Low | Eliminates echo entirely. UX change, conflicts with the "hands-free auto conversation" design intent. Good fallback / accessibility option. |
| F | **Document headset requirement**, ship speaker mode as best-effort | None | — | Honest interim position while A/D is built. |

---

## Recommended sequence

1. **B first** (cheap probe). Add `MODE_IN_COMMUNICATION` behind a `TalkConfiguration` flag, measure on the SC-03L. If the loop stops and the user's voice is still picked up, ship B + keep the software mitigations as belt-and-suspenders. Done.
2. **If B is not enough → A.** Build the JNI plugin that owns the `AudioRecord` (so we control the session id) and attaches `AcousticEchoCanceler` + `NoiseSuppressor` + `AutomaticGainControl`, exposing 16 kHz mono PCM to a `MediaStreamTrack` via `AudioStreamTrack.SetData`. This replaces `UnityWebRtcPeerConnectionAdapter.CreateMicrophoneTrack`'s `Microphone.Start` path on Android.
3. **E as a parallel, always-available fallback** — a hold-to-talk affordance the user can switch to when the environment is bad. Low cost, worth doing regardless.
4. **F** in `Backend/docs/dev-device-verification.md` until A or B lands.

Do **not** start D unless A proves infeasible — it is the biggest surface for the least incremental certainty over A.

---

## Global constraints

- Android is the target; Editor must stay testable with no mic and no native plugin (guard native calls with `#if UNITY_ANDROID && !UNITY_EDITOR` and a managed no-op fallback, same pattern as `MicrophonePermissionService`).
- Keep the change inside `Assets/Scripts/Infrastructure/Conversation/` (+ `Assets/Plugins/Android/` for a plugin). Domain/Core assemblies stay engine-free.
- No conversation audio persisted anywhere (unchanged rule).
- The software mitigations already in place stay until a device test proves the new path makes them redundant; remove them in a separate, explicitly-scoped task.
- Build with `6000.1.7f1-x86_64` (has the NDK); re-run `adb reverse tcp:8080 tcp:8080` after every build. See the android-build-notes memory.

---

## Task 1 — Probe: `MODE_IN_COMMUNICATION` audio mode (Option B)

- [ ] Add `bool useCommunicationAudioMode` to `TalkConfiguration` (serialized, default `true`), exposed as `UseCommunicationAudioMode`.
- [ ] New `Assets/Scripts/Infrastructure/Conversation/Audio/AndroidAudioModeScope.cs`: `IDisposable` that on construction calls (via `AndroidJavaObject`) `AudioManager.setMode(MODE_IN_COMMUNICATION)` and `setSpeakerphoneOn(true)`, and on dispose restores the previous mode. `#if !UNITY_ANDROID || UNITY_EDITOR` → no-op struct.
- [ ] `RealtimeWebRtcTransport.ConnectAsync` opens the scope when `configuration.UseCommunicationAudioMode`; `DisposeResourcesAsync` closes it. Thread it through the constructor like `microphoneSource`.
- [ ] EditMode: `AndroidAudioModeScope` no-op path constructs and disposes without touching Unity APIs; transport opens/closes it exactly once per connect/disconnect (assert via a fake `IAudioModeScope`).
- [ ] **Device:** build, converse 5+ turns on the SC-03L. Record: does the self-loop stop? Is the user's normal-volume speech still detected? Does playback still route to the speaker (not earpiece)?
- [ ] If yes to all → mark Task 3/4 optional, go to Task 5. If partial → keep the flag, continue to Task 2.

## Task 2 — Decide A vs E based on Task 1

- [ ] If Task 1 removed the loop but hurt mic sensitivity or audio routing: try `MODE_NORMAL` + only `setSpeakerphoneOn`, and try `VOICE_COMMUNICATION` recording via the plugin in Task 3.
- [ ] If Task 1 did nothing measurable: `AudioManager` is not reaching Unity's `AudioRecord`; A (own the `AudioRecord`) is required. Proceed to Task 3.
- [ ] Regardless: implement Task 4 (push-to-talk) in parallel — it is independent and always useful.

## Task 3 — JNI mic plugin with hardware AEC (Option A)

- [ ] `Assets/Plugins/Android/ObjecTalkMic/` — a small AAR or `.java` sources compiled by Gradle: a class that
  - opens `AudioRecord` with `MediaRecorder.AudioSource.VOICE_COMMUNICATION`, 16 kHz mono, 16-bit;
  - if `AcousticEchoCanceler.isAvailable()` attaches it to the record session, same for `NoiseSuppressor`, `AutomaticGainControl`;
  - runs a read loop on its own thread, handing `short[]`/`float[]` frames to a callback.
- [ ] `Assets/Scripts/Infrastructure/Conversation/Audio/AndroidEchoCancellingMicrophone.cs` — `AndroidJavaProxy` callback → `AudioStreamTrack.SetData(float[], 1, 16000)`. Implements a small `IRealtimeMicrophoneSource` interface.
- [ ] `UnityWebRtcPeerConnectionAdapter.CreateMicrophoneTrack`: on Android with the plugin present and `configuration` opting in, build the track from `AndroidEchoCancellingMicrophone` instead of `Microphone.Start`; Editor and the fallback keep the current path.
- [ ] Handle lifecycle: start on connect, stop + release on disconnect, stop on `OnApplicationPause(true)`, resume on the permission grant.
- [ ] EditMode: the selection logic (plugin path vs `Microphone` fallback) is a pure function over `(isAndroid, pluginAvailable, configFlag)` — test it. The JNI class itself is device-only.
- [ ] **Device:** converse 10+ turns. Self-loop must be gone AND barge-in ("AIが話している最中に話しかける→AIの音声が止まる") must work with the mic no longer force-muted during playback. Then revisit whether the `AudioStreamTrack.Enabled` muting can be dropped.

## Task 4 — Push-to-talk fallback (Option E, parallel)

- [ ] `TalkConfiguration` flag `handsFree` (default `true`). When `false`, `TalkController` keeps the mic capture disabled (`SetLocalAudioCaptureEnabled(false)`) except while a "talk" UI control is held.
- [ ] Add the hold control to the Talk panel (a button that reports press/release); wire press→`SetLocalAudioCaptureEnabled(true)`, release→`false`.
- [ ] EditMode/PlayMode: press/release toggles capture; a conversation still starts and the assistant is still heard with the mic gated.
- [ ] **Device:** confirm zero self-loop in hold-to-talk mode even on speaker.

## Task 5 — Retune / remove now-redundant mitigations

- [ ] With a working AEC path, lower server VAD back toward defaults (`threshold` ~0.5–0.6, `silence_duration_ms` ~400) and confirm on device.
- [ ] Decide whether `output_audio_buffer`-driven muting and the resume grace are still needed; if AEC + barge-in work, remove them and their tests, or keep a much shorter grace.
- [ ] Update `Backend/docs/dev-device-verification.md` (remove the headset caveat if speaker mode now passes; document the config flags).

## Task 6 — Clean up and verify

- [ ] Remove all `[TalkDiag]` diagnostics (`TalkController.cs`, `ConversationObjectRepository.cs`, `Backend/src/app.js`; keep the OpenAI-error log in `createRealtimeCall.js` or make it a real logger).
- [ ] `uloop compile` clean; EditMode + PlayMode green; `Backend` `node --test` green.
- [ ] Full `dev-device-verification.md` checklist sign-off, including barge-in and the 20-minute continuous conversation.

---

## Open questions for the human

- Is hands-free on the phone **speaker** a hard requirement, or is "headset for hands-free, push-to-talk otherwise" acceptable for v1?
- SC-03L (`AcousticEchoCanceler.isAvailable()`): needs a one-line device check before committing to Task 3.
- Budget for a native plugin in the build (Gradle changes, arm64-only) — the project already carries custom `Assets/Plugins/Android/AndroidManifest.xml` churn, so the pipeline exists.
