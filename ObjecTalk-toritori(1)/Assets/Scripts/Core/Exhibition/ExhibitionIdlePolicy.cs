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
