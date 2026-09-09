# 外部ツール（天気・Web検索）実装計画（計画 C）

> **For agentic workers:** superpowers:executing-plans で1タスクずつ TDD 実装。

**Goal:** Realtime の function ツールで `get_weather` / `web_search` を自動選択させ、結果を音声で返す。実行はバックエンド `POST /api/tools/invoke` 経由。

**Architecture:** バックエンドに `src/tools/`（weather=Open-Meteo、web_search=OpenAI Responses API、invoke ディスパッチ、registry スキーマ）+ `/api/tools/invoke` ルート。`createRealtimeCall` が `session.tools` を渡す。クライアントは `response.done` の `function_call` をパースし、`BackendToolInvoker` で実行して `function_call_output` + `response.create` を返す。実行中は「調べているよ…」表示。

**Tech Stack:** バックエンド Node 20 + Express 5 + `node --test`。クライアント Unity 6000.1、C#、NUnit。

**Spec:** `docs/superpowers/specs/2026-09-10-external-tools-design.md`

## Global Constraints

- ツールは `get_weather({location, date?})` と `web_search({query})` の2つのみ。`tool_choice: "auto"`、承認なし。
- ツール実行はバックエンド経由。OpenAI キー・外部APIをクライアントに置かない。
- ツール失敗・タイムアウト（8秒）で例外を投げず、モデルが回復できる日本語文字列を `function_call_output` に返す。
- `arguments`（地名・クエリ）をログ・レスポンスの想定外箇所に出さない。
- 会話内容・ツール結果を保存しない。
- 後方互換: ツール未設定（`toolInvoker == null` / `toolDefinitions` 空）でも従来どおり動く。
- Open-Meteo エンドポイント: geocoding `https://geocoding-api.open-meteo.com/v1/search`、forecast `https://api.open-meteo.com/v1/forecast`。OpenAI Responses: `https://api.openai.com/v1/responses`。

---

### Task 1: `src/tools/weather.js`

**Files:** Create `Backend/src/tools/weather.js`, Test `Backend/test/weather.test.js`

**Interfaces:** `createWeatherTool({ fetchImpl = fetch })` → `async ({ location, date }) => string`。
- `date`: 未指定/`"today"` → 今日、`"tomorrow"` → 明日、`/^\d{4}-\d{2}-\d{2}$/` → その日（forecast 範囲外なら today にフォールバックし一言添える）。それ以外は today 扱い。
- geocoding で1件目の lat/lon/name を取る。0件 → 「『<location>』の天気は見つからなかったよ」。
- forecast: `daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max`、`timezone=auto`、`forecast_days=3`。
- 返り値: `「<name> の <M月D日>: <天候>、最高<max>度・最低<min>度、降水確率<p>%」`。weather_code → 日本語天候の小さいマップ。
- `fetchImpl` が投げる/非 ok → 「天気を調べられなかったよ」。

- [ ] Step 1: `weather.test.js`（失敗）— geocoding/forecast の `fetchImpl` を URL で振り分けるモック。today/tomorrow/ISO、0件、fetch 失敗、weather_code マッピング。
- [ ] Step 2: `node --test test/weather.test.js` FAIL。
- [ ] Step 3: 実装。
- [ ] Step 4: PASS。
- [ ] Step 5: commit `feat(backend): add get_weather tool (Open-Meteo)`

---

### Task 2: `src/tools/webSearch.js`

**Files:** Create `Backend/src/tools/webSearch.js`, Test `Backend/test/webSearch.test.js`

**Interfaces:** `createWebSearchTool({ apiKey, model = 'gpt-4o-mini', fetchImpl = fetch })` → `async ({ query }) => string`。
- `POST https://api.openai.com/v1/responses`、headers `Authorization: Bearer <apiKey>`、body `{ model, tools: [{ type: 'web_search' }], input: "次の質問に、日本語で2〜3文で簡潔に答えて。最新情報は調べて。: " + query }`。
- レスポンスから `output_text`（あれば）または `output[]` の `content[].text` を連結。空 → 「うまく調べられなかったよ」。
- 非 ok / throw → 「調べているときに問題が起きたよ」。`query` はログに出さない。

