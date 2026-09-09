# 外部ツール（天気・Web検索）設計（計画 C）

## 目的

認識した物体とユーザーの質問に応じて、LLM が自動で天気・Web検索ツールを呼び、その結果をもとに音声で答える。ユーザーは「検索して」などと指示しない。

例: 傘を認識 →「今日この傘いる？」→ 天気ツール →「夕方から雨だから持っていったほうがいいよ」。PC を認識 →「今日の AI ニュース教えて」→ Web検索 → 要約。

親スペック: `docs/superpowers/specs/2026-09-10-open-campus-exhibition-design.md`（「バックエンドのツール層」節）。方式決定: `docs/superpowers/specs/2026-09-10-tool-transport-spike.md` → **function ツール方式**（クライアント処理）。

## 確定要件

- 方式: Realtime API の `type: "function"` ツール。`session.tools` に登録し、モデルが `function_call` を出したらクライアントが実行して `function_call_output` を返す。
- v1 のツールは2つ。GitHub / Maps / カレンダーは対象外。
  - `get_weather({ location, date? })` — Open-Meteo（APIキー不要）。`location` は地名文字列、`date` は `today` / `tomorrow` / `YYYY-MM-DD`（省略時 today）。
  - `web_search({ query })` — OpenAI Responses API（`tools:[{type:"web_search"}]`, `gpt-4o-mini`）。追加プロバイダなし。
- ツール実行は**バックエンド経由**（`POST /api/tools/invoke`）。クライアントは引数を転送するだけで、OpenAI キーも外部APIもクライアントに置かない。
- `tool_choice: "auto"`。承認フローなし。
- モデルはツールを使う前に「ちょっと調べるね」等と一言添える（プロンプト指示）。クライアントもツール実行中はインジケータを出す。
- ツール失敗時は、モデルが自然に回復できるエラー文字列を `function_call_output` に返す（例外を投げて会話を落とさない）。
- ツール呼び出しに上限（1応答あたり、1セッションあたり）とタイムアウト（既定 8 秒）。
- 会話内容・ツール結果を保存しない（既存方針）。ツールのクエリ・地名はログに残さない。

## 現状

- `createRealtimeCall.js` は `session` に `model` / `output_modalities` / `instructions` / `turn_detection` / `voice` を渡すのみ。`tools` なし。
- `RealtimeEventParser` は固定イベント種別のみ。`RealtimeWebRtcTransport` はデータチャネル受信 + `response.cancel` 送信のみ。
- `RealtimeCallClient` が Firebase トークン + バックエンド HTTP の手順を持つ（`IIdTokenProvider` / `IRealtimeHttpClient`）。
- `app.js` は `/health` と `/api/realtime/calls` のみ。`createInMemoryLimiter` / `createDailyBudget` あり。

## コンポーネント

### バックエンド

**`src/tools/registry.js`（新規）**

```js
export const toolDefinitions = [ /* get_weather, web_search の function スキーマ */ ];
export const toolNames = new Set(['get_weather', 'web_search']);
```

`toolDefinitions` は `createRealtimeCall` がそのまま `session.tools` に入れる形。

**`src/tools/weather.js`（新規）**

```js
export function createWeatherTool({ fetchImpl = fetch }) {
  return async function getWeather({ location, date }) { /* returns a short JP summary string */ };
}
```

Open-Meteo geocoding（`https://geocoding-api.open-meteo.com/v1/search?name=...`）で緯度経度 → forecast（`https://api.open-meteo.com/v1/forecast?...&daily=precipitation_probability_max,temperature_2m_max,temperature_2m_min,weathercode`）。`date` を today/tomorrow/ISO で解決。結果は「9月11日の鈴鹿市: くもり時々雨、最高28度、降水確率60%」のような1〜2文。地名が引けなければ「その場所が見つからなかった」。

**`src/tools/webSearch.js`（新規）**

```js
export function createWebSearchTool({ apiKey, model = 'gpt-4o-mini', fetchImpl = fetch }) {
  return async function webSearch({ query }) { /* returns a short JP summary string */ };
}
```

`POST https://api.openai.com/v1/responses`、body `{ model, tools:[{type:'web_search'}], input: <query に日本語で簡潔にまとめる指示を添えたもの> }`。レスポンスの `output_text` を返す。空なら「うまく調べられなかった」。

**`src/tools/invoke.js`（新規）**

