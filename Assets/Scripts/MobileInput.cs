using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Single place the rest of the game reads input from.
/// Put on your Canvas. Also sets 60 FPS and keeps the screen awake.
/// In the Editor / desktop: WASD + Shift (run), C (crouch), hold Right Mouse to look, wheel to zoom.
/// </summary>
public class MobileInput : MonoBehaviour
{
    public static MobileInput Instance { get; private set; }

    [Header("UI Sources")]
    [SerializeField] private VirtualJoystick moveJoystick;
    [SerializeField] private TouchLookPad lookPad;

    [Header("Settings")]
    [SerializeField, Range(0.5f, 1f)] private float runThreshold = 0.85f; // push joystick past this = run
    [SerializeField] private bool enableEditorFallback = true;
    [SerializeField] private int targetFrameRate = 60;

    public Vector2 Move { get; private set; }
    public bool Run { get; private set; }

    private bool crouchPressed;

    private void Awake()
    {
        Instance = this;
        Application.targetFrameRate = targetFrameRate;
        QualitySettings.vSyncCount = 0;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        Vector2 m = moveJoystick ? moveJoystick.Value : Vector2.zero;

#if UNITY_EDITOR || UNITY_STANDALONE
        if (enableEditorFallback)
        {
            Vector2 kb = ReadKeyboard();
            if (m.sqrMagnitude < 0.0001f) m = kb;
        }
#endif
        Move = Vector2.ClampMagnitude(m, 1f);
        Run = Move.magnitude >= runThreshold;
    }

    // ---- Called by UI Button (OnClick) ----
    public void ToggleCrouch() => crouchPressed = true;

    // ---- Polled by gameplay scripts ----
    public bool ConsumeCrouch()
    {
        bool r = crouchPressed;
        crouchPressed = false;
        return r;
    }

    /// <summary>Look delta in screen pixels since last call.</summary>
    public Vector2 ConsumeLook()
    {
        Vector2 d = lookPad ? lookPad.ConsumeLookDelta() : Vector2.zero;
#if UNITY_EDITOR || UNITY_STANDALONE
        if (enableEditorFallback) d += ReadMouseLook();
#endif
        return d;
    }

    /// <summary>Pinch spread in pixels (positive = fingers apart) since last call.</summary>
    public float ConsumePinch()
    {
        float p = lookPad ? lookPad.ConsumePinchDelta() : 0f;
#if UNITY_EDITOR || UNITY_STANDALONE
        if (enableEditorFallback) p += ReadScroll() * 50f;
#endif
        return p;
    }

    // ---------------- Editor / desktop fallback ----------------
#if UNITY_EDITOR || UNITY_STANDALONE
    private Vector2 ReadKeyboard()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        Vector2 v = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (v.sqrMagnitude > 1f) v.Normalize();
        v *= Input.GetKey(KeyCode.LeftShift) ? 1f : 0.5f;
        if (Input.GetKeyDown(KeyCode.C)) crouchPressed = true;
        return v;
#elif ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb == null) return Vector2.zero;
        Vector2 v = new Vector2(
            (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
            (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
        if (v.sqrMagnitude > 1f) v.Normalize();
        v *= kb.leftShiftKey.isPressed ? 1f : 0.5f;
        if (kb.cKey.wasPressedThisFrame) crouchPressed = true;
        return v;
#else
        return Vector2.zero;
#endif
    }

    private Vector2 ReadMouseLook()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!Input.GetMouseButton(1)) return Vector2.zero;
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
#elif ENABLE_INPUT_SYSTEM
        Mouse m = Mouse.current;
        if (m == null || !m.rightButton.isPressed) return Vector2.zero;
        return m.delta.ReadValue();
#else
        return Vector2.zero;
#endif
    }

    private float ReadScroll()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mouseScrollDelta.y;
#elif ENABLE_INPUT_SYSTEM
        Mouse m = Mouse.current;
        return m != null ? m.scroll.ReadValue().y / 120f : 0f;
#else
        return 0f;
#endif
    }
#endif
}
