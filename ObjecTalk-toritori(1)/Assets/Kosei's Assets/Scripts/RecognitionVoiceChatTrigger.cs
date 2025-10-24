using UnityEngine;
using RealtimeVoiceChat;
using static EmbeddingObjectMatcher;

/// <summary>
/// EmbeddingObjectMatcherの認識フラグが立ったときにVoiceChatTestを発火させるスクリプト
/// 物体検出時に会話開始、検出喪失時に会話停止
/// </summary>
public class RecognitionVoiceChatTrigger : MonoBehaviour
{
    [Header("VoiceChat設定")]
    [Tooltip("発火させるVoiceChatTestスクリプトへの参照")]
    public VoiceChatTest voiceChatTest;

    [Header("発火設定")]
    [Tooltip("認識時に一度だけ発火するか、認識中は連続で発火するか")]
    public bool triggerOnce = true;

    [Tooltip("連続発火モードの場合、発火間隔（秒）")]
    [Range(0.5f, 10.0f)]
    public float triggerInterval = 2.0f;
    
    [Tooltip("物体を見失ったときにVoiceChatを停止するか")]
    public bool stopOnRecognitionLoss = true;

    // 内部状態管理
    private bool hasTriggered = false;
    private float lastTriggerTime = 0f;
    private string currentRecognizedObject = "";
    private bool isInitialized = false;
    private int previousRecognitionState = -1;
    private int initializationFrameCount = 0;
    private const int INIT_FRAME_DELAY = 3; // 3フレーム待機

    void Start()
    {
        Debug.Log("═══════════════════════════════════════");
        Debug.Log("🎬 RecognitionVoiceChatTrigger Start() 開始");
        Debug.Log("═══════════════════════════════════════");

        // VoiceChatTestへの参照がない場合、自動で探す
        if (voiceChatTest == null)
        {
            Debug.Log("🔍 VoiceChatTestを検索中...");
            voiceChatTest = FindObjectOfType<VoiceChatTest>();
            if (voiceChatTest == null)
            {
                Debug.LogError("❌ VoiceChatTestが見つかりません！シーン内に配置するか、Inspectorで参照を設定してください。");
                return;
            }
            else
            {
                Debug.Log($"✅ VoiceChatTestを自動検出: {voiceChatTest.gameObject.name}");
            }
        }
        else
        {
            Debug.Log($"✅ VoiceChatTestへの参照が設定済み: {voiceChatTest.gameObject.name}");
        }

        Debug.Log("⏱️ 初期化待機開始（フレームベース）");
        Debug.Log("═══════════════════════════════════════");
        Debug.Log($"⚙️ 設定: triggerOnce={triggerOnce}, stopOnRecognitionLoss={stopOnRecognitionLoss}");
        Debug.Log("═══════════════════════════════════════");
    }

    void Update()
    {
        // 初期化処理（最初の数フレームは待機）
        if (!isInitialized)
        {
            initializationFrameCount++;
            
            if (initializationFrameCount >= INIT_FRAME_DELAY)
            {
                if (EmbeddingObjectMatcher.Instance != null)
                {
                    // 現在の認識状態を初期値として記録
                    previousRecognitionState = EmbeddingObjectMatcher.Instance.IsObjectRecognized;
                    Debug.Log($"📝 初期認識状態を記録: {previousRecognitionState}");
                    
                    // イベントリスナー登録
                    EmbeddingObjectMatcher.Instance.OnRecognitionStateChanged += OnRecognitionStateChanged;
                    Debug.Log("✅ OnRecognitionStateChangedイベントにリスナー登録完了");
                    
                    isInitialized = true;
                    Debug.Log("═══════════════════════════════════════");
                    Debug.Log("✅✅✅ 初期化完了！認識検出を開始します ✅✅✅");
                    Debug.Log("═══════════════════════════════════════");
                }
                else
                {
                    Debug.LogError("❌ EmbeddingObjectMatcher.Instanceが見つかりません！");
                }
            }
            return;
        }

        // 連続発火モードの場合、定期的にチェック
        if (!triggerOnce && EmbeddingObjectMatcher.Instance != null)
        {
            if (EmbeddingObjectMatcher.Instance.IsObjectRecognized == 1)
            {
                // 指定間隔で発火
                if (Time.time - lastTriggerTime >= triggerInterval)
                {
                    Debug.Log($"🔄 [連続発火モード] 間隔到達 - VoiceChat再発火");
                    TriggerVoiceChat(EmbeddingObjectMatcher.Instance.LastRecognizedObjectName);
                }
            }
        }
    }

