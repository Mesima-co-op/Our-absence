using UnityEngine;

/// <summary>
/// Animation Test Ground: loops one animation on a stripped copy of the player, imported exactly as the game
/// uses it (root motion ignored, layers off), so problems are easy to spot and tune.
/// - With a moveset step, its values are read live: edit Data/Combat/Moveset_*.asset during Play and the
///   speed, hit window (label turns red), lunge, mirror, blade direction and reach update at once.
/// - The green bar on the ground marks where the player faces; the red bar marks the moveset's reach.
/// Time is driven by AnimationTestGround (pause, frame step, slow motion).
/// Built by Our Absence > Testing > Build Animation Test Ground.
/// </summary>
[RequireComponent(typeof(Animator))]
public class ClipPreviewer : MonoBehaviour
{
    public const string State = "Clip";
    public const string MirrorParam = "mirror";

    [Tooltip("The moveset step to play (values are read live from the asset)")]
    public WeaponMoveset moveset;
    public int step = -1;
    [Tooltip("Play the step's recovery clip instead of the attack")]
    public bool recovery;
    [Tooltip("A plain clip, used when no moveset step is set")]
    public AnimationClip clip;
    [Tooltip("Shown instead of the clip's name (Mixamo clips are all called mixamo.com)")]
    public string clipTitle;
    [Tooltip("Preview the left hand (mirrored), as LMB plays it")]
    public bool leftHand;
    [Tooltip("Held weapon (a stripped copy of the weapon prefab); aligned to the fist with the moveset's blade direction")]
    public WeaponGrip weapon;
    [Tooltip("Seconds the last frame is held before looping")]
    public float loopPause = 0.5f;

    [Header("Scene parts (set by the builder)")]
    public TextMesh label;
    public Transform reachMarker;

    private Animator animator;
    private AnimatorOverrideController overrides;
    private float time;        // seconds into the clip
    private float hold;        // seconds left of the pause at the end of a loop
    private Vector3 home;
    private Vector3 alignedBlade = new Vector3(float.NaN, 0f, 0f);
    private bool alignedLeft;

    public WeaponMoveset.Step Step => moveset != null && step >= 0 && step < moveset.combo.Length ? moveset.combo[step] : null;
    public AnimationClip Clip => Step != null ? (recovery ? Step.recovery : Step.clip) : clip;
    public float Speed => Step != null && !recovery ? Mathf.Max(0.05f, Step.speed) : 1f;
    /// <summary>0-1 through the clip</summary>
    public float Normalized => Clip != null && Clip.length > 0f ? Mathf.Clamp01(time / Clip.length) : 0f;
    public bool InHitWindow => Step != null && !recovery && Normalized >= Step.hitStart && Normalized <= Step.hitEnd;
    public string Title => Step != null
        ? $"{moveset.displayName} {step + 1}{(recovery ? " rec" : "")}\n{(Clip != null ? Clip.name : "(none)")}"
        : !string.IsNullOrEmpty(clipTitle) ? clipTitle : Clip != null ? Clip.name : "(none)";

    private void Awake()
    {
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false; // the game ignores attack root motion too
        overrides = new AnimatorOverrideController(animator.runtimeAnimatorController);
        animator.runtimeAnimatorController = overrides;
        animator.speed = 0f; // time is set every frame
        home = transform.position;
        ApplyClip();
    }

    private AnimationClip applied;

    private void ApplyClip()
    {
        var c = Clip;
        if (c == applied) return;
        applied = c;
        var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>();
        overrides.GetOverrides(list);
        for (int i = 0; i < list.Count; i++)
            list[i] = new System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>(list[i].Key, c);
        overrides.ApplyOverrides(list);
    }

    /// <summary>Back to the first frame.</summary>
    public void Restart()
    {
        time = 0f;
        hold = 0f;
    }

    /// <summary>Advances the clip by <paramref name="seconds"/> of game time (at the step's speed).</summary>
    public void Advance(float seconds)
    {
        var c = Clip;
        if (c == null) return;
        if (hold > 0f)
        {
            hold -= seconds;
            if (hold <= 0f) time = 0f;
            return;
        }
        time += seconds * Speed;
        if (time >= c.length)
        {
            time = c.length;
            hold = Mathf.Max(0.0001f, loopPause);
        }
    }

    /// <summary>Steps back by <paramref name="seconds"/> of game time (frame stepping while paused).</summary>
    public void StepBack(float seconds)
    {
        hold = 0f;
        time = Mathf.Max(0f, time - seconds * Speed);
    }

    private void Update()
    {
        ApplyClip();
        var c = Clip;
        if (c == null) return;
        if (AnimationTestGround.Instance == null) Advance(Time.deltaTime); // works without the controller too

        // Same mirroring as PlayerCombat: clips are right-handed, a left-handed blow is mirrored back
        bool mirror = Step != null ? Step.mirror == !leftHand : leftHand;
        animator.SetBool(MirrorParam, mirror);
        animator.Play(State, 0, Normalized);

        // The step's lunge, as PlayerCombat spreads it until the end of the hit window
        float lunge = 0f;
        if (Step != null && !recovery && Step.lunge > 0f)
            lunge = Step.lunge * Mathf.Clamp01(Normalized / Mathf.Max(Step.hitEnd, 0.01f));
        transform.position = home + transform.forward * lunge;

        if (reachMarker != null && moveset != null)
            reachMarker.localPosition = new Vector3(0f, 0.01f, moveset.reach);
    }

    private void LateUpdate()
    {
        // The fist is only posed after the animator has run; re-align when the blade direction is tuned
        if (weapon != null && moveset != null && (moveset.bladeInFist != alignedBlade || leftHand != alignedLeft))
        {
            var hand = animator.GetBoneTransform(leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            weapon.transform.SetParent(hand, true);
            if (weapon.AlignToHand(animator, !leftHand, moveset.bladeInFist))
            {
                alignedBlade = moveset.bladeInFist;
                alignedLeft = leftHand;
            }
        }

        if (label != null)
        {
            label.text = $"{Title}\n{Normalized:0.00}{(InHitWindow ? "  HIT" : "")}";
            label.color = InHitWindow ? new Color(1f, 0.25f, 0.2f) : Color.white;
            var cam = Camera.main;
            if (cam != null) label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.transform.position);
        }
    }
}
