using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ObjecTalk.Presentation.Talk
{
    /// <summary>
    /// A hold-to-talk control for the exhibition's push-to-talk mode: it reports held/released,
    /// nothing more. Can be placed on a UI element in the scene or created at runtime by
    /// <see cref="TalkController"/> when the scene has none.
    /// </summary>
    public sealed class PushToTalkButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private bool held;

        /// <summary>Raised with <c>true</c> on press and <c>false</c> on release (or pointer leaving).</summary>
        public event Action<bool> HeldChanged;

        public bool Held => held;

        public void OnPointerDown(PointerEventData eventData) => SetHeld(true);

        public void OnPointerUp(PointerEventData eventData) => SetHeld(false);

        public void OnPointerExit(PointerEventData eventData) => SetHeld(false);

        private void OnDisable() => SetHeld(false);

        private void SetHeld(bool value)
        {
            if (held == value)
            {
                return;
            }

            held = value;
            HeldChanged?.Invoke(value);
        }
    }
}
