using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Combos for each hand, matched to the weapon in it.
/// - Each hand has its own animator layer (AttackRight_layer / AttackLeft_layer, the left one mirrored) with
///   states Hit1..Hit4 and Rec1..Rec4. Their placeholder clips (R_Hit1, L_Rec2, ...) are swapped for the
///   weapon's WeaponMoveset clips through an AnimatorOverrideController.
/// - Pressing attack starts the combo; pressing again during a swing queues the next step, which starts
///   once the current one reaches its chainFrom point. When the presses stop, the step's recovery plays
///   (moving cancels it) and the hand returns to its idle.
/// - The weapon only deals damage inside each step's hit window. Bare hands hit with the fists.
/// - Taking damage interrupts the combo and plays a hit reaction (HitReact_layer).
/// The layers, placeholders and movesets are set up by Our Absence > Combat > Build Weapons And Movesets.
/// </summary>
[RequireComponent(typeof(Animator))]
public class PlayerCombat : MonoBehaviour
{
    public const int MaxSteps = 4;
    public const string RightLayer = "AttackRight_layer";
    public const string LeftLayer = "AttackLeft_layer";
    public const string HitReactLayer = "HitReact_layer";
    public const string IdleState = "Default";

    public static string SlotClip(bool right, bool recovery, int step) => $"{(right ? "R" : "L")}_{(recovery ? "Rec" : "Hit")}{step + 1}";
    public static string StateName(bool recovery, int step) => $"{(recovery ? "Rec" : "Hit")}{step + 1}";
    public static string SpeedParam(bool right) => right ? "attackSpeedR" : "attackSpeedL";

    [Header("Bare hands")]
    [SerializeField] private WeaponMoveset unarmed;
    [SerializeField] private float fistDamage = 8f;

    [Header("Aim and lunge")]
    [Tooltip("Each swing turns toward the nearest enemy within this distance in front of the player")]
    [SerializeField] private float aimDistance = 3f;
    [SerializeField] private float aimAngle = 75f;
    [Tooltip("Stop lunging when this close to the target (m, centre to centre)")]
    [SerializeField] private float lungeStopDistance = 0.75f;

    [Header("Blending")]
    [SerializeField] private float blendIn = 0.06f;
    [SerializeField] private float blendOut = 0.25f;

    [Header("Hit reactions")]
    [Tooltip("States on HitReact_layer, played at random when the player takes damage")]
    [SerializeField] private string[] hitReactionStates = { "HitChest", "HitHead" };
    [Tooltip("Smaller hits (like standing in a fire) don't make the player flinch")]
    [SerializeField] private int minDamageToFlinch = 8;

    private class Hand
    {
        public bool right;
        public int layer;
        public WeaponMoveset set;
        public WeaponDamageSystem weapon;
        public int step = -1;
        public bool recovering;
        public int stateHash;
        public float pressedAt = -99f;
        public bool windowOpen;
        public float lunged; // 0-1 of this step's lunge already applied
        public readonly HashSet<HealthSystem> punched = new HashSet<HealthSystem>();
    }

    private CharacterController controller;
    private Transform aimTarget;

    private Animator animator;
    private AnimatorOverrideController overrides;
    private readonly Dictionary<string, AnimationClip> placeholders = new Dictionary<string, AnimationClip>();
    private Hand rightHand, leftHand;
    private int hitReactLayer = -1;
    private HealthSystem health;
    private int lastHealth;

    private static readonly int SpeedHash = Animator.StringToHash("speed");

    /// <summary>True while either hand is in the middle of an attack (not its recovery).</summary>
    public bool IsAttacking => Busy(rightHand) || Busy(leftHand);

    private static bool Busy(Hand h) => h != null && h.step >= 0 && !h.recovering;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        rightHand =new Hand { right = true, layer = animator.GetLayerIndex(RightLayer) };
        leftHand = new Hand { right = false, layer = animator.GetLayerIndex(LeftLayer) };
        hitReactLayer = animator.GetLayerIndex(HitReactLayer);
        if (rightHand.layer < 0 || leftHand.layer < 0)
        {
            Debug.LogWarning("[Combat] The animator has no attack layers. Run Our Absence > Combat > Build Weapons And Movesets.", this);
            enabled = false;
            return;
        }

        overrides = new AnimatorOverrideController(animator.runtimeAnimatorController) { name = "Player Combat Overrides" };
        var list = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        overrides.GetOverrides(list);
        foreach (var pair in list)
            if (pair.Key != null) placeholders[pair.Key.name] = pair.Key;
        animator.runtimeAnimatorController = overrides;