    /// <summary>
    /// 認識状態が変化したときに呼ばれるコールバック
    /// </summary>
    private void OnRecognitionStateChanged(bool isRecognized, string objectName)
    {
        Debug.Log("╔═══════════════════════════════════════╗");
        Debug.Log("║  OnRecognitionStateChanged 呼び出し  ║");
        Debug.Log("╚═══════════════════════════════════════╝");
        Debug.Log($"📥 isRecognized: {isRecognized}");
        Debug.Log($"📥 objectName: {objectName}");
        Debug.Log($"📥 previousRecognitionState: {previousRecognitionState}");

        // 状態変化の検証：0→1 の変化のみ発火
        int currentState = isRecognized ? 1 : 0;
        Debug.Log($"🔄 状態: {previousRecognitionState} → {currentState}");
        
        if (isRecognized)
        {
            // 0→1 の変化の場合のみ処理
            if (previousRecognitionState == 0)
            {
                Debug.Log($"★━━━━━━━━━━━━━━━━━━━━━━━━━━━━★");
                Debug.Log($"★ 物体認識イベント発火! (0→1)");
                Debug.Log($"★ 認識した物体: {objectName}");
                Debug.Log($"★━━━━━━━━━━━━━━━━━━━━━━━━━━━━★");
                
                currentRecognizedObject = objectName;

                if (triggerOnce)
                {
                    // 一度だけ発火モード
                    Debug.Log($"🎯 [Trigger Mode: Once]");
                    Debug.Log($"   hasTriggered: {hasTriggered}");
                    Debug.Log($"   currentRecognizedObject: {currentRecognizedObject}");
                    
                    if (!hasTriggered || currentRecognizedObject != objectName)
                    {
                        Debug.Log($"▶️ VoiceChatを発火します！");
                        TriggerVoiceChat(objectName);
                        hasTriggered = true;
                    }
                    else
                    {
                        Debug.Log($"⏭️ 既に発火済みのためスキップします");
                    }
                }
                else
                {
                    // 連続発火モード（Updateで処理）
                    Debug.Log($"🔄 [Trigger Mode: Continuous] VoiceChatを発火します");
                    TriggerVoiceChat(objectName);
                }
            }
            else if (previousRecognitionState == 1)
            {
                Debug.Log($"ℹ️ 認識継続中（1→1: 状態変化なし）");
            }
            else if (previousRecognitionState == -1)
            {
                Debug.Log($"⚠️ 初期化直後のイベント（-1→1）- スキップします");
            }
        }
        else
        {
            // 1→0 の変化の場合のみ処理
            if (previousRecognitionState == 1)
            {
                Debug.Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Debug.Log($"📉 物体認識喪失イベント (1→0)");
                Debug.Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                
                // 物体を見失ったときの処理
                if (stopOnRecognitionLoss && voiceChatTest != null)
                {
                    Debug.Log($"🛑 認識喪失によりVoiceChatを停止します");
                    voiceChatTest.StopConnection();
                }
                else
                {
                    Debug.Log($"ℹ️ 認識喪失しましたが、VoiceChat停止設定がOFFのため継続します");
                }

                // フラグをリセット
                hasTriggered = false;
                currentRecognizedObject = "";
                Debug.Log($"🔄 フラグをリセットしました");
            }
            else if (previousRecognitionState == 0)
            {
                Debug.Log($"ℹ️ 認識なし継続中（0→0: 状態変化なし）");
            }
            else if (previousRecognitionState == -1)
            {
                Debug.Log($"ℹ️ 初期化直後のイベント（-1→0）- 正常です");
            }
        }
        
        // 現在の状態を記録
        previousRecognitionState = currentState;
        Debug.Log($"💾 previousRecognitionState を更新: {previousRecognitionState}");
        Debug.Log("╚═══════════════════════════════════════╝");
    }