- [ ] Step 1: `webSearch.test.js`（失敗）— fetch モック。`output_text` あり／`output[]` 形式／空／非200。リクエスト body に `web_search` ツールと query が入る。
- [ ] Step 2: FAIL。
- [ ] Step 3: 実装。
- [ ] Step 4: PASS。
- [ ] Step 5: commit `feat(backend): add web_search tool (OpenAI Responses API)`

---

### Task 3: `src/tools/invoke.js` + `src/tools/registry.js`

**Files:** Create `Backend/src/tools/invoke.js`, `Backend/src/tools/registry.js`, Test `Backend/test/invoke.test.js`, `Backend/test/registry.test.js`

**Interfaces:**
- `registry.js`: `export const toolDefinitions = [{type:'function', name:'get_weather', description, parameters:{type:'object', properties:{location:{type:'string'}, date:{type:'string'}}, required:['location']}}, {type:'function', name:'web_search', ... required:['query']}]`; `export const toolNames = new Set(toolDefinitions.map(t => t.name))`.
- `invoke.js`: `createToolInvoker({ getWeather, webSearch, timeoutMs = 8000 })` → `async invokeTool(name, args) => string`.
  - `name` が `toolNames` にない → `TypeError('unknown tool')`.
  - `args` が文字列なら `JSON.parse`（失敗で `TypeError`）、オブジェクトならそのまま、null/undefined → `{}`。
  - `Promise.race` でタイムアウト → `'時間内に調べられなかったよ'`。
  - ツールが throw → `'調べられなかったよ'`（詳細はログのみ、メッセージには出さない）。

- [ ] Step 1: `registry.test.js` + `invoke.test.js`（失敗）。
- [ ] Step 2: FAIL。
- [ ] Step 3: 実装。
- [ ] Step 4: `node --test test/invoke.test.js test/registry.test.js` PASS。
- [ ] Step 5: commit `feat(backend): tool registry and invoke dispatch`

---

### Task 4: `POST /api/tools/invoke` ルート

**Files:** Modify `Backend/src/app.js`, Test `Backend/test/app.test.js`（追加）

**Interfaces:** `createApp` の deps に `invokeTool`（省略時はツール無効＝ルート未登録 or 404）。`POST /api/tools/invoke`:
- Authorization ヘッダ1個・Firebase 検証（`/api/realtime/calls` と同じヘルパ経由）。
- `limiter(uid, ip)` を適用。
- body `{ name: string(1-40), arguments: string|object|undefined }`。型不正 → 400。
- `invokeTool(name, arguments)` → `res.json({ output })`。`TypeError`/`RangeError` → 400、他 → 502。
- ログに `arguments` / `output` を出さない。

- [ ] Step 1: `app.test.js` 追加（失敗）— 認証必須、`invokeTool` に name/args が届く、未知ツール（invokeTool が TypeError）で 400、`output` が返る、`arguments` がレスポンス他所やログに出ない、`invokeTool` 無しでルートが 404。
- [ ] Step 2: FAIL。
- [ ] Step 3: 実装。
- [ ] Step 4: `node --test` 全 PASS。
- [ ] Step 5: commit `feat(backend): POST /api/tools/invoke`

---

### Task 5: `createRealtimeCall` に `session.tools` + `buildInstructions` に narration

**Files:** Modify `Backend/src/realtime/createRealtimeCall.js`, `Backend/src/conversation/buildInstructions.js`, Tests 追加

**Interfaces:**
- `createOpenAIRealtimeCall({ ..., tools = [] })` → `session` に `tools` が非空なら `tools` と `tool_choice: 'auto'` を含める。
- `buildInstructions`: 安全ルール群に1行追加（spec の文言）。

