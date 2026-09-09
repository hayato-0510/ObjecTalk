# オープンキャンパス キオスク運用の土台 実装計画（計画 A）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** キオスクを無操作で自動リセットし、来場者の登録物だけを一括削除できる状態にする。5学科のシード物体は残す。

**Architecture:** 判定ロジックは純粋クラス `ExhibitionIdlePolicy` に集約し、EditMode で網羅的にテストする。MonoBehaviour `ExhibitionSessionController` は入力検知・アトラクト画面の表示・シーン再ロードを policy に委譲する薄いシェルにする。シード保護は `ObjectItem.IsSeed` 列と `DatabaseManager` の2メソッドで実現し、既存のデバッグ削除ボタンをそこへ配線する。

**Tech Stack:** Unity 6000.x、C#、新 Input System（`com.unity.inputsystem`）、sqlite-net（`SQLiteNet` asmdef）、NUnit（EditMode / PlayMode Test Runner）。

**Spec:** `docs/superpowers/specs/2026-09-10-open-campus-exhibition-design.md`

## Global Constraints

- Active Input Handling は New（新 Input System のみ）。`UnityEngine.Input` は使わず `UnityEngine.InputSystem` を使う。
- 会話内容を端末・SQLite・バックエンドに保存しない（既存方針を継承）。
- push-to-talk 固定。`TalkConfiguration.handsFree = false`、`exhibitionMode = true`（シーン設定。Task 6）。
- リセット時に再ロードするシーン名は `"ObjectDetectionScene"`（`EmbeddingObjectMatcher.ReturnToHome` と同じ）。
- `is_seed = true` の物体は一括削除の対象外。
- Android を主要な実行環境とする。
- 新規の純粋クラスは既存のパターンに合わせ、`IClock` を注入せずメソッド引数で `DateTimeOffset now` を受け取る（`ObjectConversationCoordinator` に倣う）。

**この計画に含まないもの（別計画）**

- 登録フローのキオスク向け簡素化（分類プリセット、命名スキップ、配置ステップ省略）— シーン UI 工事のため計画 A2 として分離。
- カテゴリ別プロンプト・提案チップ、バックエンドのツール層、ネット断フォールバック — 計画 B / C。

---

### Task 1: `ExhibitionIdlePolicy`（無操作 → アトラクト → リセットの判定）

**Files:**
- Create: `Assets/Scripts/Core/Exhibition/ExhibitionIdlePolicy.cs`
- Create: `Assets/Scripts/Core/Exhibition/ObjecTalk.Core.Exhibition.asmdef`
- Modify: `Assets/Tests/EditMode/ObjecTalk.EditModeTests.asmdef`（`references` に `"ObjecTalk.Core.Exhibition"` を追加）
- Test: `Assets/Tests/EditMode/Exhibition/ExhibitionIdlePolicyTests.cs`

**Interfaces:**
- Consumes: なし（純粋 C#）
- Produces:
  - `enum ObjecTalk.Core.Exhibition.ExhibitionIdleState { Interacting, Attract, Reset }`
  - `sealed class ExhibitionIdlePolicy`
    - `ExhibitionIdlePolicy(TimeSpan idleToAttract, TimeSpan attractToReset)` — どちらも 0 以下なら `ArgumentOutOfRangeException`
    - `void RecordActivity(DateTimeOffset now)`
    - `void SetConversationActive(bool active, DateTimeOffset now)`
    - `ExhibitionIdleState Evaluate(DateTimeOffset now)`

- [ ] **Step 1: asmdef を作成する**

`Assets/Scripts/Core/Exhibition/ObjecTalk.Core.Exhibition.asmdef`:

```json
{
  "name": "ObjecTalk.Core.Exhibition",
  "rootNamespace": "ObjecTalk.Core.Exhibition",
  "references": [],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": true
}
```

- [ ] **Step 2: EditModeTests asmdef に参照を足す**

`Assets/Tests/EditMode/ObjecTalk.EditModeTests.asmdef` の `references` 配列に `"ObjecTalk.Core.Exhibition"` を追加する。

- [ ] **Step 3: 失敗するテストを書く**

`Assets/Tests/EditMode/Exhibition/ExhibitionIdlePolicyTests.cs`:

