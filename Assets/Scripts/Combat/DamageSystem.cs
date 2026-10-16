using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DamageSystem : MonoBehaviour
{
    public int damage = 10;

    [Tooltip("Damage per second if staying inside")]
    public bool damageOverTime = false;

    public float damageInterval = 1f;

    private float timer;

    private void OnTriggerEnter(Collider other)
    {
        if (!damageOverTime)
        {
            DealDamage(other);
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!damageOverTime) return;

        timer += Time.deltaTime;

        if (timer >= damageInterval)
        {
            DealDamage(other);
            timer = 0f;
        }
    }

    void DealDamage(Collider other)
    {
        HealthSystem health = other.GetComponent<HealthSystem>();

        if (health)
        {
            health.TakeDamage(damage);
        }
    }
}

