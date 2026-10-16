using UnityEngine;

/// <summary>
/// Attach to the weapon prefab (the same GameObject that has a Collider set to Is Trigger).
/// EnemyAI_HTN calls Enable() at the start of an attack and Disable() when it ends,
/// so damage is only dealt during the active swing window.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WeaponHitbox : MonoBehaviour
{
    // Set by EnemyAI_HTN.InitializeWeapon()
    [HideInInspector] public int damage;

    private Collider _col;
    private bool     _hasHitThisSwing;   // prevents multiple hits per swing

    private void Awake()
    {
        _col         = GetComponent<Collider>();
        _col.isTrigger = true;
        Disable();      // always start inactive
    }

    /// <summary>Open the damage window for one swing.</summary>
    public void Enable()
    {
        _hasHitThisSwing = false;
        _col.enabled     = true;
    }

    /// <summary>Close the damage window.</summary>
    public void Disable()
    {
        _col.enabled = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_hasHitThisSwing) return;
        if (!other.CompareTag("Player")) return;

        HealthSystem health = other.GetComponent<HealthSystem>();
        if (health == null) return;

        health.TakeDamage(damage);
        _hasHitThisSwing = true;    // one hit per swing, then close
        Disable();
    }
}
