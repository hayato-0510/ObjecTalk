namespace ObjecTalk.Domain.Conversation
{
    public enum ConversationState
    {
        Idle,
        RequestingPermission,
        Connecting,
        Listening,
        Speaking,
        Reconnecting,
        Stopping
    }

    public enum ConversationStopReason
    {
        ObjectLost,
        ObjectChanged,
        UserRequested,
        ApplicationPaused,
        AuthenticationFailed,
        FatalError
    }
}