- [ ] Step 1: `createRealtimeCall.test.js` + `buildInstructions.test.js` 追加（失敗）— `session.tools` に渡した配列、`tool_choice:'auto'`、空 tools では両方付かない。narration 行が安全ルール内。
- [ ] Step 2: FAIL。
- [ ] Step 3: 実装。
- [ ] Step 4: `node --test` 全 PASS。
- [ ] Step 5: commit `feat(backend): register tools on the realtime session`

---

### Task 6: バックエンド配線（`environment.js` / `server.js` / `app.js` deps）

**Files:** Modify `Backend/src/config/environment.js`, `Backend/src/server.js`, Tests 追加

**Interfaces:**
- `environment.js`: `webSearchModel`（既定 `gpt-4o-mini`、`MODEL_PATTERN`）、`toolInvokeTimeoutMs`（既定 8000、正の整数）。
- `server.js`: `createWeatherTool` / `createWebSearchTool({apiKey, model: webSearchModel})` / `createToolInvoker({..., timeoutMs})` を組み、`createApp({..., invokeTool})` と `createRealtimeCall({..., tools: toolDefinitions})` に渡す。

- [ ] Step 1: `environment.test.js` 追加（失敗）。
- [ ] Step 2: FAIL。
- [ ] Step 3: 実装（`server.js` は薄い配線、`app.test.js` の統合で担保）。
- [ ] Step 4: `node --test` 全 PASS。
- [ ] Step 5: commit `chore(backend): wire the tool layer into the server`

---

### Task 7: `FunctionCallRequest` + `RealtimeEventParser.TryParseFunctionCall`

**Files:** Modify `Assets/Scripts/Infrastructure/Conversation/OpenAI/RealtimeDtos.cs`, `RealtimeEventParser.cs`, Test `Assets/Tests/EditMode/Conversation/RealtimeEventParserTests.cs`（追加）

**Interfaces:**
- `public sealed class FunctionCallRequest { string Name; string CallId; string Arguments; }`（`RealtimeDtos.cs`）。
- `RealtimeEventParser.TryParseFunctionCall(string json, out FunctionCallRequest request)` — `type == "response.done"` のとき `["response"]["output"]` 配列を走査し `["type"] == "function_call"` の最初の item から `name` / `call_id` / `arguments`（文字列、無ければ `"{}"`）。無ければ false。

- [ ] Step 1: `RealtimeEventParserTests` 追加（失敗）— function_call を含む response.done、含まない response.done（false）、複数 output で function_call を拾う、不正 JSON（false）。
- [ ] Step 2: EditMode 該当フィルタで FAIL。
- [ ] Step 3: 実装。
- [ ] Step 4: PASS。既存 `RealtimeEventParserTests` も回す。
- [ ] Step 5: commit `feat: parse function_call from response.done`

---

### Task 8: transport に `FunctionCallReceived` + `SendFunctionCallOutputAsync`

**Files:** Modify `Assets/Scripts/Infrastructure/Conversation/OpenAI/OpenAIRealtimeConversationService.cs`（`IRealtimeConversationTransport`）, `RealtimeWebRtcTransport.cs`, Test `Assets/Tests/PlayMode/Conversation/TalkLifecycleTests.cs`（追加）

**Interfaces:**
- `IRealtimeConversationTransport`: `event Action<FunctionCallRequest> FunctionCallReceived;` / `Task SendFunctionCallOutputAsync(string callId, string output, CancellationToken cancellationToken);`
- `RealtimeWebRtcTransport`:
  - `OnDataChannelMessage`: 既存 `TryParse` に加え `TryParseFunctionCall` を試し、取れたら `FunctionCallReceived?.Invoke(request)`。
  - `SendFunctionCallOutputAsync`: 接続中なら `DataChannel.Send` を2回（`conversation.item.create` の function_call_output、続けて `response.create`）。未接続なら `InvalidOperationException`。JSON は `Newtonsoft.Json` で組む（`output` のエスケープ）。