```js
export function createToolInvoker({ getWeather, webSearch, timeoutMs = 8000 }) {
  return async function invokeTool(name, args) { /* dispatch + timeout, returns string, throws only on unknown tool */ };
}
```

`name` が `toolNames` にない → `TypeError`。各ツールの例外は捕捉して「調べられなかった（理由）」の文字列にして返す（会話を止めない）。`args` が JSON 文字列なら parse、オブジェクトならそのまま。タイムアウトで「時間内に調べられなかった」。

**`src/app.js`（拡張）**

`POST /api/tools/invoke` — Firebase 認証（`/api/realtime/calls` と同じ）、`limiter`、body `{ name: string(1-40), arguments: string|object }`。`invokeTool(name, arguments)` → `{ output: string }` を返す。`TypeError`/`RangeError` → 400、その他 → 502。`arguments` はログに出さない。

**`src/realtime/createRealtimeCall.js`（拡張）**

`session` に `tools: toolDefinitions`、`tool_choice: 'auto'` を追加。

**`src/conversation/buildInstructions.js`（拡張）**

固定の安全ルールに1行追加: 「天気や最新の情報が必要なときは、まず『ちょっと調べるね』と一言添えてから道具（ツール）を使い、その結果だけをもとに答えてください。ツールの結果が得られなければ正直にそう伝えてください。」

**`src/config/environment.js`（拡張）**

`webSearchModel`（既定 `gpt-4o-mini`、`MODEL_PATTERN`）、`toolInvokeTimeoutMs`（既定 8000）。`openAiApiKey` は既存を流用。

### クライアント

**`RealtimeDtos` / `RealtimeEventParser`（拡張）**

- `FunctionCallRequest { string Name; string CallId; string Arguments; }`（新規 DTO）。
- `RealtimeEventParser.TryParseFunctionCall(string json, out FunctionCallRequest request)` — `response.done` の `response.output[]` を走査し、`type == "function_call"` の item から `name` / `call_id` / `arguments` を取る。無ければ false。
- 既存 `TryParse` は変更しない。

**`IRealtimeConversationTransport`（拡張）** — `OpenAIRealtimeConversationService.cs` 内のインターフェース

```csharp
event Action<FunctionCallRequest> FunctionCallReceived;
Task SendFunctionCallOutputAsync(string callId, string output, CancellationToken cancellationToken);
```

`RealtimeWebRtcTransport` 実装:
- `OnDataChannelMessage` で `TryParseFunctionCall` も試し、取れたら `FunctionCallReceived` を発火。
- `SendFunctionCallOutputAsync` — データチャネルへ
  `{"type":"conversation.item.create","item":{"type":"function_call_output","call_id":<id>,"output":<output>}}`
  続けて `{"type":"response.create"}`。未接続なら例外。

**`IToolInvoker`（新規、`ObjecTalk.Core.Conversation`）**

```csharp
Task<string> InvokeAsync(string name, string argumentsJson, CancellationToken cancellationToken);
```

**`BackendToolInvoker`（新規、`ObjecTalk.Infrastructure.Conversation.OpenAI`）**

`RealtimeCallClient` と同じ `IIdTokenProvider` + `IRealtimeHttpClient` を使い、`POST <backendBaseUrl>/api/tools/invoke`、Firebase bearer、body `{name, arguments: argumentsJson}`、レスポンス `{output}` を返す。失敗時は短い日本語エラー文字列を返す（例外は投げない）。

**`OpenAIRealtimeConversationService`（拡張）**

- ctor に `IToolInvoker toolInvoker`（null 可 = ツール無効）。
- `transport.FunctionCallReceived` を購読。受信で:
  1. `ToolActivityChanged?.Invoke(true)` を publish。
  2. `toolInvoker.InvokeAsync(name, arguments, token)` を実行（別 Task、セッション世代を確認）。
  3. 完了で `transport.SendFunctionCallOutputAsync(callId, result, token)`。
  4. `ToolActivityChanged?.Invoke(false)`。
- `event Action<bool> ToolActivityChanged` を `IVoiceConversationService` に公開。
- 1応答あたり・1セッションあたりのツール呼び出し上限を超えたら、その旨のエラー文字列を出力に返す。
- バージ・イン／切断中に来た function_call は無視（世代チェック）。

**`TalkController` / `TalkView`（拡張）**

