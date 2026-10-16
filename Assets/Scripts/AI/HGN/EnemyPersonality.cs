using UnityEngine;

/// <summary>
/// ScriptableObject that defines an enemy archetype's personality via probability distributions.
/// Create different assets (Aggressive, Cautious, Balanced, Berserker, etc.) and assign them
/// in the Inspector to get radically different behaviour from the same HTN system.
/// </summary>
[CreateAssetMenu(fileName = "EnemyPersonality", menuName = "HTN Enemy AI/Personality", order = 1)]
public class EnemyPersonality : ScriptableObject
{
    // ─────────────────────────────────────────────
    //  Identity
    // ─────────────────────────────────────────────

    [Header("Identity")]
    [Tooltip("Human-readable name shown in debug overlays.")]
    public string personalityName = "Balanced";

    // ─────────────────────────────────────────────
    //  Core personality sliders
    // ─────────────────────────────────────────────

    [Header("Core Personality")]
    [Range(0f, 1f)]
    [Tooltip("How willing this enemy is to close distance and attack.")]
    public float aggression = 0.5f;

    [Range(0f, 1f)]
    [Tooltip("How often the enemy prioritises defensive / avoidance actions.")]
    public float caution = 0.3f;

    [Range(0f, 1f)]
    [Tooltip("Higher unpredictability = more variance in action selection.")]
    public float unpredictability = 0.2f;

    // ─────────────────────────────────────────────
    //  Action probability weights
    //  These feed into the HTN planner's method selection.
    //  They are RELATIVE weights, not hard probabilities — the
    //  planner normalises them at runtime so you can author them
    //  intuitively (e.g. set Attack=10, Retreat=1 for a berserker).
    // ─────────────────────────────────────────────

    [Header("Action Probability Weights  (relative — higher = more likely)")]
    [Min(0f)] public float weightAttackMelee   = 5f;
    [Min(0f)] public float weightReposition    = 3f;
    [Min(0f)] public float weightRetreat       = 2f;
    [Min(0f)] public float weightCircle        = 4f;
    [Min(0f)] public float weightPrepare       = 2f;
    [Min(0f)] public float weightDodge         = 3f;
    [Min(0f)] public float weightChase         = 4f;

    // ─────────────────────────────────────────────
    //  Combat timing
    // ─────────────────────────────────────────────

    [Header("Combat Timing")]
    public float minCombatTime = 1.2f;
    public float maxCombatTime = 4.0f;
    public float minFootworkTime = 0.3f;
    public float maxFootworkTime = 1.2f;
    public float attackCooldown = 1.5f;
    public int   maxConsecutiveAttacks = 2;

    // ─────────────────────────────────────────────
    //  Movement
    // ─────────────────────────────────────────────

    [Header("Movement Speeds")]
    public float walkSpeed    = 2.0f;
    public float combatSpeed  = 3.5f;
    public float chaseSpeed   = 5.5f;
    public float footworkSpeed = 1.8f;

    // ─────────────────────────────────────────────
    //  Combat ranges
    // ─────────────────────────────────────────────

    [Header("Combat Ranges")]
    public float meleeAttackRange   = 2f;
    public float optimalCombatRange = 4f;
    public float stopChasingRange   = 10f;
    public float distanceBuffer     = 0.5f;
    public float maxRetreatDistance = 5f;

    // ─────────────────────────────────────────────
    //  Detection
    // ─────────────────────────────────────────────

    [Header("Detection")]
    public float detectionRadius = 10f;
    public float detectionAngle  = 45f;

    // ─────────────────────────────────────────────
    //  Orbit / Strafe
    // ─────────────────────────────────────────────

    [Header("Orbit / Strafe")]
    public float orbitRadius  = 7f;
    public float orbitSpeed   = 1.5f;
    public float strafeJitter = 0.3f;

    // ─────────────────────────────────────────────
    //  Roll / Dodge
    // ─────────────────────────────────────────────

    [Header("Roll / Dodge")]
    public float rollCooldown = 3f;
    [Range(0f, 1f)] public float rollChance = 0.3f;

    // ─────────────────────────────────────────────
    //  Patrol rest
    // ─────────────────────────────────────────────

    [Header("Patrol Rest")]
    public float minRestTime = 1f;
    public float maxRestTime = 3f;

    // ─────────────────────────────────────────────
    //  Runtime helpers
    // ─────────────────────────────────────────────

    /// <summary>
    /// Sample a random action using the weight table.
    /// Pass in a uniform [0,1] value (or leave as -1 to auto-generate).
    /// Returns one of the TaskType primitives that are driven by weights.
    /// </summary>
    public TaskType SampleWeightedAction(float? roll = null)
    {
        float[] weights =
        {
            weightAttackMelee,
            weightReposition,
            weightRetreat,
            weightCircle,
            weightPrepare,
            weightDodge,
            weightChase
        };

        TaskType[] tasks =
        {
            TaskType.AttackMelee,
            TaskType.Reposition,
            TaskType.Retreat,
            TaskType.CirclePlayer,
            TaskType.Prepare,
            TaskType.DodgeBackward,
            TaskType.Chase
        };

        float total = 0f;
        foreach (float w in weights) total += w;

        float r = roll.HasValue ? roll.Value * total : Random.Range(0f, total);
        float cumulative = 0f;

        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (r <= cumulative) return tasks[i];
        }

        return TaskType.AttackMelee; // fallback
    }

    /// <summary>
    /// Returns a context-adjusted aggression score, matching the original
    /// CalculateAggressionScore logic but seeded from this personality.
    /// </summary>
    public float GetAggressionScore(float distance, bool playerApproaching, int consecutiveAttacks, float meleeRange, float optimalRange)
    {
        float score = aggression;
        if (distance <= optimalRange)          score += 0.2f;
        if (playerApproaching)                 score += 0.3f;
        if (consecutiveAttacks >= maxConsecutiveAttacks - 1) score -= 0.4f;
        return Mathf.Clamp01(score + Random.Range(-unpredictability * 0.1f, unpredictability * 0.1f));
    }

    /// <summary>
    /// Returns a context-adjusted caution score.
    /// </summary>
    public float GetCautionScore(float distance, bool playerRetreating, int consecutiveAttacks, float meleeRange)
    {
        float score = caution;
        if (distance < meleeRange * 0.7f)  score += 0.3f;
        if (playerRetreating)              score += 0.2f;
        if (consecutiveAttacks > 0)        score += 0.1f * consecutiveAttacks;
        return Mathf.Clamp01(score + Random.Range(-unpredictability * 0.1f, unpredictability * 0.1f));
    }
}