```csharp
using System;
using NUnit.Framework;
using ObjecTalk.Core.Exhibition;

namespace ObjecTalk.Tests.EditMode.Exhibition
{
    [TestFixture]
    public sealed class ExhibitionIdlePolicyTests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
        private static readonly TimeSpan IdleToAttract = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan AttractToReset = TimeSpan.FromSeconds(30);

        private static ExhibitionIdlePolicy CreatePolicy() => new ExhibitionIdlePolicy(IdleToAttract, AttractToReset);

        [Test]
        public void Constructor_RejectsNonPositiveSpans()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExhibitionIdlePolicy(TimeSpan.Zero, AttractToReset));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExhibitionIdlePolicy(IdleToAttract, TimeSpan.FromSeconds(-1)));
        }

        [Test]
        public void FreshPolicy_ShowsAttract_AndNeverResetsWithoutActivity()
        {
            var policy = CreatePolicy();

            Assert.That(policy.Evaluate(T0), Is.EqualTo(ExhibitionIdleState.Attract));
            Assert.That(policy.Evaluate(T0 + TimeSpan.FromHours(3)), Is.EqualTo(ExhibitionIdleState.Attract));
        }

        [Test]
        public void RightAfterActivity_IsInteracting()
        {
            var policy = CreatePolicy();
            policy.RecordActivity(T0);

            Assert.That(policy.Evaluate(T0), Is.EqualTo(ExhibitionIdleState.Interacting));
            Assert.That(policy.Evaluate(T0 + TimeSpan.FromSeconds(44)), Is.EqualTo(ExhibitionIdleState.Interacting));
        }

        [Test]
        public void IdlePastAttractThreshold_ShowsAttract()
        {
            var policy = CreatePolicy();
            policy.RecordActivity(T0);

            Assert.That(policy.Evaluate(T0 + TimeSpan.FromSeconds(45)), Is.EqualTo(ExhibitionIdleState.Attract));
            Assert.That(policy.Evaluate(T0 + TimeSpan.FromSeconds(74)), Is.EqualTo(ExhibitionIdleState.Attract));
        }

        [Test]
        public void IdlePastResetThreshold_RequestsReset()
        {
            var policy = CreatePolicy();
            policy.RecordActivity(T0);

            Assert.That(policy.Evaluate(T0 + TimeSpan.FromSeconds(75)), Is.EqualTo(ExhibitionIdleState.Reset));
        }

        [Test]
        public void ActiveConversation_SuppressesAttractAndReset()
        {
            var policy = CreatePolicy();
            policy.RecordActivity(T0);
            policy.SetConversationActive(true, T0);

            Assert.That(policy.Evaluate(T0 + TimeSpan.FromMinutes(10)), Is.EqualTo(ExhibitionIdleState.Interacting));

            policy.SetConversationActive(false, T0 + TimeSpan.FromMinutes(10));
            Assert.That(
                policy.Evaluate(T0 + TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(75)),
                Is.EqualTo(ExhibitionIdleState.Reset));
        }
    }
}
```

- [ ] **Step 4: テストが失敗することを確認する**

Unity Test Runner（EditMode）で `ExhibitionIdlePolicyTests` を実行する。
Expected: コンパイルエラー（`ExhibitionIdlePolicy` 未定義）。

- [ ] **Step 5: 実装する**

`Assets/Scripts/Core/Exhibition/ExhibitionIdlePolicy.cs`:

```csharp
using System;

namespace ObjecTalk.Core.Exhibition
{
    public enum ExhibitionIdleState
    {
        Interacting,
        Attract,
        Reset
    }

    /// <summary>
    /// 展示キオスクの無操作判定。来場者がしばらく触っていなければアトラクト画面へ、さらに
    /// 放置されたら次の来場者のためにシーンを再ロードする、という3状態を返す。
    /// 一度も操作されていない起動直後はアトラクト固定で、勝手にリセットはしない。
    /// </summary>
    public sealed class ExhibitionIdlePolicy
    {
        private readonly TimeSpan _idleToAttract;
        private readonly TimeSpan _attractToReset;

        private bool _hasActivity;
        private bool _conversationActive;
        private DateTimeOffset _lastActivityAt;

        public ExhibitionIdlePolicy(TimeSpan idleToAttract, TimeSpan attractToReset)
        {
            if (idleToAttract <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(idleToAttract));
            }

            if (attractToReset <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(attractToReset));
            }

            _idleToAttract = idleToAttract;
            _attractToReset = attractToReset;
        }

        /// <summary>タッチ・クリック・パネル操作のたびに呼ぶ。</summary>
        public void RecordActivity(DateTimeOffset now)
        {
            _hasActivity = true;
            _lastActivityAt = now;
        }

        /// <summary>会話状態が変わるたびに呼ぶ。会話中はアトラクトにもリセットにも移らない。</summary>
        public void SetConversationActive(bool active, DateTimeOffset now)
        {
            _conversationActive = active;
            if (active)
            {
                _hasActivity = true;
                _lastActivityAt = now;
            }
        }

        public ExhibitionIdleState Evaluate(DateTimeOffset now)
        {
            if (_conversationActive)
            {
                return ExhibitionIdleState.Interacting;
            }

            if (!_hasActivity)
            {
                // 起動直後、まだ誰も触っていない。片付けるものが無いのでリセットはしない。
                return ExhibitionIdleState.Attract;
            }

            var idle = now - _lastActivityAt;
            if (idle < _idleToAttract)
            {
                return ExhibitionIdleState.Interacting;
            }

            if (idle < _idleToAttract + _attractToReset)
            {
                return ExhibitionIdleState.Attract;
            }

            return ExhibitionIdleState.Reset;
        }
    }
}
```

- [ ] **Step 6: テストが通ることを確認する**

Unity Test Runner（EditMode）で `ExhibitionIdlePolicyTests` を実行する。
Expected: 全 6 ケース PASS。

- [ ] **Step 7: コミットする**

```bash
git add Assets/Scripts/Core/Exhibition/ Assets/Tests/EditMode/Exhibition/ Assets/Tests/EditMode/ObjecTalk.EditModeTests.asmdef
git commit -m "feat: add ExhibitionIdlePolicy for kiosk auto-reset"
```

---