- `voiceService.ToolActivityChanged += active => view.ShowToolActivity(active)`。
- `TalkView.ShowToolActivity(bool active)` — active 中は status を「調べているよ…」に。非 active で通常表示へ戻す。`FaceOverlayAnimator` は現状のまま（口パクは音声ソース由来なので、無音中は自然に閉じる）。

**`TalkController.ComposeRuntimeGraph`（配線）**

`BackendToolInvoker` を生成して `OpenAIRealtimeConversationService` に渡す。`configuration` に `ToolsEnabled`（既定 true）を足し、false なら null を渡す。

## データフロー

1. ユーザー発話 → モデルが `get_weather` を決める → 「ちょっと調べるね」を発話 → `response.done` に `function_call` item。
2. `RealtimeWebRtcTransport` が `FunctionCallReceived` 発火 → `OpenAIRealtimeConversationService` が `ToolActivityChanged(true)` → `TalkView` が「調べているよ…」。
3. `BackendToolInvoker` → `POST /api/tools/invoke` → バックエンドが Open-Meteo を叩く → 要約文字列。
4. `transport.SendFunctionCallOutputAsync(callId, summary)` → `response.create`。
5. モデルが結果をもとに音声で回答 → `ToolActivityChanged(false)`。

## テスト戦略

### バックエンド（`node --test`）

- `weather.test.js` — geocoding + forecast の fetch をモック。today/tomorrow/ISO の日付解決、地名不明、fetch 失敗時のメッセージ。
- `webSearch.test.js` — Responses API fetch をモック。`output_text` 抽出、空レスポンス、失敗。
- `invoke.test.js` — 既知ツールへディスパッチ、未知ツールで `TypeError`、ツール例外→文字列化、タイムアウト→文字列、JSON 文字列 args の parse。
- `registry.test.js` — `toolDefinitions` が function スキーマの形（name/description/parameters）、`toolNames` と一致。
- `createRealtimeCall.test.js`（追加）— `session.tools` に2つ入る、`tool_choice: 'auto'`。
- `buildInstructions.test.js`（追加）— ツール narration の行が安全ルール内に入る。
- `app.test.js`（追加）— `/api/tools/invoke` が認証必須、`invokeTool` に届く、未知ツールで 400、`arguments` がレスポンス・ログに漏れない、レート制限。

### Unity EditMode

- `RealtimeEventParserTests`（追加）— `response.done` の `function_call` item から `TryParseFunctionCall` が name/call_id/arguments を取る。function_call が無ければ false。複数 output のうち function_call を拾う。
- `BackendToolInvokerTests` — 正しい URL / bearer / body、`{output}` を返す、非200・不正JSONで短い日本語文字列。
- `RealtimeDtos`/`FunctionCallRequest` の生成。

### Unity PlayMode

- `RealtimeWebRtcTransport`（`TalkLifecycleTests` 系）— function_call のデータチャネルメッセージで `FunctionCallReceived` 発火、`SendFunctionCallOutputAsync` が正しい2メッセージを順に送る、未接続で例外。
- `OpenAIRealtimeConversationService`（`OpenAIRealtimeConversationServiceTests` 系）— fake transport + fake `IToolInvoker` で: function_call 受信 → invoker 呼び出し → output 送信、`ToolActivityChanged` true→false、invoker 例外時もエラー文字列を送って会話継続、世代不一致の function_call を無視、1応答あたり上限。
- `TalkController` / `TalkView` — `ToolActivityChanged(true)` で status が「調べているよ…」、false で戻る。

## 完了条件

- `session.tools` に `get_weather` / `web_search` が入り、`tool_choice: auto`。
- モデルの `function_call` をクライアントが実行し、`function_call_output` + `response.create` を送る。
- 天気ツールが Open-Meteo から実データを返し、モデルがそれをもとに答える。
- Web検索ツールが OpenAI Responses API 経由で結果を返す。
- ツール失敗・タイムアウトで会話が落ちず、モデルが「調べられなかった」と言える。
- ツール実行中に「調べているよ…」が出る。
- `arguments`（地名・クエリ）がログに残らない。
- バックエンド `node --test`、Unity EditMode / PlayMode 全通過。
- 実機（gcloud デプロイ済みバックエンド）で傘→天気、PC→ニュースが動く。

## 対象外

- GitHub / Google Maps / カレンダー / 商品検索 / YouTube。
- 物体ごとの記憶、複数物体の関係理解、リアルタイム混雑データ。
- ツール結果のキャッシュ、会話履歴保存。
- リモート MCP 方式（スパイクで不採用）。
