using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ObjecTalk.Infrastructure.Conversation.OpenAI;

namespace ObjecTalk.Infrastructure.Conversation
{
    public sealed class FirebaseIdTokenProvider : IIdTokenProvider
    {
        public async Task<string> GetAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
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

        private static async Task<string> AwaitWithCancellation(Task<string> task, CancellationToken cancellationToken)
        {
            var cancellation = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => cancellation.TrySetResult(true)))
            {
                Task completed = await Task.WhenAny(task, cancellation.Task);
                cancellationToken.ThrowIfCancellationRequested();
                if (completed != task)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            return await task;
        }
    }
}
