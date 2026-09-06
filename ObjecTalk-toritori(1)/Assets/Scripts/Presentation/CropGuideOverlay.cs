using System.Collections;
using UnityEngine;
using ObjecTalk.Domain.Recognition;

/// <summary>
/// Draws the crop guide over a camera surface: a centred square frame matching the fraction of the
/// frame the recognition keeps, with everything outside it covered by an opaque surround. The
/// component sits as a child of the RawImage / snapshot Image it guides and sizes itself against
/// that parent's RectTransform, so the same setup works for the register preview, the placement
/// snapshot, and the talk preview. All geometry is the engine-free
/// <see cref="RegistrationGuideLayout"/> / <see cref="RegistrationMaskLayout"/>; this class is only
/// the glue that copies the result onto RectTransforms.
/// </summary>
[DisallowMultipleComponent]
public class CropGuideOverlay : MonoBehaviour
{
    [Tooltip("中央の切り出し枠（ボーダーのみ、raycastTarget off）。ボーダーの子は枠の辺にアンカーしておく。")]
    public RectTransform frame;

    [Tooltip("枠の外を覆う不透明ストリップ。上・下・左・右の順。")]
    public RectTransform maskTop;
    public RectTransform maskBottom;
    public RectTransform maskLeft;
    public RectTransform maskRight;

    [Range(0.3f, 1f)]
    [Tooltip("枠の大きさ（サーフェス短辺に対する割合）。ImageGeometry.DefaultCropFraction に合わせる。")]
    public float fraction = ImageGeometry.DefaultCropFraction;

    private RectTransform Surface => transform.parent as RectTransform;
    private Coroutine _settleRoutine;

    /// <summary>Shows the guide and fits it to the current surface size.</summary>
    public void Show()
    {
        SetPartsActive(true);
        Refresh();

        // The host panel is usually activated on the same frame, so the surface still holds its
        // pre-layout size here; re-fit over the next couple of frames once layout has settled.
        if (_settleRoutine != null)
        {
            StopCoroutine(_settleRoutine);
        }

        if (isActiveAndEnabled)
        {
            _settleRoutine = StartCoroutine(RefreshWhileSettling());
        }
    }

    public void Hide()
    {
        if (_settleRoutine != null)
        {
            StopCoroutine(_settleRoutine);
            _settleRoutine = null;
        }

        SetPartsActive(false);
    }

    private IEnumerator RefreshWhileSettling()
    {
        for (var i = 0; i < 2; i++)
        {
            yield return null;
            Refresh();
        }

        _settleRoutine = null;
    }

    /// <summary>
    /// Re-fits the frame and surround to the surface's current size. Safe to call on every
    /// activation and after a layout or AspectRatioFitter change; never call it per frame.
    /// </summary>
    public void Refresh()
    {
        var surface = Surface;
        if (surface == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        var size = surface.rect.size;
        if (size.x <= 0f || size.y <= 0f)
        {
            return;
        }

        var side = RegistrationGuideLayout.FrameSize(size.x, size.y, fraction);

        if (frame != null)
        {
            Center(frame);
            frame.sizeDelta = new Vector2(side, side);
        }

        if (maskTop == null && maskBottom == null && maskLeft == null && maskRight == null)
        {
            return;
        }

        var edges = RegistrationMaskLayout.EdgeRects(size.x, size.y, side);
        Apply(maskTop, edges.Top);
        Apply(maskBottom, edges.Bottom);
        Apply(maskLeft, edges.Left);
        Apply(maskRight, edges.Right);
    }

    private void SetPartsActive(bool on)
    {
        if (frame != null) frame.gameObject.SetActive(on);
        if (maskTop != null) maskTop.gameObject.SetActive(on);
        if (maskBottom != null) maskBottom.gameObject.SetActive(on);
        if (maskLeft != null) maskLeft.gameObject.SetActive(on);
        if (maskRight != null) maskRight.gameObject.SetActive(on);
    }

    private static void Center(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
    }

    private static void Apply(RectTransform rt, RegistrationMaskLayout.Strip strip)
    {
        if (rt == null)
        {
            return;
        }

        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(strip.Width, strip.Height);
        rt.anchoredPosition = new Vector2(strip.CenterX, strip.CenterY);
    }
}
