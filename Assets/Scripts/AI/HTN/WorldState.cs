using UnityEngine;

/// <summary>
/// A plain data struct representing everything the HTN planner needs to know
/// about the world at a given moment.  It is rebuilt every frame by EnemyAI_HTN
/// and passed (by value) into the planner so the planner never mutates live data.
/// </summary>
[System.Serializable]
public struct WorldState
{
    // ── Spatial ──────────────────────────────────────────────────────────────
    public float distanceToPlayer;
    public bool playerInMeleeRange;
    public bool playerInOptimalRange;     // within optimalCombatRange
    public bool playerTooFar;             // beyond stopChasingRange

    // ── Player intent ─────────────────────────────────────────────────────────
    public bool playerApproaching;
    public bool playerRetreating;
    public bool playerVisible;             // passed vision-cone + raycast check
    public bool playerInCombatSite;

    // ── Enemy combat state ────────────────────────────────────────────────────
    public bool canAttack;               // cooldown satisfied
    public int consecutiveAttacks;
    public int maxConsecutiveAttacks;
    public bool canRoll;

    // ── Scores (computed from personality) ───────────────────────────────────
    public float aggressionScore;
    public float cautionScore;

    // ── Patrol ────────────────────────────────────────────────────────────────
    public bool hasPatrolPoints;
    public bool isPatrolling;

    // ── Current goal ─────────────────────────────────────────────────────────
    public GoalType activeGoal;

    // ── Convenience constructor ───────────────────────────────────────────────
    public static WorldState Empty => new WorldState
    {
        distanceToPlayer = float.MaxValue,
        playerInMeleeRange = false,
        playerInOptimalRange = false,
        playerTooFar = true,
        playerInCombatSite = false,
        playerApproaching = false,
        playerRetreating = false,
        playerVisible = false,
        canAttack = false,
        consecutiveAttacks = 0,
        maxConsecutiveAttacks = 2,
        canRoll = false,
        aggressionScore = 0.5f,
        cautionScore = 0.3f,
        hasPatrolPoints = false,
        isPatrolling = false,
        activeGoal = GoalType.Idle
    };
}

// ─────────────────────────────────────────────────────────────────────────────
//  Enums (shared across the whole HTN system)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>High-level goals the enemy can pursue.</summary>
public enum GoalType
{
    Idle,
    Patrol,
    DetectPlayer,
    EliminateThreat,   // root combat goal
    Survive            // low-hp fallback (future expansion)
}

/// <summary>
/// Primitive (leaf) tasks that map directly to behaviour methods.
/// Compound tasks are decomposed into sequences of these.
/// </summary>
public enum TaskType
{
    // Non-combat
    Idle,
    PatrolWaypoints,
    LookAtPlayer,

    // Approach / movement
    Chase,
    Reposition,
    CirclePlayer,
    Retreat,

    // Combat
    AttackMelee,
    Prepare,
    CombatFootwork,

    // Evasion
    DodgeBackward,
    Roll,
}
