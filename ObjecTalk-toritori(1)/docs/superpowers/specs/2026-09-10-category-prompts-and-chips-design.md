# カテゴリ別プロンプトと提案チップ 設計（計画 B）

## 目的

物体のカテゴリに応じて会話の「らしさ」と、中学生がタップして聞ける質問候補を変える。

- 5学科の代表物体は手書きの `personality_prompt`（既存の仕組み）を使う。
- 持ち込み物体・その他は、登録時に選んだ**分類**からバックエンドがペルソナ断片を足す。
- 認識・登録の直後に、その分類向けの質問チップ 3〜4 個を Talk 画面に出す。チップをタップすると、その質問文がユーザー発話として Realtime セッションに送られ、AI が答える。

親スペック: `docs/superpowers/specs/2026-09-10-open-campus-exhibition-design.md`（「カテゴリ別のシステムプロンプトと提案チップ」節の実装）

## 確定要件

- 登録フローは今のまま（分類は `categoryInputField` に代表者が手入力）。本計画は**入力された分類文字列を正規化して照合する**だけで、UI は変えない。
- 正規化: trim → 小さな別名表で正規ラベルへ寄せる（例「でんし」「電子」「ガジェット」→「電子機器」、「かさ」→「傘」）。canonical ラベルは日本語:
  - `電子機器` / `飲みもの` / `傘` / `本` / `文房具` / `アクセサリ` / `その他`
  - 代表者向けに、この 7 ラベルを書いた 1 行のチートシートを運用メモに残す。
  - 5学科物体は分類欄を空か任意にし、`personality_prompt` を主にする。
- 正規化後のラベルがどの canonical にも一致しなければ、分類ペルソナ行もチップも出さない（現在の挙動と同じ・後方互換）。
- バックエンドの分類ペルソナは**固定の安全ルールより後、かつユーザー `personalityPrompt` より後**に置く（`exhibitionContext` と同じ扱い。ユーザー入力で上書きさせない）。
- 正規化はクライアントとバックエンドで同じ規則。クライアントは正規化済み canonical ラベルを `category` として送り、バックエンドは受け取った文字列をもう一度正規化してから照合する（多重防御）。
- チップのタップは、`conversation.item.create`（`input_text`）+ `response.create` をデータチャネルに送る。push-to-talk とは別経路。マイクは開かない。
- チップのタップ後はチップを隠す（1 認識につき 1 回まで）。物体が変わったら新しい分類のチップを出し直す。
- 会話内容は保存しない（既存方針）。

## 現状

- `POST /api/realtime/calls` の body は `{objectId, objectName, personalityPrompt, language, sdp, mode}`。`buildInstructions({objectName, personalityPrompt, mode})` がプロンプトを組む。
- `ObjectItem.Category`（`category` 列）は存在するが会話には渡っていない。`ConversationObjectRepository.GetContext` は `ConversationContext(objectId, name, personalityPrompt, userId, "ja")` を返すだけ。
- `RealtimeCallClient.CreateRequestBody` は上記 6 フィールドを JObject で送る。
- `RealtimeWebRtcTransport` はデータチャネルの受信イベントを `RealtimeEvent` に変換するのみ。送信は `response.cancel` のみ（`CancelResponseAsync`）。
- `RealtimeEventParser` は固定のイベント種別だけ解釈する。

## 採用方式

分類ラベルを `ConversationContext` → リクエスト body → `buildInstructions` に通す。正規化（別名表→ canonical 7 ラベル）はクライアント・バックエンド双方に同じロジックで持つ。canonical ラベル → ペルソナ断片の対応表をバックエンドに、canonical ラベル → チップ文言の対応表をクライアントに持つ。チップ送信のためにトランスポートへ最小の「ユーザーテキスト送信」を1つ足す。

分類ペルソナはプロンプトへの1〜2行の追加であり、RAG やツールは使わない（それは計画 C）。

canonical ラベルと別名（初期値。実装時に増やせる）:

