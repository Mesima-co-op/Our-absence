using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Damages anything with a HealthSystem that touches this trigger (campfires, hazards).
/// Cooldowns are tracked per target, so the damage rate is exactly one hit per interval
/// regardless of how many trigger callbacks arrive.
/// </summary>
public class DamageSystem : MonoBehaviour
{
    public int damage = 10;

    [Tooltip("Damage per second if staying inside")]
    public bool damageOverTime = false;

    public float damageInterval = 1f;

    private readonly Dictionary<HealthSystem, float> nextDamageTime = new Dictionary<HealthSystem, float>();

    private void OnTriggerEnter(Collider other)
    {
        if (!damageOverTime)
            TryDamage(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (damageOverTime)
            TryDamage(other);
    }

    private void TryDamage(Collider other)
    {
        // Only the collider that owns the HealthSystem (the character's body) counts.
        // Searching parents would let weapons and bones count too, e.g. a held sword touching a hazard.
        HealthSystem health = other.GetComponent<HealthSystem>();
        if (health == null || health.IsDead) return;

        // One hit per target per interval, no matter how many of its colliders are inside
        if (nextDamageTime.TryGetValue(health, out float next) && Time.time < next) return;
        nextDamageTime[health] = Time.time + damageInterval;

        health.TakeDamage(damage);
    }
}
