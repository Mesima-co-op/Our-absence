using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The HTN domain: a static library of methods (decomposition rules).
/// Each GoalType maps to one or more Method objects.  The planner tries
/// each method in order; the first whose precondition passes wins.
///
/// Methods produce a sequence of TaskType primitives that are executed
/// one-by-one by EnemyAI_HTN.  Some methods include a weighted random
/// selection step (the "tactical decision") that routes through
/// EnemyPersonality to differ per enemy archetype.
/// </summary>
public static class HTNDomain
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Decompose <paramref name="goal"/> given the current <paramref name="ws"/>
    /// and <paramref name="personality"/>.
    /// Returns a list of primitive TaskTypes to execute in order, or null
    /// if no method's precondition was satisfied.
    /// </summary>
    public static List<TaskType> Decompose(GoalType goal, WorldState ws, EnemyPersonality personality)
    {
        List<Method> methods = GetMethods(goal, ws, personality);
        if (methods == null || methods.Count == 0) return null;

        foreach (Method m in methods)
        {
            if (m.Precondition(ws))
                return m.Tasks(ws, personality);
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Method table
    // ─────────────────────────────────────────────────────────────────────────

    private static List<Method> GetMethods(GoalType goal, WorldState ws, EnemyPersonality p)
    {
        switch (goal)
        {
            case GoalType.Idle: return IdleMethods();
            case GoalType.Patrol: return PatrolMethods();
            case GoalType.DetectPlayer: return DetectMethods();
            case GoalType.EliminateThreat: return CombatMethods(ws, p);
            default: return null;
        }
    }

    // ── Idle ──────────────────────────────────────────────────────────────────

    private static List<Method> IdleMethods() => new List<Method>
    {
        new Method(
            "IdleToPatrol",
            ws => ws.hasPatrolPoints,
            (ws, p) => Tasks(TaskType.PatrolWaypoints)
        ),
        new Method(
            "IdleStand",
            ws => true,
            (ws, p) => Tasks(TaskType.Idle)
        )
    };

    // ── Patrol ────────────────────────────────────────────────────────────────

    private static List<Method> PatrolMethods() => new List<Method>
    {
        new Method(
            "PatrolLoop",
            ws => ws.hasPatrolPoints && !ws.playerVisible,
            (ws, p) => Tasks(TaskType.PatrolWaypoints)
        ),
        new Method(
            "PatrolDetect",
            ws => ws.playerVisible,
            (ws, p) => Tasks(TaskType.LookAtPlayer)
        )
    };

    // ── DetectPlayer ──────────────────────────────────────────────────────────

    private static List<Method> DetectMethods() => new List<Method>
    {
        new Method(
            "Investigate",
            ws => true,
            (ws, p) => Tasks(TaskType.LookAtPlayer)
        )
    };

    // ── EliminateThreat (combat) ───────────────────────────────────────────────
    //
    //  Methods are tried top-to-bottom; first passing precondition wins.
    //  The final "TacticalDecision" method has an always-true precondition
    //  and uses the personality's weighted sampler for variety.
    // ─────────────────────────────────────────────────────────────────────────

    private static List<Method> CombatMethods(WorldState ws, EnemyPersonality p)
    {
        return new List<Method>
        {
            // 1. Rotate to player if neer but not in site of view
            new Method(
                "RotateToPlayer",
                s => !s.playerInCombatSite,
                (s, per) => {
                    if (per.detectionRadius>= s.distanceToPlayer) return Tasks(TaskType.LookAtPlayer);
                    else return Tasks(TaskType.PatrolWaypoints);
                    }
            ),

            // 2. Player left the fight — disengage
            new Method(
                "Disengage",
                s => s.playerTooFar,
                (s, per) => Tasks(TaskType.Idle)
            ),

            // 3. In melee range, cooldown ready, aggression > caution → attack
            new Method(
                "MeleeOpportunity",
                s => s.playerInMeleeRange && s.canAttack
                     && s.aggressionScore > s.cautionScore
                     && s.consecutiveAttacks < s.maxConsecutiveAttacks,
                (s, per) => Tasks(TaskType.AttackMelee)
            ),

            // 4. Too many consecutive attacks → forced reposition + footwork
            new Method(
                "ForcedReposition",
                s => s.consecutiveAttacks >= s.maxConsecutiveAttacks,
                (s, per) => Tasks(TaskType.Reposition, TaskType.CombatFootwork)
            ),

            // 5. Player charging at us → dodge or counter
            new Method(
                "CounterCharge_Dodge",
                s => s.playerApproaching && s.canRoll,
                (s, per) =>
                {
                    bool dodge = Random.value < per.rollChance;
                    return dodge
                        ? Tasks(TaskType.DodgeBackward)
                        : Tasks(s.aggressionScore > 0.7f ? TaskType.Prepare : TaskType.Retreat);
                }
            ),

            new Method(
                "CounterCharge_Stand",
                s => s.playerApproaching,
                (s, per) =>
                {
                    bool prepare = Random.value < s.aggressionScore;
                    return Tasks(prepare ? TaskType.Prepare : TaskType.Retreat);
                }
            ),

            // 6. Player running away & we're not in melee range → pursue
            new Method(
                "PursueRetreat",
                s => s.playerRetreating && !s.playerInMeleeRange,
                (s, per) =>
                    Tasks(!s.playerInOptimalRange ? TaskType.Chase : TaskType.CirclePlayer)
            ),

            // 7. Too far from player → chase to close distance
            new Method(
                "ClosingDistance",
                s => !s.playerInOptimalRange && !s.playerTooFar,
                (s, per) => Tasks(TaskType.Chase)
            ),

            // 8. In melee range but caution beats aggression → back off
            new Method(
                "CautiousBackoff",
                s => s.playerInMeleeRange && s.cautionScore > s.aggressionScore,
                (s, per) => Tasks(TaskType.Retreat, TaskType.CombatFootwork)
            ),

            // 9. Weighted tactical decision — the "anything goes" fallback
            new Method(
                "TacticalDecision",
                s => true,
                (s, per) =>
                {
                    TaskType chosen = per.SampleWeightedAction();

                    // Guard: don't attack if cooldown isn't ready
                    if (chosen == TaskType.AttackMelee && !s.canAttack)
                        chosen = TaskType.CombatFootwork;

                    // Guard: don't dodge if roll is on cooldown
                    if ((chosen == TaskType.DodgeBackward || chosen == TaskType.Roll) && !s.canRoll)
                        chosen = TaskType.CirclePlayer;

                    return new List<TaskType> { chosen, TaskType.CombatFootwork };
                }
            ),

        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static List<TaskType> Tasks(params TaskType[] types)
        => new List<TaskType>(types);
}

// ─────────────────────────────────────────────────────────────────────────────
//  Method data class (internal to the domain)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A single decomposition rule: if Precondition(ws) → produce Tasks(ws, personality).
/// </summary>
public class Method
{
    public readonly string Name;
    public readonly System.Func<WorldState, bool> Precondition;
    public readonly System.Func<WorldState, EnemyPersonality, List<TaskType>> Tasks;

    public Method(
        string name,
        System.Func<WorldState, bool> precondition,
        System.Func<WorldState, EnemyPersonality, List<TaskType>> tasks)
    {
        Name = name;
        Precondition = precondition;
        Tasks = tasks;
    }
}
