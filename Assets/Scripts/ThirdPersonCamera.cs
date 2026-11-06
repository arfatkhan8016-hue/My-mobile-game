using UnityEngine;

/// <summary>
/// Over-the-shoulder TPS camera: orbit, pinch zoom, sphere-cast collision, aim (ADS) transition.
/// Attach to the Main Camera (NOT as a child of the player).
/// </summary>
[RequireComponent(typeof(Camera))]
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;                 // Player root
    [SerializeField] private TPSCharacterController character; // optional, lowers camera when crouching
    [SerializeField] private float pivotHeight = 1.6f;
    [SerializeField] private float crouchPivotDrop = 0.5f;
    [SerializeField] private float shoulderOffset = 0.5f;      // + = right shoulder

    [Header("Look")]
    [SerializeField] private float sensitivity = 0.12f;        // degrees per pixel
    [SerializeField] private float minPitch = -35f;
    [SerializeField] private float maxPitch = 65f;
    [SerializeField] private float startPitch = 15f;

    [Header("Zoom")]
    [SerializeField] private float distance = 4f;
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxDistance = 6f;
    [SerializeField] private float pinchSpeed = 0.01f;
    [SerializeField] private float zoomSmoothTime = 0.08f;

    [Header("Aim (ADS)")]
    [SerializeField] private float aimDistance = 1.8f;
    [SerializeField] private float aimShoulderOffset = 0.7f;
    [SerializeField] private float normalFov = 60f;
    [SerializeField] private float aimFov = 45f;
    [SerializeField] private float aimBlendSpeed = 8f;

    [Header("Collision")]
    [SerializeField] private LayerMask collisionMask = ~0;     // EXCLUDE the Player layer
    [SerializeField] private float collisionRadius = 0.25f;
    [SerializeField] private float collisionBuffer = 0.1f;
    [SerializeField] private float minCollisionDistance = 0.4f;

    [Header("Smoothing")]
    [SerializeField] private float followSmoothTime = 0.04f;

    public float Yaw => yaw;
    public float Pitch => pitch;

    private Camera cam;
    private float yaw, pitch;
    private float desiredDistance;
    private float currentDistance = -1f;
    private float distanceVelocity;
    private Vector3 pivotPos, pivotVelocity;
    private float crouchT, aimT;
    private bool aiming;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        desiredDistance = distance;
    }

    private void Start()
    {
        if (!target) { enabled = false; Debug.LogError("ThirdPersonCamera: assign a target."); return; }
        yaw = target.eulerAngles.y;
        pitch = startPitch;
        pivotPos = target.position + Vector3.up * pivotHeight;
    }

    public void SetAiming(bool value) => aiming = value;

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        MobileInput input = MobileInput.Instance;

        if (input != null)
        {
            Vector2 look = input.ConsumeLook();
            yaw += look.x * sensitivity;
            pitch = Mathf.Clamp(pitch - look.y * sensitivity, minPitch, maxPitch);
            desiredDistance = Mathf.Clamp(desiredDistance - input.ConsumePinch() * pinchSpeed, minDistance, maxDistance);
        }

        if (character) crouchT = Mathf.MoveTowards(crouchT, character.IsCrouching ? 1f : 0f, dt * 4f);
        aimT = Mathf.MoveTowards(aimT, aiming ? 1f : 0f, dt * aimBlendSpeed);

        // Pivot (smoothed)
        Vector3 targetPivot = target.position + Vector3.up * (pivotHeight - crouchPivotDrop * crouchT);
        pivotPos = Vector3.SmoothDamp(pivotPos, targetPivot, ref pivotVelocity, followSmoothTime);

        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);

        float shoulder = Mathf.Lerp(shoulderOffset, aimShoulderOffset, aimT);
        float wantedDist = Mathf.Lerp(desiredDistance, aimDistance, aimT);

        Vector3 shoulderPoint = pivotPos + yawRot * Vector3.right * shoulder;
        Vector3 desiredPos = shoulderPoint - rot * Vector3.forward * wantedDist;

        // Collision: cast from the pivot (inside the player's torso) to the desired position
        Vector3 dir = desiredPos - pivotPos;
        float len = dir.magnitude;
        dir /= Mathf.Max(len, 0.0001f);

        float allowed = len;
        if (Physics.SphereCast(pivotPos, collisionRadius, dir, out RaycastHit hit, len, collisionMask, QueryTriggerInteraction.Ignore))
            allowed = Mathf.Max(hit.distance - collisionBuffer, minCollisionDistance);

        if (currentDistance < 0f || allowed < currentDistance)
        {
            currentDistance = allowed;          // snap in instantly (never clip through walls)
            distanceVelocity = 0f;
        }
        else
        {
            currentDistance = Mathf.SmoothDamp(currentDistance, allowed, ref distanceVelocity, zoomSmoothTime);
        }

        transform.SetPositionAndRotation(pivotPos + dir * currentDistance, rot);

        float fov = Mathf.Lerp(normalFov, aimFov, aimT);
        if (!Mathf.Approximately(cam.fieldOfView, fov)) cam.fieldOfView = fov;
    }
}
