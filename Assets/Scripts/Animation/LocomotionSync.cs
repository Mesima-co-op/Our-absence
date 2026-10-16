using UnityEngine;

/// <summary>
/// Keeps the player's legs in sync with how fast the body actually moves.
///
/// - Animator parameters come from the CharacterController's real velocity (not the stick),
///   so walking into a wall stops the legs, and starts/stops match the movement.
/// - The locomotion states play faster or slower ("moveAnimSpeed") so the feet cover exactly
///   the ground the body covers (no ice-skating).
/// - Root motion is ignored while walking/running (the code moves the player), but applied
///   during rolls and dodges so they travel the distance their animation shows.
///
/// Clip speeds are measured by Our Absence > Animation > Calibrate Player Locomotion,
/// which also adds the "moveAnimSpeed" parameter to the animator controller.
/// </summary>
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(CharacterController))]
public class LocomotionSync : MonoBehaviour
{
    public const string SpeedMultiplierParam = "moveAnimSpeed";

    [Header("Ground speed of each clip at normal playback (m/s, measured by the calibration tool)")]
    public float walkClipSpeed = 1.34f;
    public float runClipSpeed = 2.76f;
    public float combatWalkClipSpeed = 1.34f;
    public float combatRunClipSpeed = 3.47f;
    public float lockOnClipSpeed = 1.75f;

    [Header("Playback limits (outside these the feet slide a little instead of looking frantic)")]
    public float minPlayback = 0.6f;
    public float maxPlayback = 1.8f;

    [Header("States that move by their own animation (rolls, dodges)")]
    public string[] rootMotionStates = { "Roll", "Standing Dodge Backward", "Locked Roll", "Locked Standing Dodge Backward" };

    [Tooltip("Smoothing for the blend parameters (seconds)")]
    public float paramDamping = 0.08f;

    private Animator animator;
    private CharacterController controller;
    private int[] rootMotionHashes;
    private bool hasSpeedParam;

    public bool InRootMotionState { get; private set; }

    /// <summary>Set by the movement code each frame: the walk and run speeds it is using.</summary>
    public float WalkSpeed { get; set; } = 1.8f;
    public float RunSpeed { get; set; } = 4.2f;

    private static readonly int SpeedHash = Animator.StringToHash("speed");
    private static readonly int HorizontalHash = Animator.StringToHash("Horizontal");
    private static readonly int VerticalHash = Animator.StringToHash("Vertical");
    private static readonly int WeaponDrawnHash = Animator.StringToHash("isWeaponDrawn");
    private static readonly int MultiplierHash = Animator.StringToHash(SpeedMultiplierParam);

    private void Awake()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        rootMotionHashes = System.Array.ConvertAll(rootMotionStates, Animator.StringToHash);
        foreach (var p in animator.parameters)
            if (p.nameHash == MultiplierHash) hasSpeedParam = true;
        if (!hasSpeedParam)
            Debug.LogWarning("[LocomotionSync] Animator has no 'moveAnimSpeed' parameter. Run Our Absence > Animation > Calibrate Player Locomotion.", this);
    }

    private void Update()
    {
        InRootMotionState = IsInRootMotionState();

        Vector3 velocity = controller.velocity;
        velocity.y = 0f;
        float groundSpeed = velocity.magnitude;
        if (groundSpeed < 0.05f) groundSpeed = 0f;

        // 0 = idle, 1 = walk, 2 = run, matching the blend tree thresholds
        float blend = groundSpeed <= WalkSpeed
            ? groundSpeed / Mathf.Max(WalkSpeed, 0.01f)
            : 1f + Mathf.Clamp01((groundSpeed - WalkSpeed) / Mathf.Max(RunSpeed - WalkSpeed, 0.01f));
        animator.SetFloat(SpeedHash, blend, paramDamping, Time.deltaTime);

        // Lock-on strafing: direction relative to where the player faces, scaled so 1 = jog
        Vector3 local = transform.InverseTransformDirection(velocity);
        float strafeScale = Mathf.Max(lockOnClipSpeed, 0.01f);
        animator.SetFloat(HorizontalHash, Mathf.Clamp(local.x / strafeScale, -1f, 1f), paramDamping, Time.deltaTime);
        animator.SetFloat(VerticalHash, Mathf.Clamp(local.z / strafeScale, -1f, 1f), paramDamping, Time.deltaTime);

        if (hasSpeedParam)
            animator.SetFloat(MultiplierHash, PlaybackMultiplier(groundSpeed, blend));
    }

    private float PlaybackMultiplier(float groundSpeed, float blend)
    {
        if (groundSpeed <= 0.05f) return 1f;

        bool lockedOn = animator.layerCount > 1 && animator.GetLayerWeight(1) > 0.5f;
        float clipSpeed;
        if (lockedOn)
        {
            clipSpeed = lockOnClipSpeed;
        }
        else
        {
            bool combat = animator.GetBool(WeaponDrawnHash);
            float walk = combat ? combatWalkClipSpeed : walkClipSpeed;
            float run = combat ? combatRunClipSpeed : runClipSpeed;
            clipSpeed = blend <= 1f ? walk * Mathf.Max(blend, 0.3f) : Mathf.Lerp(walk, run, blend - 1f);
        }
        return Mathf.Clamp(groundSpeed / Mathf.Max(clipSpeed, 0.01f), minPlayback, maxPlayback);
    }

    private bool IsInRootMotionState()
    {
        for (int layer = 0; layer < Mathf.Min(animator.layerCount, 2); layer++)
        {
            if (animator.GetLayerWeight(layer) < 0.5f && layer > 0) continue;
            var state = animator.GetCurrentAnimatorStateInfo(layer);
            var next = animator.IsInTransition(layer) ? animator.GetNextAnimatorStateInfo(layer) : default;
            foreach (int hash in rootMotionHashes)
                if (state.shortNameHash == hash || (animator.IsInTransition(layer) && next.shortNameHash == hash))
                    return true;
        }
        return false;
    }

    private void OnAnimatorMove()
    {
        // Walking/running is moved by Player_controller; only rolls and dodges use the animation's motion
        if (!InRootMotionState || !controller.enabled) return;

        Vector3 delta = animator.deltaPosition;
        delta.y = 0f;
        controller.Move(delta);
    }
}