### Task 2: `ObjectItem.IsSeed` 列とシード保護の DB メソッド

**Files:**
- Modify: `Assets/Scripts/Database/DataManager.cs`（`ObjectItem` に列を追加、`DatabaseManager` に 2 メソッド追加）
- Test: `Assets/Tests/EditMode/ExhibitionSeedTests.cs`

**Interfaces:**
- Consumes: `DatabaseManager(string dbPath)`（既存の internal コンストラクタ）、`ObjectItem`、`User`
- Produces:
  - `ObjectItem.IsSeed`（`[Column("is_seed")] public bool IsSeed { get; set; }`）
  - `DatabaseManager.MarkAllObjectsAsSeed()` — `objects` の全行の `is_seed` を 1 にする
  - `DatabaseManager.ClearVisitorRegistrations()` — `is_seed = 0` の物体と、その `object_features` 行・`recognition_samples` 行だけを削除する。`is_seed = 1` の物体、`users` 行、`features` 行は残す

- [ ] **Step 1: 失敗するテストを書く**

`Assets/Tests/EditMode/ExhibitionSeedTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ObjecTalk.Infrastructure.Database;
using SQLite;

namespace ObjecTalk.Tests.EditMode
{
    [TestFixture]
    public sealed class ExhibitionSeedTests
    {
        private string _databasePath;

        [SetUp]
        public void SetUp()
        {
            _databasePath = Path.Combine(Path.GetTempPath(), $"objectalk-seed-{Guid.NewGuid():N}.sqlite");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                var path = _databasePath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void IsSeed_DefaultsToFalse_AndRoundTrips()
        {
            using (var database = new DatabaseManager(_databasePath))
            {
                database.SaveUser(CreateUser());
                database.SaveObjectWithFeatures(CreateObject("visitor-1", isSeed: false), new List<string>());
                database.SaveObjectWithFeatures(CreateObject("seed-1", isSeed: true), new List<string>());
            }

            using (var connection = new SQLiteConnection(_databasePath))
            {
                Assert.That(connection.Find<ObjectItem>("visitor-1").IsSeed, Is.False);
                Assert.That(connection.Find<ObjectItem>("seed-1").IsSeed, Is.True);
            }
        }

        [Test]
        public void MarkAllObjectsAsSeed_SetsEveryExistingRow()
        {
            using (var database = new DatabaseManager(_databasePath))
            {
                database.SaveUser(CreateUser());
                database.SaveObjectWithFeatures(CreateObject("a", isSeed: false), new List<string>());
                database.SaveObjectWithFeatures(CreateObject("b", isSeed: false), new List<string>());

                database.MarkAllObjectsAsSeed();
            }

            using (var connection = new SQLiteConnection(_databasePath))
            {
                Assert.That(connection.Find<ObjectItem>("a").IsSeed, Is.True);
                Assert.That(connection.Find<ObjectItem>("b").IsSeed, Is.True);
            }
        }

        [Test]
        public void ClearVisitorRegistrations_RemovesNonSeedOnly_AndKeepsUser()
        {
            using (var database = new DatabaseManager(_databasePath))
            {
                database.SaveUser(CreateUser());
                database.SaveObjectWithFeatures(CreateObject("seed-1", isSeed: true), new List<string> { "電子機器" });
                database.SaveObjectWithFeatures(CreateObject("visitor-1", isSeed: false), new List<string> { "文房具" });
                database.RecognitionSamples.SaveSamples("seed-1", CreateSamples());
                database.RecognitionSamples.SaveSamples("visitor-1", CreateSamples());

                database.ClearVisitorRegistrations();
            }

            using (var connection = new SQLiteConnection(_databasePath))
            {
                Assert.That(connection.Find<User>("user-1"), Is.Not.Null);
                Assert.That(connection.Find<ObjectItem>("seed-1"), Is.Not.Null);
                Assert.That(connection.Find<ObjectItem>("visitor-1"), Is.Null);
                Assert.That(
                    connection.ExecuteScalar<int>("SELECT COUNT(*) FROM recognition_samples WHERE object_id = ?", "visitor-1"),
                    Is.Zero);
                Assert.That(
                    connection.ExecuteScalar<int>("SELECT COUNT(*) FROM recognition_samples WHERE object_id = ?", "seed-1"),
                    Is.EqualTo(3));
                Assert.That(
                    connection.ExecuteScalar<int>("SELECT COUNT(*) FROM object_features WHERE object_id = ?", "visitor-1"),
                    Is.Zero);
            }
        }

        private static User CreateUser()
        {
            return new User
            {
                user_id = "user-1",
                Name = "exhibition",
                DateOfBirth = "2000-01-01",
                GmailAddress = "exhibition@example.invalid",
                IsCalendarSyncEnabled = false,
                IsWeatherSyncEnabled = false
            };
        }

        private static ObjectItem CreateObject(string objectId, bool isSeed)
        {
            return new ObjectItem
            {
                object_id = objectId,
                user_id = "user-1",
                Name = objectId,
                ImageData = new byte[] { 1, 2, 3 },
                ImageWidth = 320,
                ImageHeight = 240,
                EyeStyleId = -1,
                MouthStyleId = -1,
                IsSeed = isSeed
            };
        }

        private static RecognitionSampleRecord[] CreateSamples()
        {
            RecognitionSampleRecord Make(int index) => new RecognitionSampleRecord(
                index,
                new RecognitionDataVersion(1, "model-v2", "rgb-224-v1"),
                new[] { 1f, 2f, 3f, 4f },
                new byte[] { 1, 2, 3, 4 },
                2,
                3,
                0,
                new byte[] { 1, 2, 3, 4, 5, 6 },
                320,
                240);

            return new[] { Make(0), Make(1), Make(2) };
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Unity Test Runner（EditMode）で `ExhibitionSeedTests` を実行する。
Expected: コンパイルエラー（`ObjectItem.IsSeed` / `MarkAllObjectsAsSeed` / `ClearVisitorRegistrations` 未定義）。

- [ ] **Step 3: `ObjectItem` に列を追加する**

`Assets/Scripts/Database/DataManager.cs` の `ObjectItem` クラス、`personality_prompt` の直後に:

```csharp
    [Column("is_seed")]
    public bool IsSeed { get; set; }
