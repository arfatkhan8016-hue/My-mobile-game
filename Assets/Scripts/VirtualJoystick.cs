using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// On-screen joystick. Put this on a transparent Image covering the LEFT half of the screen.
/// Works with multi-touch (tracks only its own pointer) and with both Input Systems.
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private RectTransform background;   // joystick base (anchor: middle-center)
    [SerializeField] private RectTransform handle;       // child of background (anchor: middle-center)
    [SerializeField] private float radius = 100f;        // max handle travel in canvas units
    [SerializeField, Range(0f, 0.5f)] private float deadZone = 0.1f;
    [SerializeField] private bool floating = true;       // base appears where the thumb lands

    public Vector2 Value { get; private set; }

    private RectTransform area;
    private Vector2 defaultPosition;
    private int pointerId = -1;

    private void Awake()
    {
        area = (RectTransform)transform;
        defaultPosition = background.anchoredPosition;
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (pointerId != -1) return;
        pointerId = e.pointerId;

        if (floating)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, e.position, e.pressEventCamera, out Vector2 local);
            background.anchoredPosition = local;
        }
        Process(e);
    }

    public void OnDrag(PointerEventData e)
    {
        if (e.pointerId == pointerId) Process(e);
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (e.pointerId != pointerId) return;
        pointerId = -1;
        Value = Vector2.zero;
        handle.anchoredPosition = Vector2.zero;
        if (floating) background.anchoredPosition = defaultPosition;
    }

    private void Process(PointerEventData e)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(background, e.position, e.pressEventCamera, out Vector2 p);
        Vector2 v = Vector2.ClampMagnitude(p / radius, 1f);
        handle.anchoredPosition = v * radius;
        Value = v.magnitude < deadZone ? Vector2.zero : v;
    }

    private void OnDisable()
    {
        pointerId = -1;
        Value = Vector2.zero;
    }
}
