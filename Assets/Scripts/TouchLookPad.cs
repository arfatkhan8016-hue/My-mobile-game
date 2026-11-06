using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Swipe-to-look area (RIGHT half of the screen). One finger = look, two fingers = pinch zoom.
/// Uses pointer events, so it works with the old and new Input System and never allocates per frame.
/// </summary>
public class TouchLookPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private int idA = -1, idB = -1;
    private Vector2 posA, posB;
    private Vector2 lookDelta;
    private float pinchDelta;
    private float lastPinchDistance;

    public Vector2 ConsumeLookDelta()
    {
        Vector2 d = lookDelta;
        lookDelta = Vector2.zero;
        return d;
    }

    public float ConsumePinchDelta()
    {
        float d = pinchDelta;
        pinchDelta = 0f;
        return d;
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (idA < 0)
        {
            idA = e.pointerId;
            posA = e.position;
        }
        else if (idB < 0)
        {
            idB = e.pointerId;
            posB = e.position;
            lastPinchDistance = (posA - posB).magnitude;
        }
    }

    public void OnDrag(PointerEventData e)
    {
        if (e.pointerId == idA)
        {
            if (idB < 0) lookDelta += e.delta;
            posA = e.position;
        }
        else if (e.pointerId == idB)
        {
            posB = e.position;
        }
        else return;

        if (idA >= 0 && idB >= 0)
        {
            float d = (posA - posB).magnitude;
            pinchDelta += d - lastPinchDistance;
            lastPinchDistance = d;
        }
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (e.pointerId == idA)
        {
            idA = idB;       // promote second finger
            posA = posB;
            idB = -1;
        }
        else if (e.pointerId == idB)
        {
            idB = -1;
        }
    }

    private void OnDisable()
    {
        idA = idB = -1;
        lookDelta = Vector2.zero;
        pinchDelta = 0f;
    }
}