```

`CreateTable<ObjectItem>()` が既存 DB へ `ALTER TABLE ... ADD COLUMN "is_seed"` を自動発行する。既存行は非シード（0）として読まれる。

- [ ] **Step 4: `DatabaseManager` に 2 メソッドを追加する**

`DeleteObject` の直後に:

```csharp
    /// <summary>
    /// 現在登録されている物体をすべてシード（展示用・削除対象外）にする。5学科の代表物体を
    /// セットアップし終えた時点で1回呼ぶ運用を想定している。
    /// </summary>
    public void MarkAllObjectsAsSeed()
    {
        _connection.Execute("UPDATE objects SET is_seed = 1");
    }

    /// <summary>
    /// 来場者が登録した物体（is_seed = 0）と、その特徴の関連付け・認識サンプルだけを削除する。
    /// シード物体、ユーザー行、features 行は残す。
    /// </summary>
    public void ClearVisitorRegistrations()
    {
        _connection.BeginTransaction();
        try
        {
            var visitorIds = _connection.Table<ObjectItem>()
                .Where(o => !o.IsSeed)
                .Select(o => o.object_id)
                .ToList();

            foreach (var objectId in visitorIds)
            {
                _connection.Table<ObjectFeature>().Delete(of => of.object_id == objectId);
                _connection.Execute("DELETE FROM recognition_samples WHERE object_id = ?", objectId);
                _connection.Delete<ObjectItem>(objectId);
            }

            _connection.Commit();
            Debug.Log($"来場者登録 {visitorIds.Count} 件を削除しました。シード物体は保持します。");
        }
        catch (Exception ex)
        {
            _connection.Rollback();
            Debug.LogError($"来場者登録の削除中にエラーが発生しました: {ex.Message}");
            throw;
        }
    }
```

- [ ] **Step 5: テストが通ることを確認する**

Unity Test Runner（EditMode）で `ExhibitionSeedTests` を実行する。
Expected: 全 3 ケース PASS。

- [ ] **Step 6: 既存の DB テストが壊れていないことを確認する**

Unity Test Runner（EditMode）で `RecognitionSampleRepositoryTests` を実行する。
Expected: 全 PASS（`InvalidateObsolete_RemovesOnlyRecognitionData_AndIsIdempotent` が `ObjectItem` の列追加後も通る）。

- [ ] **Step 7: コミットする**

```bash
git add Assets/Scripts/Database/DataManager.cs Assets/Tests/EditMode/ExhibitionSeedTests.cs
git commit -m "feat: add is_seed column and visitor-registration wipe"
```

---

### Task 3: `TalkController.IsConversationActive`

**Files:**
- Modify: `Assets/Scripts/Presentation/Talk/TalkController.cs`
- Test: `Assets/Tests/PlayMode/Conversation/TalkLifecycleTests.cs`（テストを 1 本追加）

**Interfaces:**
- Consumes: `TalkController.InitializeForTests(...)`、`FakeVoiceConversationService`（同ファイル内の既存 fake）、`ConversationState`
- Produces: `TalkController.IsConversationActive`（`public bool` — `voiceService` が非 null かつ `CurrentState != ConversationState.Idle` のとき true）

- [ ] **Step 1: 失敗するテストを書く**

`TalkLifecycleTests.cs` の `TalkController_RecognizedObjectStartsConversationAndUpdatesJapaneseState` の直後に追加:

```csharp
        [UnityTest]
        public IEnumerator TalkController_IsConversationActive_TracksVoiceServiceState()
        {
            var fixture = CreateTalkFixture();
            var controller = fixture.Root.GetComponent<TalkController>();
            fixture.Root.SetActive(true);

            Assert.That(controller.IsConversationActive, Is.False);

            fixture.Source.Emit(Seen("object-1", "Cup"));
            yield return WaitUntil(() => fixture.Voice.StartedObjectIds.Count == 1);
            Assert.That(controller.IsConversationActive, Is.True);

            fixture.Voice.PublishState(ConversationState.Idle);
            Assert.That(controller.IsConversationActive, Is.False);

            UnityEngine.Object.Destroy(fixture.Root);
        }
