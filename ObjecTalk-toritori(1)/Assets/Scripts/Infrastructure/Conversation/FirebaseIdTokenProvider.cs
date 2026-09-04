using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ObjecTalk.Infrastructure.Conversation.OpenAI;

namespace ObjecTalk.Infrastructure.Conversation
{
    public interface IFirebaseIdTokenSource
    {
        Task<string> GetCurrentTokenAsync(bool forceRefresh);
    }

    public sealed class FirebaseIdTokenProvider : IIdTokenProvider
    {
        private readonly IFirebaseIdTokenSource _tokenSource;

        public FirebaseIdTokenProvider()
            : this(new ReflectionFirebaseIdTokenSource())
        {
        }

        public FirebaseIdTokenProvider(IFirebaseIdTokenSource tokenSource)
        {
            _tokenSource = tokenSource ?? throw new ArgumentNullException(nameof(tokenSource));
        }

        public async Task<string> GetAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Task<string> tokenTask = _tokenSource.GetCurrentTokenAsync(forceRefresh);
                string token = await AwaitWithCancellation(tokenTask, cancellationToken);
                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new InvalidOperationException("Firebase token is unavailable.");
                }

                return token;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                throw new InvalidOperationException("Firebase authentication is unavailable.");
            }
        }

        private static async Task<string> AwaitWithCancellation(Task<string> task, CancellationToken cancellationToken)
        {
            if (task == null)
            {
                throw new InvalidOperationException("Firebase token is unavailable.");
            }

            if (!cancellationToken.CanBeCanceled)
            {
                return await task;
            }

            var cancellation = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => cancellation.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancellation.Task) == task)
                {
                    return await task;
                }
            }

            ObserveAbandonedFault(task);
            throw new OperationCanceledException(cancellationToken);
        }

        private static void ObserveAbandonedFault(Task task)
        {
            task.ContinueWith(
                completed => { var ignored = completed.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }
    }

    internal sealed class ReflectionFirebaseIdTokenSource : IFirebaseIdTokenSource
    {
        public Task<string> GetCurrentTokenAsync(bool forceRefresh)
        {
            Type authType = FindFirebaseAuthType();
            PropertyInfo defaultInstance = authType?.GetProperty("DefaultInstance", BindingFlags.Public | BindingFlags.Static);
            object auth = defaultInstance?.GetValue(null, null);
            PropertyInfo currentUser = auth?.GetType().GetProperty("CurrentUser", BindingFlags.Public | BindingFlags.Instance);
            object user = currentUser?.GetValue(auth, null);
            if (user == null)
            {
                throw new InvalidOperationException("Firebase user is unavailable.");
            }

            MethodInfo tokenAsync = user.GetType().GetMethod("TokenAsync", new[] { typeof(bool) });
            Task<string> tokenTask = tokenAsync?.Invoke(user, new object[] { forceRefresh }) as Task<string>;
            if (tokenTask == null)
            {
                throw new InvalidOperationException("Firebase token is unavailable.");
            }

            return tokenTask;
        }

        private static Type FindFirebaseAuthType()
        {
            Type authType = Type.GetType("Firebase.Auth.FirebaseAuth, Firebase.Auth");
            if (authType != null)
            {
                return authType;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                authType = assembly.GetType("Firebase.Auth.FirebaseAuth", false);
                if (authType != null)
                {
                    return authType;
                }
            }

            return null;
        }
    }
}