- [ ] Step 1: `TalkLifecycleTests` 追加（失敗）— fake data channel に function_call の response.done を流すと `FunctionCallReceived` が name/call_id/arguments 付きで発火。`SendFunctionCallOutputAsync` が2メッセージを正しい順・正しい JSON で送る。未接続で例外。
- [ ] Step 2: PlayMode 該当で FAIL。
- [ ] Step 3: 実装。
- [ ] Step 4: PASS。
- [ ] Step 5: commit `feat: transport function-call receive and output send`

---

### Task 9: `IToolInvoker` + `BackendToolInvoker`

**Files:** Create `Assets/Scripts/Core/Conversation/IToolInvoker.cs`, `Assets/Scripts/Infrastructure/Conversation/OpenAI/BackendToolInvoker.cs`, Test `Assets/Tests/EditMode/Conversation/BackendToolInvokerTests.cs`

**Interfaces:**
- `ObjecTalk.Core.Conversation.IToolInvoker`: `Task<string> InvokeAsync(string name, string argumentsJson, CancellationToken cancellationToken)`.
- `BackendToolInvoker(string backendBaseUrl, IIdTokenProvider tokenProvider, IRealtimeHttpClient httpClient, TimeSpan? requestTimeout = null)` — `RealtimeCallClient` に倣う。`POST <base>/api/tools/invoke`、`Authorization: Bearer <firebaseToken>`、body `{"name":<name>,"arguments":<argumentsJson>}`（`arguments` は文字列そのまま）。レスポンス `{"output": "..."}` の `output` を返す。非200 / 不正 JSON / タイムアウト / トークン失敗 → 短い日本語（例「うまく調べられなかったよ」）。**例外は投げない。**

- [ ] Step 1: `BackendToolInvokerTests`（失敗）— 正しい URL/method/bearer/body、`output` 抽出、非200・不正JSON・トークン失敗で日本語文字列（throw しない）、`argumentsJson` がそのまま body に入る。
- [ ] Step 2: FAIL。
- [ ] Step 3: 実装。
- [ ] Step 4: PASS。
- [ ] Step 5: commit `feat: BackendToolInvoker calls /api/tools/invoke`

---

### Task 10: `OpenAIRealtimeConversationService` のツール実行

**Files:** Modify `OpenAIRealtimeConversationService.cs`, `Assets/Scripts/Core/Conversation/IVoiceConversationService.cs`, Test `Assets/Tests/EditMode/Conversation/OpenAIRealtimeConversationServiceTests.cs`（追加）

**Interfaces:**
- ctor に `IToolInvoker toolInvoker = null`、`int maxToolCallsPerResponse = 3`。
- `IVoiceConversationService`: `event Action<bool> ToolActivityChanged;`
- `transport.FunctionCallReceived` 購読。受信で（アクティブ世代のみ）:
  1. 上限チェック（1応答あたり `maxToolCallsPerResponse`；`ResponseStarted`/`ResponseDone` でカウンタ管理）。超過なら `SendFunctionCallOutputAsync(callId, "これ以上は調べられないよ")`。
  2. `ToolActivityChanged(true)` publish。
  3. `toolInvoker.InvokeAsync(name, args, token)` を await（別 async）。null invoker なら `"道具が使えないみたい"`。
  4. `transport.SendFunctionCallOutputAsync(callId, result, token)`。
  5. `ToolActivityChanged(false)` publish。
- `toolInvoker` が例外を投げても（本来投げない設計だが保険）捕捉してエラー文字列を送る。
- バージ・イン／`FinishSession` で進行中のツールは破棄（世代チェック）。

- [ ] Step 1: `OpenAIRealtimeConversationServiceTests` 追加（失敗）— fake transport（`FunctionCallReceived` を発火できる）+ fake `IToolInvoker`。受信→invoker 呼び出し→`SendFunctionCallOutputAsync` に result、`ToolActivityChanged` true→false、null invoker、上限超過、世代不一致で無視。
- [ ] Step 2: FAIL。
- [ ] Step 3: 実装。
- [ ] Step 4: PASS。既存の service テストも全部回す。
- [ ] Step 5: commit `feat: run tool calls in the realtime conversation service`

---

### Task 11: `TalkView.ShowToolActivity` + `TalkController` 配線

