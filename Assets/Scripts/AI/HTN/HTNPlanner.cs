using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stateful runtime planner.  Holds the current task queue and decides when
/// to replan.  One instance lives on the EnemyAI_HTN component.
///
/// Replanning happens when:
///   • The task queue is empty (natural completion).
///   • The active goal changes.
///   • The world state changes significantly (InterruptCheck).
///   • A forced replan is requested externally.
/// </summary>
public class HTNPlanner
{
    // ─────────────────────────────────────────────────────────────────────────
    //  State
    // ─────────────────────────────────────────────────────────────────────────

    private Queue<TaskType> _taskQueue = new Queue<TaskType>();
    private GoalType _currentGoal = GoalType.Idle;
    private EnemyPersonality _personality;

    // The task that is currently being executed
    public TaskType CurrentTask { get; private set; } = TaskType.Idle;

    // How long we've been running the current task
    public float CurrentTaskTimer { get; private set; } = 0f;

    // True when we have an active non-empty plan
    public bool HasPlan => _taskQueue.Count > 0 || CurrentTask != TaskType.Idle;

    // Last method name selected (useful for debug overlays)
    public string LastMethodName { get; private set; } = "none";
    private bool _taskLocked = false;
    public bool IsTaskLocked => _taskLocked;

    // ─────────────────────────────────────────────────────────────────────────
    //  Init
    // ─────────────────────────────────────────────────────────────────────────

    public HTNPlanner(EnemyPersonality personality)
    {
        _personality = personality;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Tick — call every frame from EnemyAI_HTN.Update()
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Advances the planner one frame.  Returns the task that should execute this frame.
    /// </summary>
    public TaskType Tick(WorldState ws, float deltaTime)
    {
        CurrentTaskTimer += deltaTime;

        // Never interrupt a locked task — it must call AdvanceTask() itself
        if (_taskLocked)
            return CurrentTask;

        GoalType desiredGoal = DeriveGoal(ws);

        bool goalChanged = desiredGoal != _currentGoal;
        bool planExhausted = _taskQueue.Count == 0;
        bool significantChange = InterruptCheck(ws);

        if (goalChanged || planExhausted || significantChange)
        {
            _currentGoal = desiredGoal;
            Replan(ws);
        }

        return CurrentTask;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Force advance (call when a task finishes naturally, e.g. attack anim ends)
    // ─────────────────────────────────────────────────────────────────────────

    public void AdvanceTask()
    {
        _taskLocked = false;

        if (_taskQueue.Count > 0)
        {
            CurrentTask = _taskQueue.Dequeue();
            CurrentTaskTimer = 0f;
            _taskLocked = IsNonInterruptable(CurrentTask); // re-lock for queued tasks too
        }
        else
        {
            CurrentTask = TaskType.Idle;
            CurrentTaskTimer = 0f;
        }
    }

    /// <summary>Request an immediate replan on the next Tick.</summary>
    public void ForceReplan()
    {
        _taskQueue.Clear();
        CurrentTask = TaskType.Idle;
        CurrentTaskTimer = 0f;
    }

    /// <summary>Advance timer only — used when the current task is non-interruptable.</summary>
    public void TickTimerOnly(float deltaTime)
    {
        CurrentTaskTimer += deltaTime;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Goal derivation — maps world state facts to a high-level goal
    // ─────────────────────────────────────────────────────────────────────────

    private GoalType DeriveGoal(WorldState ws)
    {
        if (!ws.playerVisible && ws.playerTooFar)
            return ws.hasPatrolPoints ? GoalType.Patrol : GoalType.Idle;

        // DetectPlayer is a brief transitional goal set explicitly by EnemyAI_HTN.
        // Once the LookAtPlayer task finishes the override is cleared and we fall through.
        if (ws.activeGoal == GoalType.DetectPlayer)
            return GoalType.DetectPlayer;

        // Player is visible and no special override — go fight
        return GoalType.EliminateThreat;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Interrupt check — should we abandon the current plan mid-execution?
    // ─────────────────────────────────────────────────────────────────────────

    private WorldState _lastInterruptSnapshot;

    private bool InterruptCheck(WorldState ws)
    {
        if (CurrentTaskTimer < MinTimeBeforeInterrupt(CurrentTask)) return false;


        bool interrupt = false;

        if (ws.playerInMeleeRange != _lastInterruptSnapshot.playerInMeleeRange)
            interrupt = true;
        else if (ws.playerApproaching && !_lastInterruptSnapshot.playerApproaching)
            interrupt = true;
        else if (ws.canAttack && !_lastInterruptSnapshot.canAttack
                 && (CurrentTask == TaskType.CirclePlayer || CurrentTask == TaskType.CombatFootwork))
            interrupt = true;

        // Always update snapshot so the same delta doesn't trigger twice
        _lastInterruptSnapshot = ws;
        return interrupt;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Replan
    // ─────────────────────────────────────────────────────────────────────────

    private void Replan(WorldState ws)
    {
        _taskQueue.Clear();
        _taskLocked = false;

        List<TaskType> plan = HTNDomain.Decompose(_currentGoal, ws, _personality);

        if (plan == null || plan.Count == 0)
        {
            LastMethodName = "fallback:Idle";
            CurrentTask = TaskType.Idle;
            return;
        }

        List<TaskType> shuffled = new List<TaskType>();
        for (int i = 1; i < plan.Count; i++)
            shuffled.Add(plan[i]);

        for (int i = 0; i < shuffled.Count; i++)
        {
            int randIndex = Random.Range(i, shuffled.Count);
            (shuffled[i], shuffled[randIndex]) = (shuffled[randIndex], shuffled[i]);
        }

        foreach (var task in shuffled)
            _taskQueue.Enqueue(task);

        CurrentTask = plan[0];
        CurrentTaskTimer = 0f;
        _taskLocked = IsNonInterruptable(CurrentTask); // lock if needed

        _lastInterruptSnapshot = ws;

#if UNITY_EDITOR
        LastMethodName = string.Join(" → ", plan);
#endif
    }

    private static bool IsNonInterruptable(TaskType task)
    {
        return task == TaskType.AttackMelee
            || task == TaskType.Roll
            || task == TaskType.DodgeBackward
            || task == TaskType.LookAtPlayer;
    }
    private static float MinTimeBeforeInterrupt(TaskType task)
    {
        switch (task)
        {
            case TaskType.CombatFootwork: return 0.5f;
            case TaskType.Retreat: return 0.5f;
            case TaskType.Reposition: return 0.5f;
            case TaskType.Prepare: return 0.4f;
            default: return 0.3f; // global minimum
        }
    }
}
