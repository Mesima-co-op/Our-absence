using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player weapon damage. Lives on the weapon prefab; only deals damage while the wielder
/// is in an attack animation, and hits each target at most once per swing.
/// </summary>
public class WeaponDamageSystem : MonoBehaviour
{
    [SerializeField] private ItemDataInstance WeaponItem;
    [Tooltip("Damage per hit before the item's damage modifier is applied.")]
    [SerializeField] private float baseDamage = 20f;

    private AnimationManager wielder;
    private AttackAnimationController attackAnimations;
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
        hitThisSwing.Clear();
    }

    private void Update()
    {
        // A new swing starts once the previous attack has ended
        if (attackAnimations != null && !attackAnimations.IsAttacking())
            hitThisSwing.Clear();
    }

    private float Damage => baseDamage * (WeaponItem != null ? WeaponItem.damageModifier : 1f);

    private void OnTriggerEnter(Collider other)
    {
        // Only a held weapon in the middle of an attack does damage
        if (attackAnimations == null || !attackAnimations.IsAttacking()) return;

        HealthSystem enemyHealth = other.GetComponentInParent<HealthSystem>();
        if (enemyHealth == null || enemyHealth.IsDead) return;
        if (enemyHealth.transform.root == transform.root) return; // never hit the wielder
        if (!enemyHealth.CompareTag("Enemy") && !other.CompareTag("Enemy")) return;
        if (!hitThisSwing.Add(enemyHealth)) return;

        enemyHealth.TakeDamage(Mathf.RoundToInt(Damage));
    }
}
