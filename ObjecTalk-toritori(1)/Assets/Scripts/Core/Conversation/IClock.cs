using System;

namespace ObjecTalk.Core.Conversation
{
    public interface IClock
    {
        DateTimeOffset UtcNow { get; }
    }
}
