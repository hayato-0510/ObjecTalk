// AI設定ファイル (ai.config.js)
// 音声とモデル、システムプロンプトを一元管理

export const AI_CONFIG = {
    // OpenAI Realtime API 設定
    voice: 'marin',  // 'alloy', 'echo', 'fable', 'onyx', 'nova', 'shimmer'から選択
    model: 'gpt-realtime-mini',
    
    // ロールキーワード (オプション: 設定すると自動でロールプロンプトを生成)
    roleKeyword: '猫娘',  // 例: '猫娘', 'メイド', '博士' など
    
    // ベースシステムプロンプト
    baseSystemPrompt: `あなたは親しみやすく、フレンドリーな音声アシスタントです。
自然な会話を心がけ、ユーザーとの対話を楽しんでください。

# 会話の特徴
- 短く簡潔に答える
- 自然で人間らしい話し方をする
- ユーザーの質問に対して適切に応答する
- 必要に応じて質問を返す

# 注意事項
- 音声での会話なので、読み上げに適した自然な日本語を使う
- 箇条書きや記号は避け、話し言葉で表現する`,

    // システムプロンプト (デフォルトはbaseSystemPromptを使用)
    systemPrompt: null,
};

// システムプロンプトが未設定の場合、baseSystemPromptを使用
if (!AI_CONFIG.systemPrompt) {
    AI_CONFIG.systemPrompt = AI_CONFIG.baseSystemPrompt;
}
