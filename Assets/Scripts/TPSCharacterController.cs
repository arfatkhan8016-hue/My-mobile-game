using UnityEngine;

/// <summary>
/// Camera-relative movement: Idle / Walk / Run / Crouch, driven by joystick input.
/// Attach to the Player root (the object that has the CharacterController).
/// Pivot of the player root must be at the character's FEET.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class TPSCharacterController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private ThirdPersonCamera cameraRig;
    [SerializeField] private LayerMask obstacleMask = ~0;      // EXCLUDE the Player layer (ceiling check)

    [Header("Speeds (m/s)")]
    [SerializeField] private float walkSpeed = 2f;
    [SerializeField] private float runSpeed = 5f;
    [SerializeField] private float crouchSpeed = 1.5f;
    [SerializeField] private float acceleration = 12f;

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 720f;       // degrees / second
    public bool faceCameraDirection;                           // set true while aiming/shooting

    [Header("Gravity")]
    [SerializeField] private float gravity = -25f;

    [Header("Crouch capsule")]
    [SerializeField] private float standHeight = 1.8f;
    [SerializeField] private float crouchHeight = 1.2f;
    [SerializeField] private float capsuleChangeSpeed = 4f;

    [Header("Animator")]
    [SerializeField] private float speedDampTime = 0.1f;

    public bool IsCrouching { get; private set; }
    public bool IsGrounded => cc.isGrounded;
    public float CurrentSpeed => currentSpeed;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int CrouchHash = Animator.StringToHash("IsCrouching");
    private static readonly int GroundedHash = Animator.StringToHash("IsGrounded");

    private CharacterController cc;
    private Vector3 moveDir;
    private float currentSpeed;
    private float verticalVelocity;

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (animator) animator.applyRootMotion = false;

        cc.height = standHeight;
        cc.center = new Vector3(0f, standHeight * 0.5f, 0f);
        moveDir = transform.forward;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        MobileInput input = MobileInput.Instance;
        Vector2 move = input != null ? input.Move : Vector2.zero;

        if (input != null && input.ConsumeCrouch()) TryToggleCrouch();
        UpdateCapsule(dt);

        float camYaw = cameraRig ? cameraRig.Yaw : transform.eulerAngles.y;
        Quaternion yawRot = Quaternion.Euler(0f, camYaw, 0f);

        bool hasInput = move.sqrMagnitude > 0.01f;
        float targetSpeed = 0f;

        if (hasInput)
        {
            moveDir = (yawRot * new Vector3(move.x, 0f, move.y)).normalized;
            targetSpeed = IsCrouching ? crouchSpeed : (input != null && input.Run ? runSpeed : walkSpeed);
        }

        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * dt);

        // Rotation
        Vector3 faceDir = faceCameraDirection ? yawRot * Vector3.forward : (hasInput ? moveDir : Vector3.zero);
        if (faceDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(faceDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * dt);
        }

        // Gravity + move (single CharacterController.Move per frame)
        if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * dt;

        Vector3 velocity = moveDir * currentSpeed + Vector3.up * verticalVelocity;
        cc.Move(velocity * dt);

        // Animator
        if (animator)
        {
            animator.SetFloat(SpeedHash, currentSpeed, speedDampTime, dt);
            animator.SetBool(CrouchHash, IsCrouching);
            animator.SetBool(GroundedHash, cc.isGrounded);
        }
    }

    private void TryToggleCrouch()
    {
        if (IsCrouching)
        {
            // Don't stand up under a low ceiling
            float r = cc.radius * 0.9f;
            Vector3 top = transform.position + Vector3.up * (standHeight - r);
            if (Physics.CheckSphere(top, r, obstacleMask, QueryTriggerInteraction.Ignore)) return;
        }
        IsCrouching = !IsCrouching;
    }

    private void UpdateCapsule(float dt)
    {
        float target = IsCrouching ? crouchHeight : standHeight;
        if (Mathf.Approximately(cc.height, target)) return;
        float h = Mathf.MoveTowards(cc.height, target, capsuleChangeSpeed * dt);
        cc.height = h;
        cc.center = new Vector3(0f, h * 0.5f, 0f);
    }
}
