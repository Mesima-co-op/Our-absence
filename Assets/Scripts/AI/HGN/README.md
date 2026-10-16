# HTN Enemy AI System
### Hierarchical Task Network planner replacing the original state-machine EnemyAI.cs

---

## File Overview

```
EnemyAI_HTN/
├── WorldState.cs                 ← Data struct + GoalType / TaskType enums
├── EnemyPersonality.cs           ← ScriptableObject: per-archetype tuning + probability weights
├── HTNDomain.cs                  ← Method library: goal decomposition rules
├── HTNPlanner.cs                 ← Runtime planner: holds plan queue, triggers replan
├── EnemyAI_HTN.cs                ← MonoBehaviour: sensor → WorldState → planner → behaviour
└── Editor/
    └── PersonalityPresetGenerator.cs   ← Tools > HTN Enemy AI > Create Personality Presets
```

---

## Quick Start

1. **Copy all files** into your Unity project (any folder under `Assets/`).
2. Run **Tools > HTN Enemy AI > Create Personality Presets** — this creates six ready-to-use
   `.asset` files under `Assets/HTN_EnemyAI/Personalities/`.
3. On your enemy GameObject, **replace** `EnemyAI` with `EnemyAI_HTN`.
4. Assign the **`player`** Transform and drag in a **`EnemyPersonality`** asset.
5. Optionally assign `patrolPoints`, `weapon`, `hend`, `agent`.
6. Hit Play.

To create a new archetype: right-click in the Project window →
**Create > HTN Enemy AI > Personality**, tune the sliders and weight fields.

---

## Architecture

```
EnemyAI_HTN.Update()
    │
    ├─ AnalyzePlayerBehavior()   ← tracks approach / retreat direction
    │
    ├─ BuildWorldState()         ← snapshot: distances, flags, scores
    │
    ├─ HTNPlanner.Tick(ws)
    │       │
    │       ├─ DeriveGoal()      ← Idle / Patrol / DetectPlayer / EliminateThreat
    │       │
    │       ├─ HTNDomain.Decompose(goal, ws, personality)
    │       │       │
    │       │       └─ Tries each Method in order:
    │       │               precondition(ws) → first passing method wins
    │       │               Returns List<TaskType>  (the plan)
    │       │
    │       └─ Returns CurrentTask (front of queue)
    │
    └─ ExecuteTask(currentTask)  ← runs the behaviour, calls AdvanceTask() when done
```

### Key concepts

| Concept | Where | Description |
|---|---|---|
| **Goal** | `GoalType` enum | What the enemy wants (high-level) |
| **Method** | `HTNDomain.Method` | A decomposition rule: precondition → task sequence |
| **Task** | `TaskType` enum | Primitive action the enemy can execute |
| **WorldState** | `WorldState` struct | All facts the planner needs (rebuilt every frame) |
| **Personality** | `EnemyPersonality` SO | Per-archetype numbers + probability weights |

---

## Probability Distribution System

The personality's **action weight table** drives the `TacticalDecision` method —
the fallback that fires when no specific rule matches:

```csharp
weightAttackMelee  = 5f;   // relative weight — not a 0-1 probability
weightReposition   = 3f;
weightRetreat      = 2f;
weightCircle       = 4f;
weightPrepare      = 2f;
weightDodge        = 3f;
weightChase        = 4f;
```

`EnemyPersonality.SampleWeightedAction()` normalises these and samples proportionally,
so a Berserker with `weightAttackMelee = 15` will choose to attack roughly
15 / (15 + 1 + 0 + 1 + 2 + 1 + 12) ≈ **47% of the time** from that fallback path.

The `aggression` and `caution` floats further modulate earlier methods —
Aggressive enemies are more likely to satisfy the `MeleeOpportunity` precondition
before the planner even reaches the weighted sampler.

---

## Preset Archetypes

| Name | Aggr | Caut | Style |
|---|---|---|---|
| Aggressive | 0.80 | 0.10 | Closes fast, attacks freely |
| Cautious   | 0.30 | 0.70 | Circles, waits for openings |
| Balanced   | 0.55 | 0.30 | Original behaviour |
| Berserker  | 1.00 | 0.00 | Non-stop attacks, no retreat |
| Skirmisher | 0.50 | 0.45 | Hit-and-run, heavy dodging |
| Coward     | 0.10 | 0.90 | Flees on sight, rarely attacks |

---

## Adding New Behaviours

**New primitive task:**
1. Add entry to `TaskType` enum in `WorldState.cs`.
2. Add `case TaskType.YourTask:` in `EnemyAI_HTN.ExecuteTask()`.
3. Implement the behaviour method.
4. Optionally add a weight field in `EnemyPersonality` and include it in `SampleWeightedAction()`.

**New method (decomposition rule):**
1. Add a `new Method(...)` entry in the appropriate method list in `HTNDomain.cs`.
   Earlier entries take priority — put specific rules before general ones.

**New goal:**
1. Add to `GoalType` enum.
2. Add a `case GoalType.YourGoal:` in `HTNDomain.GetMethods()`.
3. Add a condition in `HTNPlanner.DeriveGoal()` to switch to it.

---

## Animator Parameters Required

Same as the original `EnemyAI.cs`:

| Parameter | Type | Notes |
|---|---|---|
| `Vertical`    | Float | Forward/back blend |
| `Horizontal`  | Float | Left/right blend |
| `speed`       | Float | 0=idle, 1=walk, 2=run |
| `isAttacking` | Bool  | Set true on attack start, cleared by animation |
| `attackType`  | Int   | 1–3 attack variation index |
| `dodgeBackwards` | Trigger | Backward dodge |
| `Roll`        | Trigger | Roll/evade |
| `Shoot`       | Trigger | Ranged attack (unused in melee path) |

Layer `Target_layer` must exist and will be weighted 0 (patrol/idle) or 1 (combat).
