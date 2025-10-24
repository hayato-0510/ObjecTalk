// WebSocket対応のNode.js Backend Server (server.js)
// 役割:
// 1. フロントエンドにサービスを提供する
// 2. WebSocketでOpenAI Realtime APIと接続
// 3. エフェメラルキー方式でセキュアな音声通信を実現

import express from 'express';
import cors from 'cors';
import axios from 'axios';
import dotenv from 'dotenv';
import { createServer } from 'http';
import { AI_CONFIG } from './ai.config.js';

dotenv.config();

const app = express();
const PORT = 3001;
const OPENAI_API_KEY = process.env.OPENAI_API_KEY;
const VOICE = AI_CONFIG.voice;
const MODEL = AI_CONFIG.model;
let SYSTEM_PROMPT = AI_CONFIG.systemPrompt;

// CORS設定（Unityクライアント用）
app.use(cors({ origin: '*' })); // Unity からのアクセスを許可
app.use(express.json());

// APIキーの確認
if (!OPENAI_API_KEY) {
    console.error("FATAL: OPENAI_API_KEYが.envファイルに設定されていません。");
    process.exit(1);
}

// HTTPサーバーの作成
const server = createServer(app);

// ----------------------------------------------------
// ロールプロンプトを生成する関数
// ----------------------------------------------------
async function generateRolePrompt(roleKeyword) {
    try {
        console.log(`🤖 ロールキーワード「${roleKeyword}」からプロンプトを生成中...`);
        
        const response = await axios.post('https://api.openai.com/v1/chat/completions', {
            model: 'gpt-4o-mini',
            messages: [
                {
                    role: 'system',
                    content: 'あなたはAIキャラクター設定の専門家です。与えられたキーワードから、そのキャラクターになりきるための詳細なロール設定を生成してください。'
                },
                {
                    role: 'user',
                    content: `以下のキーワードをベースに、音声対話AIのロール設定を生成してください：

キーワード: 「${roleKeyword}」

以下の形式で出力してください：
# Role
[このキャラクターの詳細な説明。性格、話し方、特徴など]

# Speaking Style
[話し方の特徴、語尾、言葉遣いなど]

# Approach
[ユーザーとの対話でどのように振る舞うか]

注意：
- 創造的で魅力的なキャラクター設定を考えてください
- 音声対話に適した自然な話し方を設定してください
- 日本語で対話するキャラクターとして設定してください`
                }
            ],
            temperature: 0.8,
            max_tokens: 1000
        }, {
            headers: {
                'Authorization': `Bearer ${OPENAI_API_KEY}`,
                'Content-Type': 'application/json'
            }
        });

        const generatedRole = response.data.choices[0].message.content;
        console.log('✅ ロールプロンプトを生成しました\n');
        console.log('--- Generated Role ---');
        console.log(generatedRole);
        console.log('--- End of Role ---\n');
        
        return generatedRole + '\n\n' + AI_CONFIG.baseSystemPrompt;
        
    } catch (error) {
        console.error('❌ ロールプロンプト生成エラー:', error.message);
        console.log('⚠️ デフォルトプロンプトを使用します');
        return AI_CONFIG.baseSystemPrompt;
    }
}

// ----------------------------------------------------
// 起動時の初期化処理
// ----------------------------------------------------
async function initialize() {
    if (AI_CONFIG.roleKeyword) {
        SYSTEM_PROMPT = await generateRolePrompt(AI_CONFIG.roleKeyword);
    } else {
        SYSTEM_PROMPT = AI_CONFIG.baseSystemPrompt;
    }
}

// ----------------------------------------------------
// システムプロンプトを取得するエンドポイント
// ----------------------------------------------------
app.get('/api/config', (req, res) => {
    console.log(`📥 GET /api/config - クライアントから設定情報の要求`);
    console.log(`   Client IP: ${req.ip}`);
    console.log(`   User-Agent: ${req.get('User-Agent')}`);
    
    res.json({
        systemPrompt: SYSTEM_PROMPT,
        voice: VOICE,
        model: MODEL,
        roleKeyword: AI_CONFIG.roleKeyword || null
    });
    
    console.log(`✅ 設定情報を送信完了`);
});