```

- [ ] **Step 2: テストが失敗することを確認する**

Unity Test Runner（PlayMode）で `TalkController_IsConversationActive_TracksVoiceServiceState` を実行する。
Expected: コンパイルエラー（`IsConversationActive` 未定義）。

- [ ] **Step 3: 実装する**

`TalkController.cs`、`public void InitializeForTests(...)` の直前に:

```csharp
        /// <summary>
        /// 会話セッションが動いているか。展示の無操作リセット判定が参照する。
        /// </summary>
        public bool IsConversationActive =>
            voiceService != null && voiceService.CurrentState != ConversationState.Idle;
```

- [ ] **Step 4: テストが通ることを確認する**

Unity Test Runner（PlayMode）で `TalkController_IsConversationActive_TracksVoiceServiceState` を実行する。
Expected: PASS。

- [ ] **Step 5: コミットする**

```bash
git add Assets/Scripts/Presentation/Talk/TalkController.cs Assets/Tests/PlayMode/Conversation/TalkLifecycleTests.cs
git commit -m "feat: expose TalkController.IsConversationActive"
```

---

### Task 4: `ExhibitionSessionController`（MonoBehaviour シェル）

**Files:**
- Create: `Assets/Scripts/Presentation/Exhibition/ExhibitionSessionController.cs`
- Test: `Assets/Tests/PlayMode/Exhibition/ExhibitionSessionControllerTests.cs`
- Create: `Assets/Tests/PlayMode/Exhibition/`（新ディレクトリ。`ObjecTalk.PlayModeTests` asmdef 配下のため asmdef 追加は不要）

**Interfaces:**
- Consumes: `ObjecTalk.Core.Exhibition.ExhibitionIdlePolicy` / `ExhibitionIdleState`（Task 1）、`ObjecTalk.Presentation.Talk.TalkController.IsConversationActive`（Task 3）
- Produces:
  - `sealed class ExhibitionSessionController : MonoBehaviour`
  - `void InitializeForTests(ExhibitionIdlePolicy policy, GameObject attractOverlay, Action onReset, Func<DateTimeOffset> now)` — テスト用シーム。`onReset` は本番では `SceneManager.LoadScene("ObjectDetectionScene")`
  - `void NotifyActivity()` — 外部（アトラクト画面のタップ受けなど）からも活動を通知できる

- [ ] **Step 1: 失敗するテストを書く**

`Assets/Tests/PlayMode/Exhibition/ExhibitionSessionControllerTests.cs`:

```csharp
using System;
using System.Collections;
using NUnit.Framework;
using ObjecTalk.Core.Exhibition;
using UnityEngine;
using UnityEngine.TestTools;

namespace ObjecTalk.Tests.PlayMode.Exhibition
{
    public sealed class ExhibitionSessionControllerTests
    {
        private static ExhibitionIdlePolicy Policy() =>
            new ExhibitionIdlePolicy(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(30));

        [UnityTest]
        public IEnumerator ShowsAttractOverlay_WhenPolicyReportsAttract()
        {
            var now = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
            var overlay = new GameObject("Attract");
            var resetCalls = 0;

            var host = new GameObject("ExhibitionSessionController");
            var controller = host.AddComponent<ExhibitionSessionController>();
            controller.InitializeForTests(Policy(), overlay, () => resetCalls++, () => now);

            controller.NotifyActivity();
            yield return null;
            Assert.That(overlay.activeSelf, Is.False, "操作直後はアトラクトを隠す");

            now += TimeSpan.FromSeconds(46);
            yield return null;
            Assert.That(overlay.activeSelf, Is.True, "無操作 45 秒超でアトラクト表示");
            Assert.That(resetCalls, Is.Zero);

            now += TimeSpan.FromSeconds(30);
            yield return null;
            Assert.That(resetCalls, Is.EqualTo(1), "さらに 30 秒でリセット");

            UnityEngine.Object.Destroy(host);
            UnityEngine.Object.Destroy(overlay);
        }

