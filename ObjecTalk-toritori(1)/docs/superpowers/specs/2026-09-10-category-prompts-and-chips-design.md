# カテゴリ別プロンプトと質問サジェスト 設計（計画 B）

## 目的

物体のカテゴリに応じて、会話の「らしさ」と、中学生に見せる質問例を変える。

- 5学科の代表物体は手書きの `personality_prompt`（既存）を使う。
- 持ち込み物体・その他は、登録時に入力された**分類**からバックエンドがペルソナ断片を1行足す。
- 認識・登録の直後に、その分類向けの質問例 3〜4 個を Talk 画面に出す。中学生はその中の1つを**声に出して読み**、push-to-talk で話す。質問例は読み上げのヒントであり、タップで自動送信はしない。

親スペック: `docs/superpowers/specs/2026-09-10-open-campus-exhibition-design.md`（「カテゴリ別のシステムプロンプトと提案チップ」節の実装）

## 確定要件

- 登録フローは変えない。分類は今の自由入力欄（`categoryInputField`）に代表者が手入力する。
- 照合は **`trim` して 7 つの自然な日本語ラベルと完全一致**のみ。別名表・あいまい照合・運用メモはなし（性善説）。
  - ラベル: `電子機器` / `飲みもの` / `傘` / `本` / `文房具` / `アクセサリ` / `その他`
  - どれとも一致しなければ、分類ペルソナ行も質問例も出さない（従来どおり動く。後方互換）。実際に代表者が使う語が表と違ったら、そのときキーを足すだけ（実装コスト極小）。
  - 5学科物体は分類欄を空にし、`personality_prompt` を主にする。
- バックエンドの分類ペルソナは**固定の安全ルールより後、かつユーザー `personalityPrompt` より後**に置く（`exhibitionContext` と同じ。ユーザー入力で上書きさせない）。
- 質問例はタップで送信しない。表示のみ。会話が始まったら（最初のユーザー／AI 発話）、物体が変わったら、`Idle` に戻ったら、消す。
- 会話内容は保存しない（既存方針）。
- トランスポート（`RealtimeWebRtcTransport` / `RealtimeEventParser`）は変更しない。

## 現状

- `POST /api/realtime/calls` の body は `{objectId, objectName, personalityPrompt, language, sdp, mode}`。`buildInstructions({objectName, personalityPrompt, mode})` がプロンプトを組む。
- `ObjectItem.Category`（`category` 列）は存在するが会話に渡っていない。`ConversationObjectRepository.GetContext` は `ConversationContext(objectId, name, personalityPrompt, userId, "ja")` を返す。
- `RealtimeCallClient.CreateRequestBody` は上記 6 フィールドを JObject で送る。
- `buildInstructions` は `mode === 'exhibition'` のとき `exhibitionContext()`（ハードコードの高専事実）を安全ルールの後に足す。

## 採用方式

分類ラベルを `ConversationContext` → リクエスト body → `buildInstructions` に通す。バックエンドに「ラベル → ペルソナ行」表、クライアントに「ラベル → 質問例」表を1つずつ持つ。どちらも `trim` + 完全一致でルックアップし、外れたら何もしない。

## コンポーネント

### クライアント

**`ConversationContext`（拡張）**

```text
ObjectId / ObjectName / PersonalityPrompt / UserId / Language / Category
```

`Category` は空文字許容。`ConversationObjectRepository.GetContext` が `(item.Category ?? string.Empty).Trim()` を渡す。

**`RealtimeCallClient.CreateRequestBody`（拡張）**

`category` を追加。空でも空文字で送る（バックエンドは空を「分類なし」として扱う）。

**`CategorySuggestions`（新規・純粋クラス、`ObjecTalk.Domain.Conversation`）**

```csharp
public static IReadOnlyList<string> ForCategory(string category);
```

`category.Trim()` で 7 ラベルを引く。未知・空は空リスト。EditMode で網羅テスト。

| ラベル | 質問例 |
|---|---|
| 電子機器 | これで何が作れる？ / プログラミングってむずかしい？ / AIってどうやって動いてるの？ |
| 飲みもの | 今日はこれ持っていったほうがいい？ / これって体にいいの？ |
| 傘 | 今日はこれ持っていったほうがいい？ / 明日の天気は？ |
| 本 | これに何が書いてあるの？ / 似た本ある？ / どんな人におすすめ？ |
| 文房具 | これのじょうずな使い方は？ / これで何ができる？ |
| アクセサリ | これってどうやって作るの？ / どこで使うもの？ |
| その他 | これ何だと思う？ / これについて教えて |

