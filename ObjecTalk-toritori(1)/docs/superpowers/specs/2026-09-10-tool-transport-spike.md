# ツール通信方式のスパイク（計画 C の前提調査）

**問い:** Realtime API（`gpt-realtime` / `gpt-realtime-2.1`）で天気・Web検索ツールを LLM に自動選択させるとき、どう配線するのが最小コストか。

## 分かったこと（2026-08 GA の Realtime API）

Realtime API のツールは2方式。

### A. `type: "function"` ツール（クライアント処理）

- `session.tools` に `{type:"function", name, description, parameters}` を登録。
- モデルが呼ぶと `response.done` の出力に `type:"function_call"`（`name` / `arguments`(JSON文字列) / `call_id`）が入る。`response.function_call_arguments.delta` でストリーミングも取れる。
- クライアントが実行 → `conversation.item.create`（`{type:"function_call_output", call_id, output}`）→ `response.create` で続きを促す。
- **今のクライアントは対応していない。** `RealtimeWebRtcTransport` は固定イベントの受信 + `response.cancel` 送信のみ。`RealtimeEventParser` も固定種別のみ。約 100 行の追加が要る。
- 実行経路はクライアント → 自前バックエンド `/api/tools/invoke`。**バックエンドは今の到達性（adb reverse / 通常の backend URL）で足りる。**

### B. `type: "mcp"` リモート MCP（サーバー処理）

- `session.tools` に `{type:"mcp", server_label, server_url, allowed_tools, require_approval:"never", authorization}` を登録。
- **ツール実行は OpenAI 側が MCP サーバーを直接叩く。クライアントは何もしない**（`function_call_output` を返す必要なし）。
- **Unity クライアントの変更ゼロ。** 追加は `createRealtimeCall.js` に `session.tools` を1つ足すだけ。
- 制約: **MCP サーバーは OpenAI から到達可能な公開 URL が必要**（localhost 不可）。`require_approval:"never"` でも `response.done` が MCP 完了前に来ることがあり、続きの `response.create` が要る（バックエンド側の考慮）。

### Web検索の組み込み

- **Realtime には組み込み `web_search` は無い。** Responses API にはある。
- 選択肢: (1) 自前で外部プロバイダ（Brave 無料 2000/月 / Tavily 無料 1000/月）、(2) バックエンドのツール実装内で OpenAI Responses API（`tools:[{type:"web_search"}]`）を呼ぶ＝ベンダー1社で完結・新規アカウント不要（OpenAI トークン消費のみ）。
- 天気: Open-Meteo（キー不要・無料）。

## 推奨

**バックエンドが展示用に公開デプロイされている前提なら B（リモート MCP）。**

- 根拠: 音声機能が既に「device-verified & merged」＝バックエンドの公開デプロイ経路（`Dockerfile` / `productionBackendBaseUrl`）が存在する。MCP サーバーは同じバックエンドの `/mcp` ルートとして相乗りでき、同じデプロイ・同じ認証。**Unity クライアント改修ゼロ。**
- ツール実装: `weather` = Open-Meteo、`web_search` = OpenAI Responses API 経由（ベンダー1社）。
- MCP サーバーは `@modelcontextprotocol/sdk`（Node）+ Streamable HTTP。`authorization` にバックエンド発行のトークン。
- 開発時のローカル検証は ngrok 等のトンネルで OpenAI から `/mcp` に到達させる。

**もしバックエンドを展示時に公開しない（キオスクと同一 LAN / adb reverse のみ）なら A（function ツール）。** クライアント約 100 行 + `/api/tools/invoke`。

## 計画 C に持ち越す確認事項

1. 展示時、バックエンドは公開 URL でデプロイされるか（B の前提）。
2. Web検索は OpenAI Responses API 経由でよいか（追加アカウント不要）。
3. v1 ツールは `weather` + `web_search` の2つでよいか（GitHub / Maps は後）。
4. `require_approval:"never"` でよいか（中学生向け・読み取り専用ツールのみなので可のはず）。