| canonical | 別名（正規化前） |
|---|---|
| 電子機器 | でんし, 電子, ガジェット, デバイス, PC, パソコン, スマホ, arduino, アルドゥイーノ |
| 飲みもの | 飲み物, のみもの, ドリンク, ペットボトル, 水筒 |
| 傘 | かさ, アンブレラ |
| 本 | 書籍, ほん, ノート, 教科書, プリント, 紙 |
| 文房具 | ぶんぼうぐ, ペン, 鉛筆, えんぴつ, 消しゴム, 定規 |
| アクセサリ | アクセサリー, キーホルダー, かざり, 小物 |
| その他 | (空文字・不明はここ。ただしペルソナ行もチップも "その他" は最小限) |

## コンポーネント

### クライアント

**`ConversationContext`（拡張）**

```text
ObjectId / ObjectName / PersonalityPrompt / UserId / Language / Category
```

`Category` は空文字許容。`ConversationObjectRepository.GetContext` が `item.Category ?? ""` を渡す。

**`CategoryLabel`（新規・純粋クラス、`ObjecTalk.Domain.Conversation`）**

```csharp
static string Normalize(string raw);   // trim → 別名表 → canonical 7ラベル、一致しなければ ""
```

`ConversationObjectRepository.GetContext` が `CategoryLabel.Normalize(item.Category)` を渡す。EditMode で別名・空・未知を網羅テスト。

**`RealtimeCallClient.CreateRequestBody`（拡張）**

正規化済み `category` を追加（空なら空文字で送る）。

**`CategorySuggestions`（新規・純粋クラス、`ObjecTalk.Domain.Conversation`）**

```csharp
static IReadOnlyList<string> ForCategory(string canonicalLabel);
```

canonical ラベル → 3〜4 個の質問文。未知・空は空リスト。EditMode で網羅テスト。

例:
- `電子機器` — 「これで何が作れる？」「プログラミングってむずかしい？」「AIってどうやって動いてるの？」
- `飲みもの` — 「今日はこれ持っていったほうがいい？」「これって体にいいの？」
- `傘` — 「今日はこれ持っていったほうがいい？」「明日の天気は？」
- `本` — 「これに何が書いてあるの？」「似た本ある？」
- `文房具` — 「これのじょうずな使い方は？」「これで何ができる？」
- `アクセサリ` — 「これってどうやって作るの？」「どこで使うもの？」
- `その他` — 「これ何だと思う？」「これについて教えて」

**`IRealtimeConversationTransport.SendUserTextAsync(string text, CancellationToken)`（新規メソッド）**

`RealtimeWebRtcTransport` 実装: データチャネルに
`{"type":"conversation.item.create","item":{"type":"message","role":"user","content":[{"type":"input_text","text":<text>}]}}`
を送り、続けて `{"type":"response.create"}` を送る。未接続なら失敗を返す。

`OpenAIRealtimeConversationService` にパススルー `SendUserTextAsync` を足し、`IVoiceConversationService` にも公開する。状態が `Listening` のときだけ受け付け、送信後は `Speaking` への遷移をサーバーイベントに任せる。

**`TalkController` / `TalkView`（チップ UI）**

- `TalkController` が認識イベントで `CategorySuggestions.ForCategory(context.Category)` を引き、`TalkView.ShowSuggestions(IReadOnlyList<string>)` を呼ぶ。
- `TalkView` はチップ（横並びのボタン）を動的生成。タップで `TalkController` 経由 `voiceService.SendUserTextAsync(text)` を呼び、全チップを隠す。
- 物体が変わったら `TalkView.ClearSuggestions()` → 新カテゴリで出し直す。
- 会話が `Idle` に戻ったらチップを隠す。

### バックエンド

**`src/conversation/categoryLabel.js`（新規）**

```js
export function normalizeCategory(raw) { /* trim → alias table → canonical 7 labels, else '' */ }
```

Unity 側 `CategoryLabel.Normalize` と同じ別名表・同じ canonical。バックエンドは受信文字列をこれで再正規化してから照合する（多重防御）。

**`src/conversation/categoryPersona.js`（新規）**

```js
export function categoryPersona(canonicalLabel) { /* returns a line, or '' for unknown/empty */ }
```