        [UnityTest]
        public IEnumerator ResetIsCalledOnce_EvenIfPolicyKeepsReportingReset()
        {
            var now = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
            var overlay = new GameObject("Attract");
            var resetCalls = 0;

            var host = new GameObject("ExhibitionSessionController");
            var controller = host.AddComponent<ExhibitionSessionController>();
            controller.InitializeForTests(Policy(), overlay, () => resetCalls++, () => now);

            controller.NotifyActivity();
            now += TimeSpan.FromSeconds(120);
            yield return null;
            yield return null;
            yield return null;

            Assert.That(resetCalls, Is.EqualTo(1));

            UnityEngine.Object.Destroy(host);
            UnityEngine.Object.Destroy(overlay);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Unity Test Runner（PlayMode）で `ExhibitionSessionControllerTests` を実行する。
Expected: コンパイルエラー（`ExhibitionSessionController` 未定義）。

- [ ] **Step 3: 実装する**

`Assets/Scripts/Presentation/Exhibition/ExhibitionSessionController.cs`:

```csharp
using System;
using ObjecTalk.Core.Exhibition;
using ObjecTalk.Presentation.Talk;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ObjecTalk.Presentation.Exhibition
{
    /// <summary>
    /// 展示キオスクの無操作リセット。判定は <see cref="ExhibitionIdlePolicy"/> に委譲し、この
    /// コンポーネントは入力の検知、アトラクト画面の表示切替、シーン再ロードだけを行う。
    /// </summary>
    public sealed class ExhibitionSessionController : MonoBehaviour
    {
        [Tooltip("無操作でアトラクト画面へ移るまでの秒数。")]
        [SerializeField] private float idleToAttractSeconds = 45f;

        [Tooltip("アトラクト表示後、さらに無操作でシーンを再ロードするまでの秒数。")]
        [SerializeField] private float attractToResetSeconds = 30f;

        [Tooltip("待機中に前面へ出すアトラクト画面のルート。会話中・操作中は非表示になる。")]
        [SerializeField] private GameObject attractOverlay;

        [Tooltip("会話状態を参照する TalkController。未設定ならシーンから探す。")]
        [SerializeField] private TalkController talkController;

        private ExhibitionIdlePolicy _policy;
        private Action _onReset;
        private Func<DateTimeOffset> _now;
        private bool _resetRequested;

        private void Awake()
        {
            if (_policy == null)
            {
                _policy = new ExhibitionIdlePolicy(
                    TimeSpan.FromSeconds(Mathf.Max(1f, idleToAttractSeconds)),
                    TimeSpan.FromSeconds(Mathf.Max(1f, attractToResetSeconds)));
                _onReset = () => SceneManager.LoadScene("ObjectDetectionScene");
                _now = () => DateTimeOffset.UtcNow;
            }

            if (talkController == null)
            {
                talkController = FindAnyObjectByType<TalkController>();
            }
        }

        public void InitializeForTests(
            ExhibitionIdlePolicy policy,
            GameObject overlay,
            Action onReset,
            Func<DateTimeOffset> now)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            attractOverlay = overlay;
            _onReset = onReset ?? throw new ArgumentNullException(nameof(onReset));
            _now = now ?? throw new ArgumentNullException(nameof(now));
        }

        /// <summary>アトラクト画面のタップなど、外部からの操作通知。</summary>
        public void NotifyActivity()
        {
            _policy?.RecordActivity(_now());
        }

        private void Update()
        {
            if (_policy == null || _resetRequested)
            {
                return;
            }

            var now = _now();

            if (HasPointerActivityThisFrame())
            {
                _policy.RecordActivity(now);
            }

            if (talkController != null)
            {
                _policy.SetConversationActive(talkController.IsConversationActive, now);
            }

            switch (_policy.Evaluate(now))
            {
                case ExhibitionIdleState.Interacting:
                    SetAttractVisible(false);
                    break;

                case ExhibitionIdleState.Attract:
                    SetAttractVisible(true);
                    break;

                case ExhibitionIdleState.Reset:
                    _resetRequested = true;
                    _onReset();
                    break;
            }
        }

        private static bool HasPointerActivityThisFrame()
        {
            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame)
            {
                return true;
            }

            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.anyKey.wasPressedThisFrame;
        }

        private void SetAttractVisible(bool visible)
        {
            if (attractOverlay != null && attractOverlay.activeSelf != visible)
            {
                attractOverlay.SetActive(visible);
            }
        }
    }
}
```

- [ ] **Step 4: テストが通ることを確認する**

Unity Test Runner（PlayMode）で `ExhibitionSessionControllerTests` を実行する。
Expected: 2 ケース PASS。

- [ ] **Step 5: コミットする**

```bash
git add Assets/Scripts/Presentation/Exhibition/ Assets/Tests/PlayMode/Exhibition/
git commit -m "feat: add ExhibitionSessionController kiosk shell"
```

---

### Task 5: デバッグ削除エリアを来場者ワイプ + シード化に配線する

**Files:**
- Modify: `Assets/Scripts/Presentation/Controllers/EmbeddingObjectMatcher.cs`（`#region Debug: wipe registered data` 相当のコード生成部分 `EnsureDebugWipeButton` / `ConfirmDebugWipe`）
- Test: `Assets/Tests/PlayMode/EmbeddingObjectMatcherFlowTests.cs`（スモークテストを 1 本追加）

**Interfaces:**
- Consumes: `DatabaseManager.ClearVisitorRegistrations()` / `MarkAllObjectsAsSeed()`（Task 2）
- Produces: 画面左上のデバッグエリアに「来場者データ消去」ボタン（`ClearVisitorRegistrations` + `LoadRegisteredObjects`）と「今の登録をシード化」ボタン（`MarkAllObjectsAsSeed` + `LoadRegisteredObjects`）

- [ ] **Step 1: 失敗するテストを書く**

`EmbeddingObjectMatcherFlowTests.cs` の既存テストクラス内に追加（`EmbeddingObjectMatcher` のインスタンス生成は既存テストのヘルパーに合わせる。ヘルパーが無い場合は `new GameObject().AddComponent<EmbeddingObjectMatcher>()` で生成し、`objectMatcherRootPanel` にダミー `GameObject` を割り当ててから 1 フレーム進める）:

```csharp
        [UnityTest]
        public IEnumerator DebugArea_HasVisitorWipeAndSeedButtons()
        {
            var host = new GameObject("Matcher");
            var rootPanel = new GameObject("RootPanel");
            var matcher = host.AddComponent<EmbeddingObjectMatcher>();
            typeof(EmbeddingObjectMatcher)
                .GetField("objectMatcherRootPanel", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .SetValue(matcher, rootPanel);

            // Update() が遅延生成する
            yield return null;
            yield return null;

            var labels = rootPanel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
            var texts = new System.Collections.Generic.List<string>();
            foreach (var label in labels)
            {
                texts.Add(label.text);
            }

            Assert.That(texts, Has.Some.Contains("来場者データ消去"));
            Assert.That(texts, Has.Some.Contains("シード化"));

            UnityEngine.Object.Destroy(host);
            UnityEngine.Object.Destroy(rootPanel);
        }
```

- [ ] **Step 2: テストが失敗することを確認する**

Unity Test Runner（PlayMode）で `DebugArea_HasVisitorWipeAndSeedButtons` を実行する。
Expected: FAIL（現状のラベルは「登録データ消去」で「来場者データ消去」「シード化」が無い）。

- [ ] **Step 3: 実装する**

`EmbeddingObjectMatcher.cs` の `EnsureDebugWipeButton()` で、最初のボタンのラベルを `"登録データ消去"` から `"来場者データ消去"` に変える。確認ラベルを `"登録した物体をすべて消します。よろしいですか？"` から `"来場者が登録した物体を消します（展示用は残します）。よろしいですか？"` に変える。

同メソッド内、`_debugWipeConfirmGo.SetActive(false);` の直前に「シード化」ボタンを足す:

```csharp
        CreateDebugWipeButton(
            rootRect, "今の登録をシード化", new Color(0.15f, 0.35f, 0.55f, 0.92f),
            new Vector2(320f, 88f), new Vector2(0f, -96f), OnSeedAllRequested);
```

`ConfirmDebugWipe()` の `dbManager.ClearRegistrationData();` を `dbManager.ClearVisitorRegistrations();` に変える。ログ文言を「来場者登録を消去しました。」に合わせる。

`ConfirmDebugWipe()` の直後にハンドラを足す:

```csharp
    private void OnSeedAllRequested()
    {
        if (dbManager == null)
        {
            SetStatusText("DBに接続できていないためシード化できません。");
            return;
        }

        try
        {
            dbManager.MarkAllObjectsAsSeed();
        }
        catch (Exception e)
        {
            SetStatusText($"シード化に失敗しました: {e.Message}");
            Debug.LogError($"Seed-all failed: {e}");
            return;
        }

        LoadRegisteredObjects();
        SetStatusText("今の登録をすべてシード（削除対象外）にしました。");
    }
```

- [ ] **Step 4: テストが通ることを確認する**

Unity Test Runner（PlayMode）で `DebugArea_HasVisitorWipeAndSeedButtons` を実行する。
Expected: PASS。

- [ ] **Step 5: 既存のナビゲーションテストが壊れていないことを確認する**

Unity Test Runner（PlayMode）で `ObjectDetectionSceneNavigationTests` と `EmbeddingObjectMatcherFlowTests` を実行する。
Expected: 全 PASS。

- [ ] **Step 6: コミットする**

```bash
git add Assets/Scripts/Presentation/Controllers/EmbeddingObjectMatcher.cs Assets/Tests/PlayMode/EmbeddingObjectMatcherFlowTests.cs
git commit -m "feat: wire debug area to visitor wipe and seed marking"
```

---

### Task 6: シーン統合と展示設定

**Files:**
- Modify: `Assets/Scenes/ObjectDetectionScene.unity`（Unity エディタでの手作業）
- Test: `Assets/Tests/PlayMode/Exhibition/ObjectDetectionSceneExhibitionTests.cs`

**Interfaces:**
- Consumes: `ExhibitionSessionController`（Task 4）、`TalkConfiguration`（既存）
- Produces: `ObjectDetectionScene` に配置済みの `ExhibitionSessionController` とアトラクト画面

- [ ] **Step 1: 失敗するテストを書く**

`Assets/Tests/PlayMode/Exhibition/ObjectDetectionSceneExhibitionTests.cs`:

```csharp
using System.Collections;
using NUnit.Framework;
using ObjecTalk.Presentation.Exhibition;
using ObjecTalk.Presentation.Talk;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ObjecTalk.Tests.PlayMode.Exhibition
{
    public sealed class ObjectDetectionSceneExhibitionTests
    {
        [UnityTest]
        public IEnumerator Scene_HasExhibitionSessionController_AndPushToTalkFixed()
        {
            yield return SceneManager.LoadSceneAsync("ObjectDetectionScene", LoadSceneMode.Single);
            yield return null;

            var controller = Object.FindAnyObjectByType<ExhibitionSessionController>(FindObjectsInactive.Include);
            Assert.That(controller, Is.Not.Null, "ExhibitionSessionController がシーンに無い");

            var config = Object.FindAnyObjectByType<TalkConfiguration>(FindObjectsInactive.Include);
            Assert.That(config, Is.Not.Null);
            Assert.That(config.HandsFree, Is.False, "展示は push-to-talk 固定（handsFree=false）");
            Assert.That(config.ExhibitionMode, Is.True, "exhibitionMode=true");
        }
    }
}
```

`ObjectDetectionScene` を `Assets/Tests/PlayMode` の対象シーンに含める必要がある場合は、`ObjecTalk.PlayModeTests.asmdef` と同階層の設定、または Build Settings のシーンリストを確認する（`ObjectDetectionSceneNavigationTests` が既に同シーンを読んでいるので、その方式に合わせる）。

- [ ] **Step 2: テストが失敗することを確認する**

Unity Test Runner（PlayMode）で `Scene_HasExhibitionSessionController_AndPushToTalkFixed` を実行する。
Expected: FAIL（`ExhibitionSessionController` 不在、または `HandsFree` が true）。

- [ ] **Step 3: シーンを編集する（Unity エディタ）**

1. `Assets/Scenes/ObjectDetectionScene.unity` を開く。
2. 空の GameObject `ExhibitionSessionController` を作り、`ExhibitionSessionController` コンポーネントを付ける。
3. アトラクト画面を作る: メインの Canvas 配下に全画面パネル `AttractOverlay`（不透明背景 + 「がめんにタッチしてはじめよう」 + キャラのループアニメは後日）。初期状態は非アクティブでよい（`Evaluate` が起動直後に `Attract` を返すので Update で自動表示される）。
4. `ExhibitionSessionController` の `attractOverlay` に `AttractOverlay` を割り当てる。`talkController` にシーンの `TalkController` を割り当てる（未設定でも `Awake` が探すが、明示する）。
5. `AttractOverlay` にフルスクリーンの透明 `Button`（または `EventTrigger`）を置き、`OnClick` を `ExhibitionSessionController.NotifyActivity` に配線する。タッチでアトラクトが閉じる。
6. シーンの `TalkConfiguration` を選び、`handsFree` のチェックを外す（push-to-talk 固定）、`exhibitionMode` にチェックを入れる、`sessionMaxSeconds` に上限（例 180）を入れる。
7. シーンを保存する。

- [ ] **Step 4: テストが通ることを確認する**

Unity Test Runner（PlayMode）で `Scene_HasExhibitionSessionController_AndPushToTalkFixed` を実行する。
Expected: PASS。

- [ ] **Step 5: 手動確認（Play モード）**

1. `ObjectDetectionScene` を Play する。数十秒放置するとアトラクト画面が出る。
2. 画面をクリックするとアトラクトが閉じる。
3. アトラクトが出た状態でさらに放置すると、シーンが再ロードされ登録・会話状態がクリアされる。
4. 物体を登録 → 会話中は放置してもアトラクト・リセットが起きない。
5. デバッグエリアの「今の登録をシード化」→「来場者データ消去」で、シード物体だけが残る。

- [ ] **Step 6: コミットする**

```bash
git add Assets/Scenes/ObjectDetectionScene.unity Assets/Tests/PlayMode/Exhibition/ObjectDetectionSceneExhibitionTests.cs
git commit -m "feat: integrate ExhibitionSessionController into ObjectDetectionScene"
```

---

## Self-Review

**Spec coverage（この計画の担当範囲）**

| 設計書の項目 | 対応タスク |
|---|---|
| 無操作タイムアウト → アトラクト → シーン再ロード | Task 1, 4, 6 |
| 会話中はタイムアウトを停止 | Task 1（`SetConversationActive`）、Task 3、Task 4 |
| アトラクト画面 | Task 6 |
| `is_seed` スキーマ + 非シード削除 | Task 2 |
| シード物体の保護（一括削除からの除外） | Task 2, 5 |
| 既存の一括削除ボタンを来場者ワイプへ切り替え | Task 5 |
| push-to-talk 固定 / exhibitionMode | Task 6 |
| セッション時間上限（既存 `sessionMaxSeconds`） | Task 6（設定のみ。コード変更なし） |

範囲外（別計画）: 登録フロー簡素化（A2）、カテゴリ別プロンプト・提案チップ・ツール層・ネット断フォールバック（B/C）。

**Placeholder scan:** 各ステップに実コードあり。Task 6 Step 3 のみ Unity エディタ手作業（シーン `.unity` はテキスト編集に不向きなため）。手順は番号付きで具体化済み。

**Type consistency:** `ExhibitionIdlePolicy` / `ExhibitionIdleState`（Task 1）→ Task 4 で同名参照。`IsConversationActive`（Task 3）→ Task 4 で参照。`ClearVisitorRegistrations` / `MarkAllObjectsAsSeed`（Task 2）→ Task 5 で参照。`ObjectItem.IsSeed`（Task 2）→ Task 2 のテストと `ClearVisitorRegistrations` で参照。名前の不一致なし。

**既知の要検証点（実装時に確認）**

- sqlite-net の `CreateTable<ObjectItem>()` が既存 DB へ `is_seed` 列を自動追加し、既存行が非シード（0/NULL→false）として読めること（Task 2 Step 6 で担保）。
- `ObjecTalk.PlayModeTests` から Assembly-CSharp の `EmbeddingObjectMatcher` を参照できること（`EmbeddingObjectMatcherFlowTests` が既に参照しているはず。できなければ Task 5 のテストは `ObjectDetectionScene` 経由のシーンテストに変更する）。
- `ObjectDetectionScene` が PlayMode テストから読み込めること（`ObjectDetectionSceneNavigationTests` に倣う）。
