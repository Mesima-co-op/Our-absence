using UnityEngine;

/// <summary>
/// Dust for movement: a puff under each foot as it lands while moving, a burst when a roll or dodge
/// starts, and a ring of dust plus a camera bump on landing from a fall.
/// Works on any humanoid with a CharacterController (the player and the enemies). Effects come from FXLibrary.
/// </summary>
[RequireComponent(typeof(Animator))]
public class MovementFX : MonoBehaviour
{
    [Header("Footsteps")]
    [Tooltip("No puffs below this ground speed (m/s)")]
    public float minStepSpeed = 1.2f;
    [Tooltip("Speed at which the puffs reach full size (m/s)")]
    public float fullStepSpeed = 4.5f;
    [Tooltip("How high a foot must lift (m) before its next landing counts as a step")]
    public float stepLift = 0.07f;

    [Header("Landing")]
    [Tooltip("Seconds in the air before a landing makes dust")]
    public float minAirTime = 0.3f;
    [Tooltip("Fall speed (m/s) for the biggest landing")]
    public float hardLandingSpeed = 9f;
    [Tooltip("Shake the camera on landing (only for the player)")]
    public bool shakeOnLanding = true;

    Animator animator;
    CharacterController controller;
    LocomotionSync locomotion;
    Transform leftFoot, rightFoot;
    float leftRest, rightRest;
    bool leftLifted, rightLifted;
    Vector3 lastPosition;
    Vector3 velocity;
    float airTime, fallSpeed;
    bool wasRolling;
    bool isPlayer;

    void Start()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        locomotion = GetComponent<LocomotionSync>();
        isPlayer = GetComponent<Player_controller>() != null;
        if (animator.isHuman)
        {
            leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        }
        // A foot's lowest height above the ground is its "planted" height; start from a guess and learn it
        leftRest = rightRest = 0.1f * transform.lossyScale.y;
        lastPosition = transform.position;
    }

    void LateUpdate()
    {
        var lib = FXLibrary.Instance;
        if (lib == null || Time.deltaTime <= 0f) return;

        Vector3 position = transform.position;
        velocity = (position - lastPosition) / Time.deltaTime;
        lastPosition = position;
        float groundSpeed = new Vector2(velocity.x, velocity.z).magnitude;
        // Enemies move by root motion with no downward push, so isGrounded alone flickers; probe as well
        bool grounded = controller == null || controller.isGrounded
            || Physics.Raycast(position + Vector3.up * 0.1f, Vector3.down, 0.3f, ~0, QueryTriggerInteraction.Ignore);

        Landing(lib, grounded);
        Roll(lib, grounded);
        if (grounded && leftFoot != null)
        {
            Step(lib, leftFoot, ref leftRest, ref leftLifted, groundSpeed);
            Step(lib, rightFoot, ref rightRest, ref rightLifted, groundSpeed);
        }
    }

    void Step(FXLibrary lib, Transform foot, ref float rest, ref bool lifted, float groundSpeed)
    {
        float height = foot.position.y - transform.position.y;
        rest = Mathf.Min(rest, height);
        rest = Mathf.Lerp(rest, height, 0.002f); // slowly forget, in case of slopes

        if (height > rest + stepLift) { lifted = true; return; }
        if (!lifted || height > rest + stepLift * 0.35f) return;
        lifted = false;

        if (groundSpeed < minStepSpeed) return;
        float k = Mathf.InverseLerp(minStepSpeed, fullStepSpeed, groundSpeed);
        Vector3 at = new Vector3(foot.position.x, transform.position.y + 0.02f, foot.position.z);
        // Kicked back, against the direction of travel
        Vector3 back = -new Vector3(velocity.x, 0, velocity.z).normalized + Vector3.up * 0.6f;
        FX.Play(lib.footstepDust, at, back, Mathf.Lerp(0.55f, 1.15f, k) * transform.lossyScale.y);
    }

    void Roll(FXLibrary lib, bool grounded)
    {
        bool rolling = locomotion != null && locomotion.InRootMotionState;
        if (rolling && !wasRolling && grounded)
        {
            Vector3 back = -transform.forward + Vector3.up * 0.4f;
            FX.Play(lib.rollDust, transform.position + Vector3.up * 0.05f, back, transform.lossyScale.y);
        }
        wasRolling = rolling;
    }

    void Landing(FXLibrary lib, bool grounded)
    {
        if (!grounded)
        {
            airTime += Time.deltaTime;
            fallSpeed = Mathf.Max(fallSpeed, -velocity.y);
            return;
        }

        if (airTime >= minAirTime && fallSpeed > 2f)
        {
            float k = Mathf.Clamp01(fallSpeed / hardLandingSpeed);
            FX.Play(lib.landDust, transform.position + Vector3.up * 0.03f, Vector3.up, Mathf.Lerp(0.6f, 1.4f, k) * transform.lossyScale.y);
            if (isPlayer && shakeOnLanding)
                CombatFeedback.Bump(transform.position, lib.shakeLand * k);
        }
        airTime = 0f;
        fallSpeed = 0f;
    }
}
