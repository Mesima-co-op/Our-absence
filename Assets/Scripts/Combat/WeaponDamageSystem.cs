using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player weapon damage. Lives on the weapon prefab and hits each target at most once per swing.
/// With PlayerCombat on the wielder, damage is dealt only inside the current combo step's hit window
/// (HitWindowOpen) and multiplied by that step's damage. Without it, the whole attack animation counts.
/// </summary>
public class WeaponDamageSystem : MonoBehaviour
{
    [SerializeField] private ItemDataInstance WeaponItem;
    [Tooltip("Damage per hit before the item's damage modifier and the combo step's multiplier are applied.")]
    [SerializeField] private float baseDamage = 20f;

    private AnimationManager wielder;
    private AttackAnimationController attackAnimations;
    private bool comboDriven;
    private float stepDamage = 1f;
    private readonly HashSet<HealthSystem> hitThisSwing = new HashSet<HealthSystem>();

    void Start()
    {
        if (WeaponItem == null) WeaponItem = GetComponentInParent<ItemDataInstance>();
        ResolveWielder();
    }

    private void OnTransformParentChanged()
    {
        // Picked up / drawn / dropped: the wielder may have changed
        ResolveWielder();
    }

    private void ResolveWielder()
    {
        wielder = GetComponentInParent<AnimationManager>();
        attackAnimations = wielder != null ? new AttackAnimationController(wielder) : null;
        comboDriven = wielder != null && wielder.GetComponent<PlayerCombat>() != null;
        hitThisSwing.Clear();
        HitWindowOpen = false;
        SwingTrail = false;

        // Held weapons leave a swoosh while swinging
        if (wielder != null && trail == null)
            trail = gameObject.AddComponent<WeaponTrail>();
        if (trail != null) trail.enabled = wielder != null;
    }

    private WeaponTrail trail;

    /// <summary>True while the weapon rests on the back: it deals no damage and leaves no trail.</summary>
    public bool Sheathed { get; set; }

    /// <summary>Set by PlayerCombat: the current swing can hit.</summary>
    public bool HitWindowOpen { get; set; }

    /// <summary>Set by PlayerCombat: the blade is moving fast enough to leave a trail.</summary>
    public bool SwingTrail { get; set; }

    public float BaseDamage => baseDamage;

    /// <summary>A new combo step: everything can be hit once more, with this damage multiplier.</summary>
    public void BeginSwing(float damageMultiplier)
    {
        hitThisSwing.Clear();
        stepDamage = damageMultiplier;
    }

    private bool CanHit => !Sheathed && (comboDriven ? HitWindowOpen : attackAnimations != null && attackAnimations.IsAttacking());

    private BoxCollider blade;
    private Vector3 lastCenter;
    private Quaternion lastRotation;
    private bool hadWindow;
    private readonly Collider[] overlaps = new Collider[24];

    private void Update()
    {
        if (comboDriven)
        {
            if (trail != null) trail.Emitting = SwingTrail && !Sheathed;
            SweepBlade();
            return;
        }
        bool attacking = attackAnimations != null && attackAnimations.IsAttacking();
        // A new swing starts once the previous attack has ended
        if (attackAnimations != null && !attacking)
            hitThisSwing.Clear();
        if (trail != null) trail.Emitting = attacking && !Sheathed;
    }

    /// <summary>
    /// A fast swing moves the blade further in one frame than its own width, so trigger events miss
    /// hits. While the hit window is open, test the blade's box at its current pose and at points
    /// in between since the last frame.
    /// </summary>
    private void SweepBlade()
    {
        if (blade == null) blade = GetComponent<BoxCollider>();
        if (blade == null || !CanHit) { hadWindow = false; return; }

        Vector3 center = transform.TransformPoint(blade.center);
        Quaternion rotation = transform.rotation;
        Vector3 half = Vector3.Scale(blade.size * 0.5f, transform.lossyScale);
        int steps = hadWindow ? Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(center, lastCenter) / 0.08f), 1, 8) : 1;
        for (int i = 1; i <= steps; i++)
        {
            float k = i / (float)steps;
            Vector3 c = hadWindow ? Vector3.Lerp(lastCenter, center, k) : center;
            Quaternion r = hadWindow ? Quaternion.Slerp(lastRotation, rotation, k) : rotation;
            int count = Physics.OverlapBoxNonAlloc(c, half, overlaps, r, ~0, QueryTriggerInteraction.Ignore);
            for (int j = 0; j < count; j++) TryHit(overlaps[j]);
        }
        lastCenter = center;
        lastRotation = rotation;
        hadWindow = true;
    }

    private float Damage =>baseDamage * stepDamage * (WeaponItem != null ? WeaponItem.damageModifier : 1f);

    private void OnTriggerEnter(Collider other) => TryHit(other);

    // The blade may already be inside the enemy when the hit window opens
    private void OnTriggerStay(Collider other) => TryHit(other);

    private void TryHit(Collider other)
    {
        if (!CanHit) return;
        Strike(other);
    }

    /// <summary>
    /// Hits the enemy that owns <paramref name="other"/>, once per swing. Used by the blade's own contact and by
    /// PlayerCombat's reach check. Returns true if it dealt damage.
    /// </summary>
    public bool Strike(Collider other)
    {
        if (Sheathed || other == null) return false;
        HealthSystem enemyHealth = other.GetComponentInParent<HealthSystem>();
        if (enemyHealth == null || enemyHealth.IsDead) return false;
        if (enemyHealth.transform.root == transform.root) return false; // never hit the wielder
        if (!enemyHealth.CompareTag("Enemy") && !other.CompareTag("Enemy")) return false;
        if (!hitThisSwing.Add(enemyHealth)) return false;

        enemyHealth.TakeDamage(Mathf.RoundToInt(Damage));

        Vector3 blade = TryGetComponent<Collider>(out var own) ? own.bounds.center : transform.position;
        Vector3 point = other.ClosestPoint(blade);
        Vector3 from = wielder != null ? wielder.transform.position : transform.position;
        CombatFeedback.Hit(point, enemyHealth.transform.position - from, victimIsPlayer: false);
        return true;
    }
}
