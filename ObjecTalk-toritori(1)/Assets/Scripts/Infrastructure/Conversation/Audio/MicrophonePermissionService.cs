using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace ObjecTalk.Infrastructure.Conversation.Audio
{
    public enum MicrophonePermissionStatus
    {
        Granted,
        Denied,
        DeniedPermanently
    }

    public interface IMicrophonePermissionService
    {
        Task<MicrophonePermissionStatus> RequestAsync(CancellationToken cancellationToken);
    }

    public sealed class MicrophonePermissionService : IMicrophonePermissionService
    {
        public async Task<MicrophonePermissionStatus> RequestAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                return MicrophonePermissionStatus.Granted;
            }

            var completion = new TaskCompletionSource<MicrophonePermissionStatus>();
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += permission => completion.TrySetResult(MicrophonePermissionStatus.Granted);
            callbacks.PermissionDenied += permission => completion.TrySetResult(MicrophonePermissionStatus.Denied);
            callbacks.PermissionDeniedAndDontAskAgain += permission => completion.TrySetResult(MicrophonePermissionStatus.DeniedPermanently);
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
            return await AwaitWithCancellation(completion.Task, cancellationToken);
#else
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                AsyncOperation operation = Application.RequestUserAuthorization(UserAuthorization.Microphone);
                while (!operation.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
            }

            return Application.HasUserAuthorization(UserAuthorization.Microphone)
                ? MicrophonePermissionStatus.Granted
                : MicrophonePermissionStatus.Denied;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static async Task<T> AwaitWithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
        {
            var cancellation = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => cancellation.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancellation.Task) == task)
                {
                    return await task;
                }
            }

            throw new OperationCanceledException(cancellationToken);
        }
#endif
    }
}
