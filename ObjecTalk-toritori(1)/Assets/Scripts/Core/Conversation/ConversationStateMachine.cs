using ObjecTalk.Domain.Conversation;

namespace ObjecTalk.Core.Conversation
{
    public sealed class ConversationStateMachine
    {
        public ConversationStateMachine()
        {
            State = ConversationState.Idle;
        }

        public ConversationState State { get; private set; }

        public long CurrentGeneration { get; private set; }

        public long BeginGeneration()
        {
            CurrentGeneration++;
            State = ConversationState.Idle;
            return CurrentGeneration;
        }

        public bool TryTransition(ConversationState next, long generation)
        {
            if (generation != CurrentGeneration || !IsTransitionAllowed(State, next))
            {
                return false;
            }

            State = next;
            return true;
        }

        public bool ForceIdle(long generation)
        {
            if (generation != CurrentGeneration)
            {
                return false;
            }

            State = ConversationState.Idle;
            return true;
        }

        private static bool IsTransitionAllowed(ConversationState current, ConversationState next)
        {
            switch (current)
            {
                case ConversationState.Idle:
                    return next == ConversationState.RequestingPermission;
                case ConversationState.RequestingPermission:
                    return next == ConversationState.Connecting || next == ConversationState.Stopping;
                case ConversationState.Connecting:
                    return next == ConversationState.Listening || next == ConversationState.Reconnecting || next == ConversationState.Stopping;
                case ConversationState.Listening:
                    return next == ConversationState.Speaking || next == ConversationState.Reconnecting || next == ConversationState.Stopping;
                case ConversationState.Speaking:
                    return next == ConversationState.Listening || next == ConversationState.Reconnecting || next == ConversationState.Stopping;
                case ConversationState.Reconnecting:
                    return next == ConversationState.Connecting || next == ConversationState.Stopping;
                case ConversationState.Stopping:
                    return next == ConversationState.Idle;
                default:
                    return false;
            }
        }
    }
}