**Files:** Modify `Assets/Scripts/Presentation/Talk/TalkView.cs`, `TalkController.cs`, `Assets/Scripts/Presentation/Talk/TalkConfiguration.cs`, Test `TalkLifecycleTests.cs`（追加）

**Interfaces:**
- `TalkView.ShowToolActivity(bool active)` — active 中は status を「調べているよ…」、非 active で `ShowState` の通常表示へ戻す（直近 state を保持しておく）。
- `TalkController`: `voiceService.ToolActivityChanged += view.ShowToolActivity`（購読/解除を `Subscribe`/`Unsubscribe` に追加）。
- `TalkConfiguration`: `[SerializeField] private bool toolsEnabled = true;` + `public bool ToolsEnabled`。
- `ComposeRuntimeGraph`: `toolsEnabled` なら `new BackendToolInvoker(configuration.ResolveBackendBaseUrl(), new FirebaseIdTokenProvider(), new UnityWebRequestRealtimeHttpClient())` を作り `OpenAIRealtimeConversationService` に渡す。false なら null。

- [ ] Step 1: `TalkLifecycleTests` 追加（失敗）— `FakeVoiceConversationService` に `ToolActivityChanged` を追加、fire すると status が「調べているよ…」→ false で戻る。
- [ ] Step 2: FAIL。
- [ ] Step 3: 実装。
- [ ] Step 4: PASS。
- [ ] Step 5: PlayMode 全体を回す。
- [ ] Step 6: commit `feat: show a "調べているよ" indicator during tool calls`

---

### Task 12: 全スイート + バックエンド最終確認

- [ ] Step 1: `Backend/` で `node --test` 全 PASS。
- [ ] Step 2: `uloop run-tests --test-mode EditMode` 全 PASS。
- [ ] Step 3: `uloop run-tests --test-mode PlayMode` 全 PASS。
- [ ] Step 4: `uloop compile` 0 エラー。
- [ ] Step 5: 差分の self-review（spec カバレッジ、`arguments` のログ漏れ、後方互換）。

---

### Task 13: 実機確認

- [ ] Step 1: `Builds/Android/DevBuild.BuildAndroidDev` で APK。gcloud の本番バックエンド URL を使うよう `TalkConfiguration`（`useDevelopmentBackend` を要確認）。
- [ ] Step 2: `adb install -r` → 起動。
- [ ] Step 3: 手動: 傘を認識 →「今日この傘いる？」→「調べているよ…」→ 天気をもとに回答。PC を認識 →「今日の AI ニュース教えて」→ Web検索 → 要約。
- [ ] Step 4: ツール失敗時（機内モード等）に会話が落ちないこと。
- [ ] Step 5: logcat に地名・クエリが出ていないこと。

---

## Self-Review

**Spec coverage:** weather(T1)/web_search(T2)/invoke+registry(T3)/route(T4)/session.tools+narration(T5)/wiring(T6)/parser(T7)/transport(T8)/invoker(T9)/service(T10)/UI(T11)。実機(T13)。

**Placeholder scan:** T6/T13 は薄い配線・手動確認。T8 の JSON 生成は `JObject`（`RealtimeCallClient` と同じ Newtonsoft）。

**型の一貫性:** `FunctionCallRequest`(T7)→T8,T10。`IToolInvoker`(T9)→T10,T11。`ToolActivityChanged`(T10)→T11。`toolDefinitions`(T3)→T5,T6。`invokeTool`(T3)→T4,T6。

**要検証:**
- Realtime の `function_call` が `response.done` の `response.output[]` に本当に `name`/`call_id`/`arguments` を揃えて出すか（実機 T13 で確認、ダメなら `response.output_item.done` / `response.function_call_arguments.done` + `name` の合成にフォールバック）。
- `IRealtimeConversationTransport` にメソッド追加で既存の fake 実装（`TalkLifecycleTests` 内）を更新。
- `OpenAIRealtimeConversationServiceTests` の fake transport が `FunctionCallReceived` を発火できるよう拡張。