**`TalkController` / `TalkView`（質問例の表示）**

- `TalkController` が認識イベント（`RecognizedObjectState` が recognized）で `CategorySuggestions.ForCategory(context.Category)` を引き、非空なら `TalkView.ShowSuggestions(IReadOnlyList<string>)` を呼ぶ。
- `TalkView` は「こう聞いてみよう」の見出し + 質問例を縦に並べたパネルを動的生成（`PushToTalkButton` のランタイム生成に倣う）。タップ判定なし（`raycastTarget` オフ）。
- 消すタイミング: `voiceService` が最初に `Speaking` になったとき／物体が変わったとき（`SetCurrentObjectName` が別名で呼ばれた or 認識が別 ObjectId）／`Idle` に戻ったとき → `TalkView.ClearSuggestions()`。
- 物体が変わって新カテゴリなら出し直す。

### バックエンド

**`src/conversation/categoryPersona.js`（新規）**

```js
export function categoryPersona(category) {
  // category を trim し、下表に完全一致すれば1行返す。外れ・空・null は ''。
}
```

| ラベル | ペルソナ行 |
|---|---|
| 電子機器 | あなたは電子機器です。仕組み・使い方・作れるものを、中学生にもわかる言葉で短く話します。 |
| 飲みもの | あなたは飲みものです。中身や体との関わりを、やさしく短く話します。 |
| 傘 | あなたは傘です。今日必要かどうかや、天気の話題に自然につなげます。 |
| 本 | あなたは本です。中身・ジャンル・似たものの話をします。 |
| 文房具 | あなたは文房具です。使い方の工夫や、勉強・ものづくりの話をします。 |
| アクセサリ | あなたは小物・アクセサリです。作り方や使いどころの話をします。 |
| その他 | （行なし・`''`） |

**`src/conversation/buildInstructions.js`（拡張）**

`buildInstructions({objectName, personalityPrompt, mode, category})`。`category` は文字列であれば受け取り、制御文字除去 → `categoryPersona()`。非空なら、安全ルールの後・`exhibitionContext()` の前に1行足す。`category` が文字列で正規化前 50 文字超なら `RangeError`。`category` 省略時は現状のまま。

**`src/app.js validateBody`（拡張）**

`category` を optional として受理: 文字列でなければ 400、`trim` 後 0–50 文字の範囲外なら 400。`buildInstructions` に渡す。

## テスト戦略

### バックエンド（`node --test`）

- `categoryPersona.test.js` — 各ラベルが期待の行を返す、`その他`／未知／空／null／前後空白付きラベルの扱い。
- `buildInstructions.test.js`（追加）— `category` 指定時に分類行が安全ルールの後・`exhibitionContext` の前に入る。未知 `category` では入らない。制御文字が除去される。51 文字で `RangeError`。`exhibition` + `category` の順序（安全ルール → 分類行 → exhibition）。
- `app.test.js`（追加）— body の `category` が `buildInstructions` に届く。51 文字で 400。非文字列で 400。`category` 無しで従来どおり 200。

### Unity EditMode

- `CategorySuggestionsTests` — 各ラベルが 3〜4 件、前後空白付きでも引ける、未知・空・null は空、要素に重複なし。
- `ConversationObjectRepositoryTests`（追加）— `ConversationContext.Category` が `item.Category` の trim 結果、null は空文字。
- `RealtimeCallClientTests`（追加）— リクエスト body に `category` キーが入る、空カテゴリは空文字列。
- `ConversationContext` のコンストラクタ引数追加に伴う既存テストの更新。

### Unity PlayMode

- `TalkController` / `TalkView`（`TalkLifecycleTests` 系）— 認識で質問例が表示される、`Speaking` で消える、物体変更で新カテゴリに差し替わる、`Idle` で消える、未知カテゴリでは出ない。

## 完了条件

- 分類（7 ラベルのいずれか）で登録した持ち込み物体の会話に、分類ペルソナ行が入る（安全ルールの後）。
- 認識時にその分類の質問例が Talk 画面に出て、会話が始まると消える。
- `category` 無し・未知ラベルのリクエストが従来どおり動く。
- 5学科物体（手書き `personality_prompt`、分類なし）の挙動は変わらない。
- バックエンド `node --test` 全通過、Unity EditMode / PlayMode 全通過。

## 対象外

- RAG・外部ツール（天気 / Web検索）— 計画 C。
- 質問例のタップ送信、データチャネルへのテキスト送信。
- 登録フローのキオスク簡素化・分類プリセット UI。
- 別名照合・あいまい一致。
- 質問例の多言語化、会話履歴の保存。
