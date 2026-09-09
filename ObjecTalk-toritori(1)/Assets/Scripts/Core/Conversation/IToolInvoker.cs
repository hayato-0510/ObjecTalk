using System.Threading;
using System.Threading.Tasks;

namespace ObjecTalk.Core.Conversation
{
    /// <summary>
    /// Runs a tool the model asked for and returns a short result string for the
    /// <c>function_call_output</c>. Implementations never throw — a failure comes back as a plain
    /// message the model can read out — so a flaky tool cannot drop the conversation.
    /// </summary>
    public interface IToolInvoker
    {
        Task<string> InvokeAsync(string name, string argumentsJson, CancellationToken cancellationToken);
    }
}