対応表:
- `電子機器` — 「あなたは電子機器です。仕組み・使い方・作れるものを、中学生にもわかる言葉で短く話します。」
- `飲みもの` — 「あなたは飲みものです。中身や体との関わりを、やさしく短く話します。」
- `傘` — 「あなたは傘です。今日必要かどうかや、天気の話題に自然につなげます。」
- `本` — 「あなたは本です。中身・ジャンル・似たものの話をします。」
- `文房具` — 「あなたは文房具です。使い方の工夫や、勉強・ものづくりの話をします。」
- `アクセサリ` — 「あなたは小物・アクセサリです。作り方や使いどころの話をします。」
- `その他` — （行なし）

**`src/conversation/buildInstructions.js`（拡張）**

`buildInstructions({objectName, personalityPrompt, mode, category})`。`category` を制御文字除去 → `normalizeCategory`。`categoryPersona(...)` が非空なら、安全ルールの後・`exhibitionContext` の前に1行足す。`category` は正規化前 0–50 文字を超えたら `RangeError`。

**`src/app.js validateBody`（拡張）**

`category` を optional として受理: 文字列、trim 後 0–50 文字、それ以外は 400。`buildInstructions` に渡す。

## データフロー（チップ送信）

1. 物体認識 → `TalkController` が `CategorySuggestions.ForCategory` → `TalkView.ShowSuggestions`
2. 中学生がチップをタップ
3. `TalkController` → `voiceService.SendUserTextAsync(text)` → `RealtimeWebRtcTransport` がデータチャネルに `conversation.item.create` + `response.create`
4. サーバーが応答生成 → 既存のイベント経路で `Speaking` 表示・音声再生
5. `TalkView` がチップを隠す

## テスト戦略

### バックエンド（`node --test`）

- `categoryLabel.test.js` — 別名が canonical へ寄る、canonical はそのまま、空・未知・null は `''`、前後空白を無視。
- `categoryPersona.test.js` — 既知 canonical が期待の文言を返す、`その他` / 未知 / 空 / null は `''`。
- `buildInstructions.test.js`（追加）— `category` に別名を渡しても分類行が入る、安全ルールの後・`exhibitionContext` の前。未知 `category` では行が入らない。`category` の制御文字が除去される。正規化前 51 文字で `RangeError`。
- `app.test.js`（追加）— body の `category` が `buildInstructions` に渡る。51 文字で 400。`category` 無しでも従来どおり通る。

### Unity EditMode

- `CategoryLabelTests` — 別名 → canonical、空・未知 → 空文字、前後空白の除去。
- `CategorySuggestionsTests` — 各 canonical が 3〜4 件を返す、未知・空は空、重複なし。
- `ConversationObjectRepositoryTests`（追加）— `ConversationContext.Category` が `CategoryLabel.Normalize(item.Category)` の結果になる、null / 未知は空文字。
- `RealtimeCallClientTests`（追加）— リクエスト body に `category` が入る。空カテゴリは空文字で送る。
- `RealtimeEventParser` は変更なし（送信のみ追加のため）。

### Unity PlayMode

- `RealtimeWebRtcTransport`（`TalkLifecycleTests` 系）— `SendUserTextAsync` がデータチャネルに正しい 2 メッセージを順に送る、未接続で失敗。
- `TalkController` / `TalkView` — 認識でチップが出る、タップで `SendUserTextAsync` が呼ばれチップが消える、物体変更で出し直す、`Idle` で消える。

## 完了条件

- 分類つきで登録した持ち込み物体の会話に、分類ペルソナ行が入る（安全ルールの後）。
- 認識時に分類向けチップが出る。タップすると AI がその質問に答え、チップが消える。
- `category` 無しのリクエストが従来どおり動く（後方互換）。
- 5学科物体（手書き `personality_prompt`）の挙動は変わらない。
- バックエンド `node --test` 全通過、Unity EditMode / PlayMode 全通過。

## 対象外

- RAG・外部ツール（天気 / Web検索）— 計画 C。
- 登録フローのキオスク簡素化（分類プリセット UI 自体）— 対象外（代表者が操作する前提。現在の自由入力の分類欄をそのまま使い、本計画は入力文字列の正規化・照合のみ）。
- チップ文言の多言語化。
- 会話履歴の保存。