// ----------------------------------------------------
// Ephemeral Keyを生成するエンドポイント
// ----------------------------------------------------
app.post('/api/session', async (req, res) => {
    console.log("\n" + "=".repeat(60));
    console.log("📥 POST /api/session - Ephemeral Key 生成要求を受信");
    console.log(`   Client IP: ${req.ip}`);
    console.log(`   User-Agent: ${req.get('User-Agent')}`);
    console.log(`   Time: ${new Date().toLocaleString('ja-JP')}`);
    console.log("=".repeat(60));

    try {
        // WebRTC方式のセッション設定（公式リファレンス準拠）
        const sessionConfig = {
            model: MODEL,
            voice: VOICE,
            instructions: SYSTEM_PROMPT,
            modalities: ["text", "audio"],
            input_audio_format: "pcm16",
            output_audio_format: "pcm16",
            input_audio_transcription: {
                model: "whisper-1"
            },
            turn_detection: {
                type: "server_vad",
                threshold: 1.0,
                prefix_padding_ms: 300,
                silence_duration_ms: 500
            }
        };

        console.log("📝 Session config:");
        console.log(`   Model: ${MODEL}`);
        console.log(`   Voice: ${VOICE}`);
        console.log(`   Instructions: ${SYSTEM_PROMPT.substring(0, 100)}...`);

        // OpenAI APIにEphemeral Keyをリクエスト
        const response = await axios.post(
            'https://api.openai.com/v1/realtime/sessions',
            sessionConfig,
            {
                headers: {
                    "Authorization": `Bearer ${OPENAI_API_KEY}`,
                    "Content-Type": "application/json"
                }
            }
        );

        console.log("✅ OpenAI APIからレスポンス受信");
        console.log("📦 Full response:", JSON.stringify(response.data, null, 2));
        
        // レスポンスの構造を確認してclient_secretを取得
        const clientSecret = response.data.client_secret?.value || response.data.client_secret;
        const expiresAt = response.data.client_secret?.expires_at || response.data.expires_at;
        
        if (!clientSecret) {
            console.error("❌ Failed to extract ephemeral key");
            console.error("   Response structure:", JSON.stringify(response.data, null, 2));
            throw new Error("Invalid response structure from OpenAI");
        }
        
        console.log(`🔑 Ephemeral Key 生成成功: ${clientSecret.substring(0, 20)}...`);
        if (expiresAt) {
            console.log(`⏰ 有効期限: ${new Date(expiresAt * 1000).toLocaleString('ja-JP')}`);
        }
        
        // クライアントにEphemeral Keyを返す
        res.json({
            client_secret: clientSecret,
            expires_at: expiresAt,
            model: MODEL,
            voice: VOICE,
            instructions: SYSTEM_PROMPT
        });
        
        console.log(`📤 クライアントにEphemeral Keyを送信完了`);
        console.log("=".repeat(60) + "\n");

    } catch (error) {
        console.error("❌ OpenAI API Error:");
        console.error(`   Status: ${error.response?.status}`);
        console.error(`   Message: ${error.message}`);
        console.error(`   Details:`, error.response?.data || error.message);
        console.log("=".repeat(60) + "\n");
        
        res.status(error.response?.status || 500).json({
            error: "Failed to generate ephemeral key",
            details: error.response?.data || error.message
        });
    }
});

// WebSocketサーバーは不要（クライアントがOpenAIに直接接続するため）
// このサーバーはエフェメラルキー生成のみを担当

// ----------------------------------------------------
// サーバー起動
// ----------------------------------------------------
initialize().then(() => {
    server.listen(PORT, '0.0.0.0', () => {
        console.log("\n" + "=".repeat(60));
        console.log("🚀 サーバー起動完了");
        console.log("=".repeat(60));
        console.log(`✅ Backend server running on http://localhost:${PORT}`);
        console.log(`🎙️  Voice: ${VOICE}`);
        console.log(`🤖 Model: ${MODEL}`);
        if (AI_CONFIG.roleKeyword) {
            console.log(`🎭 Role Keyword: ${AI_CONFIG.roleKeyword}`);
        }
        console.log(`💬 System Prompt: ${SYSTEM_PROMPT.substring(0, 100)}...`);
        console.log(`🔑 エフェメラルキー生成サーバーとして動作中`);
        console.log(`📝 クライアントはエフェメラルキーを使用してOpenAIに直接接続します`);
        console.log("=".repeat(60));
        console.log("⏳ クライアントからの接続を待機中...\n");
    });
});