    /// <summary>
    /// VoiceChatTestを発火させる
    /// </summary>
    private void TriggerVoiceChat(string objectName)
    {
        Debug.Log($"");
        Debug.Log($"🚀━━━━━━━━━━━━━━━━━━━━━━━━━━━━🚀");
        Debug.Log($"🚀 TriggerVoiceChat() 実行開始");
        Debug.Log($"🚀━━━━━━━━━━━━━━━━━━━━━━━━━━━━🚀");

        if (voiceChatTest == null)
        {
            Debug.LogError("❌ VoiceChatTestへの参照がnullです！");
            return;
        }
        
        Debug.Log($"✅ voiceChatTest参照OK: {voiceChatTest.gameObject.name}");
        Debug.Log($"📝 対象物体: {objectName}");
        Debug.Log($"⏰ 時刻: {System.DateTime.Now:HH:mm:ss.fff}");

        lastTriggerTime = Time.time;

        Debug.Log($"▶️ voiceChatTest.StartConnection() を呼び出します...");
        
        try
        {
            voiceChatTest.StartConnection();
            Debug.Log($"✅ voiceChatTest.StartConnection() の呼び出し完了");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ StartConnection()でエラー発生: {e.Message}");
            Debug.LogError($"スタックトレース: {e.StackTrace}");
        }
        
        Debug.Log($"🚀━━━━━━━━━━━━━━━━━━━━━━━━━━━━🚀");
        Debug.Log($"");
    }

    /// <summary>
    /// VoiceChatを停止する
    /// </summary>
    private void StopConnection()
    {
        Debug.Log($"");
        Debug.Log($"🛑━━━━━━━━━━━━━━━━━━━━━━━━━━━━🛑");
        Debug.Log($"🛑 StopConnection() 実行開始");
        Debug.Log($"🛑━━━━━━━━━━━━━━━━━━━━━━━━━━━━🛑");
        
        if (voiceChatTest == null)
        {
            Debug.LogError("❌ VoiceChatTestへの参照がnullです！");
            return;
        }

        Debug.Log($"✅ voiceChatTest参照OK: {voiceChatTest.gameObject.name}");
        Debug.Log($"⏰ 時刻: {System.DateTime.Now:HH:mm:ss.fff}");
        
        Debug.Log($"▶️ voiceChatTest.StopConnection() を呼び出します...");
        
        try
        {
            voiceChatTest.StopConnection();
            Debug.Log($"✅ voiceChatTest.StopConnection() の呼び出し完了");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ StopConnection()でエラー発生: {e.Message}");
            Debug.LogError($"スタックトレース: {e.StackTrace}");
        }
        
        Debug.Log($"🛑━━━━━━━━━━━━━━━━━━━━━━━━━━━━🛑");
        Debug.Log($"");
    }

    void OnDestroy()
    {
        Debug.Log("🗑️ RecognitionVoiceChatTrigger OnDestroy - イベントリスナー解除");
        
        // イベントリスナーを解除
        if (EmbeddingObjectMatcher.Instance != null)
        {
            EmbeddingObjectMatcher.Instance.OnRecognitionStateChanged -= OnRecognitionStateChanged;
            Debug.Log("✅ OnRecognitionStateChangedイベントリスナーを解除しました");
        }
    }
}