        SetMoveset(true, null);
        SetMoveset(false, null);
    }

    private void OnEnable()
    {
        health = GetComponent<HealthSystem>();
        if (health != null)
        {
            lastHealth = health.Health;
            health.OnHealthChanged += HandleHealthChanged;
        }
    }

    private void OnDisable()
    {
        if (health != null) health.OnHealthChanged -= HandleHealthChanged;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Movesets
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Which moveset a hand uses (null = bare hands). Call when the equipped weapon changes.</summary>
    public void SetMoveset(bool right, WeaponMoveset set)
    {
        if (overrides == null) return;
        var hand = right ? rightHand : leftHand;
        set = set != null && set.combo.Length > 0 ? set : unarmed;
        if (hand.set == set) return;
        if (hand.step >= 0) EndCombo(hand);
        hand.set = set;
        if (set == null) return;

        var changes = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        for (int i = 0; i < MaxSteps; i++)
        {
            // Steps past the end of a short combo are never played; reuse the last one so nothing is empty
            var step = set.combo[Mathf.Min(i, set.combo.Length - 1)];
            AddOverride(changes, SlotClip(right, false, i), step.clip);
            AddOverride(changes, SlotClip(right, true, i), step.recovery != null ? step.recovery : step.clip);
        }
        overrides.ApplyOverrides(changes);
    }

    private void AddOverride(List<KeyValuePair<AnimationClip, AnimationClip>> changes, string slot, AnimationClip clip)
    {
        if (placeholders.TryGetValue(slot, out var original))
            changes.Add(new KeyValuePair<AnimationClip, AnimationClip>(original, clip));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Attacking
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>An attack press for one hand. <paramref name="weapon"/> is the held weapon (null = fist).</summary>
    public void Attack(bool right, WeaponMoveset set, WeaponDamageSystem weapon)
    {
        if (!enabled) return;
        var hand = right ? rightHand : leftHand;
        SetMoveset(right, set);
        if (hand.set == null) return;
        hand.weapon = weapon;

        if (hand.step < 0 || hand.recovering) StartStep(hand, 0);
        else hand.pressedAt = Time.time; // queue the next step
    }

    private void StartStep(Hand hand, int index)
    {
        var step = hand.set.combo[index];
        string state = StateName(false, index);
        animator.SetFloat(SpeedParam(hand.right), Mathf.Max(0.05f, step.speed));
        animator.CrossFadeInFixedTime(state, blendIn, hand.layer, 0f);
        hand.stateHash = Animator.StringToHash(state);
        hand.step = index;
        hand.recovering = false;
        hand.pressedAt = -99f;
        hand.lunged = 0f;
        hand.punched.Clear();
        SetWindow(hand, false);
        AimAtNearestEnemy();
        if (hand.weapon != null) hand.weapon.BeginSwing(step.damage);
        animator.SetBool(hand.right ? "isAttackingRight" : "isAttackingLeft", true);
    }

    private void StartRecovery(Hand hand)
    {
        string state = StateName(true, hand.step);
        animator.CrossFadeInFixedTime(state, 0.1f, hand.layer, 0f);
        hand.stateHash = Animator.StringToHash(state);
        hand.recovering = true;
        SetWindow(hand, false);
        // The player may move again; moving cancels the recovery
        animator.SetBool(hand.right ? "isAttackingRight" : "isAttackingLeft", false);
    }

    private void EndCombo(Hand hand)
    {
        if (hand.layer >= 0) animator.CrossFadeInFixedTime(IdleState, blendOut, hand.layer);
        hand.step = -1;
        hand.recovering = false;
        SetWindow(hand, false);
        if (hand.weapon != null) hand.weapon.SwingTrail = false;
        animator.SetBool(hand.right ? "isAttackingRight" : "isAttackingLeft", false);
    }

    private void Update()
    {
        Tick(rightHand);
        Tick(leftHand);
    }

    private void Tick(Hand hand)
    {
        if (hand.step < 0) return;
        float t = NormalizedTime(hand);
        if (t < 0f) return; // the crossfade hasn't reached our state yet

        if (hand.recovering)
        {
            if (t >= 0.98f || animator.GetFloat(SpeedHash) > 0.4f) EndCombo(hand);
            return;
        }

        var step = hand.set.combo[hand.step];
        Lunge(hand, step, t);
        SetWindow(hand, t >= step.hitStart && t <= step.hitEnd);
        if (hand.weapon != null) hand.weapon.SwingTrail = t >= step.hitStart - 0.15f && t <= step.hitEnd + 0.1f;
        if (hand.windowOpen) ReachHits(hand, step);

        bool queued = Time.time - hand.pressedAt <= hand.set.inputBuffer;
        bool hasNext = hand.step + 1 < hand.set.combo.Length;
        if (queued && t >= step.chainFrom)
        {
            // Next step, or around again after the finisher
            StartStep(hand, hasNext ? hand.step + 1 : 0);
            return;
        }
        if (t >= 0.98f)
        {
            if (step.recovery != null) StartRecovery(hand);
            else EndCombo(hand);
        }
    }

    /// <summary>Normalized time of the hand's current attack state, or -1 if it isn't playing yet.</summary>
    private float NormalizedTime(Hand hand)
    {
        var current = animator.GetCurrentAnimatorStateInfo(hand.layer);
        if (current.shortNameHash == hand.stateHash) return current.normalizedTime;
        if (animator.IsInTransition(hand.layer))
        {
            var next = animator.GetNextAnimatorStateInfo(hand.layer);
            if (next.shortNameHash == hand.stateHash) return next.normalizedTime;
        }
        return -1f;
    }

    private void SetWindow(Hand hand, bool open)
    {
        hand.windowOpen = open;
        if (hand.weapon != null) hand.weapon.HitWindowOpen = open;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Aim and lunge
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Turns the player toward the closest living enemy in front of them (soft lock).</summary>
    private void AimAtNearestEnemy()
    {
        aimTarget = null;
        float best = float.MaxValue;
        foreach (var target in FindObjectsOfType<HealthSystem>())
        {
            if (target.IsDead || target.transform.root == transform.root || !target.CompareTag("Enemy")) continue;
            Vector3 to = target.transform.position - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance > aimDistance || distance < 0.01f) continue;
            if (Vector3.Angle(transform.forward, to) > aimAngle) continue;
            if (distance < best) { best = distance; aimTarget = target.transform; }
        }
        if (aimTarget != null)
        {
            Vector3 to = aimTarget.position - transform.position;
            to.y = 0f;
            transform.rotation = Quaternion.LookRotation(to);
        }
    }

    /// <summary>Steps forward during the swing, until the hit lands or the target is close enough.</summary>
    private void Lunge(Hand hand, WeaponMoveset.Step step, float t)
    {
        if (controller == null || !controller.enabled || step.lunge <= 0f) return;
        float progress = Mathf.Clamp01(t / Mathf.Max(step.hitEnd, 0.01f));
        float delta = (progress - hand.lunged) * step.lunge;
        hand.lunged = progress;
        if (delta <= 0f) return;
        if (aimTarget != null)
        {
            Vector3 to = aimTarget.position - transform.position;
            to.y = 0f;
            if (to.magnitude <= lungeStopDistance) return;
        }
        controller.Move(transform.forward * delta);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Bare hands
    // ─────────────────────────────────────────────────────────────────────

    private readonly Collider[] overlap = new Collider[32];

    /// <summary>
    /// The swing's reach: while the hit window is open, enemies in front of the player within the moveset's
    /// reach and arc are hit (once per step). Blade contact (WeaponDamageSystem) also counts; a target is
    /// never hit twice by the same step either way.
    /// </summary>
    private void ReachHits(Hand hand, WeaponMoveset.Step step)
    {
        float reach = hand.set.reach;
        Vector3 chest = transform.position + Vector3.up * 1.1f;
        int count = Physics.OverlapCapsuleNonAlloc(chest, chest + transform.forward * Mathf.Max(0.1f, reach - 0.3f),
            0.55f, overlap, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var col = overlap[i];
            var target = col.GetComponentInParent<HealthSystem>();
            if (target == null || target.IsDead || target.transform.root == transform.root) continue;
            if (!target.CompareTag("Enemy") && !col.CompareTag("Enemy")) continue;
            Vector3 to = target.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f && Vector3.Angle(transform.forward, to) > hand.set.arc) continue;

            if (hand.weapon != null)
            {
                hand.weapon.Strike(col);
            }
            else if (hand.punched.Add(target))
            {
                var fist = animator.GetBoneTransform(hand.right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                Vector3 from = fist != null ? fist.position : chest;
                target.TakeDamage(Mathf.RoundToInt(fistDamage * step.damage));
                CombatFeedback.Hit(col.ClosestPoint(from), to, victimIsPlayer: false);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Getting hit
    // ─────────────────────────────────────────────────────────────────────

    private void HandleHealthChanged(int current, int max)
    {
        bool hurt = lastHealth - current >= minDamageToFlinch;
        lastHealth = current;
        if (!hurt || current <= 0 || hitReactLayer < 0 || hitReactionStates.Length == 0) return;

        // A blow interrupts the attack
        if (rightHand.step >= 0) EndCombo(rightHand);
        if (leftHand.step >= 0) EndCombo(leftHand);
        string state = hitReactionStates[Random.Range(0, hitReactionStates.Length)];
        animator.CrossFadeInFixedTime(state, 0.05f, hitReactLayer, 0f);
    }
}
