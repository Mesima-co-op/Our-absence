using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// HTN-based enemy AI.  Replaces the original state-machine EnemyAI.cs.
///
/// Responsibilities:
///   • Build a WorldState each frame from sensors and combat trackers.
///   • Feed it to HTNPlanner, which returns the current TaskType.
///   • Execute the behaviour for that task.
///   • Report task completion back to the planner so it advances the queue.
///
/// All tunable numbers live in the EnemyPersonality ScriptableObject.
/// Assign different personalities in the Inspector to get different archetypes
/// with no code changes.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class EnemyAI_HTN : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Inspector fields
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public Transform player;
    public NavMeshAgent agent;              // optional — not used for movement (CharacterController is used), but kept for nav-mesh queries

    [Header("Personality  (ScriptableObject)")]
    [Tooltip("Drag in one of the EnemyPersonality assets. Different archetypes just swap this.")]
    public EnemyPersonality personality;

    [Header("Patrol")]
    [SerializeField] private Transform[] patrolPoints;

    [Header("Weapons")]
    public GameObject hend;
    public GameObject weapon;
    public int damage = 0;

    [Header("Gravity")]
    public float gravity = -9.81f;

    // ─────────────────────────────────────────────────────────────────────────
    //  Private — component cache
    // ─────────────────────────────────────────────────────────────────────────

    private Animator _animator;
    private CharacterController _controller;

    // ─────────────────────────────────────────────────────────────────────────
    //  Private — planner
    // ─────────────────────────────────────────────────────────────────────────

    private HTNPlanner _planner;
    private TaskType _currentTask = TaskType.Idle;

    // ─────────────────────────────────────────────────────────────────────────
    //  Private — world-state trackers
    // ─────────────────────────────────────────────────────────────────────────

    private Vector3 _velocity = Vector3.zero;
    private Vector3 _lastPlayerPos = Vector3.zero;
    private float _smoothedApproach = 0f;

    // Player behaviour flags (updated by AnalyzePlayerBehavior)
    private bool _playerApproaching;
    private bool _playerRetreating;

    // Combat trackers
    private float _lastAttackTime = 0f;
    private float _lastRollTime = 0f;
    private int _consecutiveAttacks = 0;

    // Patrol rest
    private int _currentPatrolIndex = 0;
    private bool _isResting = false;
    private float _restTimer = 0f;

    // Orbit / strafe
    private float _orbitAngle = 0f;
    private int _orbitDirection = 1;

    // Footwork
    private float _footworkTimer = 0f;
    private Vector3 _footworkOffset = Vector3.zero;

    // Goal override (allows Detect_Player transition from patrol)
    private GoalType _goalOverride = GoalType.Idle;
    private bool _hasGoalOverride = false;

    // Player health system
    private HealthSystem _playerHealth;

    // Weapon hitbox (on the weapon prefab)
    private WeaponHitbox _weaponHitbox;


    // ─────────────────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _controller = GetComponent<CharacterController>();
    }

    private void Start()
    {
        if (!personality)
        {
            Debug.LogError($"[HTN] {name}: No EnemyPersonality assigned! Creating a default one.", this);
            personality = ScriptableObject.CreateInstance<EnemyPersonality>();
        }

        _planner = new HTNPlanner(personality);
        _lastPlayerPos = player ? player.position : transform.position;
        damage = weapon ? damage : 2;

        if (weapon)
        {
            _weaponHitbox = weapon.GetComponent<WeaponHitbox>();
            if (_weaponHitbox != null)
                _weaponHitbox.damage = damage;
            else
                Debug.LogWarning($"[HTN] {name}: weapon has no WeaponHitbox component.", this);
        }

        if (player)
            _playerHealth = player.GetComponent<HealthSystem>();

        if (!_playerHealth)
            Debug.LogWarning($"[HTN] {name}: No HealthSystem found on player.", this);
    }

    private void Update()
    {
        ApplyGravity();

        if (!player) return;

        AnalyzePlayerBehavior();

        WorldState ws = BuildWorldState();

        if (_planner.IsTaskLocked)
            _planner.TickTimerOnly(Time.deltaTime);
        else
            _currentTask = _planner.Tick(ws, Time.deltaTime);

        ExecuteTask(_currentTask, ws);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  World-state construction
    // ─────────────────────────────────────────────────────────────────────────

    private WorldState BuildWorldState()
    {
        float dist = Vector3.Distance(transform.position, player.position);
        float p = personality.optimalCombatRange;
        float m = personality.meleeAttackRange;
        float buf = personality.distanceBuffer;

        bool visible = CheckPlayerInVisionCone();
        bool isInfront = CheckPlayerInfront();

        // Transition from Patrol/Idle to Detect when player first becomes visible
        if (visible && !_hasGoalOverride
            && (_planner.CurrentTask == TaskType.PatrolWaypoints || _planner.CurrentTask == TaskType.Idle))
        {
            _goalOverride = GoalType.DetectPlayer;
            _hasGoalOverride = true;
            _planner.ForceReplan();
        }
        // Clear the Detect override as soon as we're no longer running LookAtPlayer
        // (ExecuteLookAtPlayer calls AdvanceTask → CurrentTask moves on → we escalate to EliminateThreat)
        if (_hasGoalOverride && _goalOverride == GoalType.DetectPlayer
            && _planner.CurrentTask != TaskType.LookAtPlayer)
        {
            _hasGoalOverride = false;
            _goalOverride = GoalType.Idle;
        }

        WorldState ws = new WorldState
        {
            distanceToPlayer = dist,
            playerInMeleeRange = dist <= m,
            playerInOptimalRange = dist <= p + buf,
            playerTooFar = dist > personality.stopChasingRange + buf,

            playerApproaching = _playerApproaching,
            playerRetreating = _playerRetreating,
            playerVisible = visible,
            playerInCombatSite = isInfront,

            canAttack = Time.time - _lastAttackTime > personality.attackCooldown,
            consecutiveAttacks = !_animator.GetBool("isAttacking") ? 0 : _consecutiveAttacks,
            maxConsecutiveAttacks = personality.maxConsecutiveAttacks,
            canRoll = Time.time - _lastRollTime > personality.rollCooldown,

            aggressionScore = personality.GetAggressionScore(dist, _playerApproaching, _consecutiveAttacks, m, p),
            cautionScore = personality.GetCautionScore(dist, _playerRetreating, _consecutiveAttacks, m),

            hasPatrolPoints = patrolPoints != null && patrolPoints.Length > 0,
            isPatrolling = _currentTask == TaskType.PatrolWaypoints,

            activeGoal = _hasGoalOverride ? _goalOverride : GoalType.Idle
        };

        return ws;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Task executor — maps TaskType → behaviour method
    // ─────────────────────────────────────────────────────────────────────────

    private void ExecuteTask(TaskType task, WorldState ws)
    {
        switch (task)
        {
            case TaskType.Idle: ExecuteIdle(); break;
            case TaskType.PatrolWaypoints: ExecutePatrol(); break;
            case TaskType.LookAtPlayer: ExecuteLookAtPlayer(); break;
            case TaskType.Chase: ExecuteChase(); break;
            case TaskType.Reposition: ExecuteReposition(); break;
            case TaskType.CirclePlayer: ExecuteCirclePlayer(); break;
            case TaskType.Retreat: ExecuteRetreat(); break;
            case TaskType.AttackMelee: ExecuteAttackMelee(); break;
            case TaskType.Prepare: ExecutePrepare(); break;
            case TaskType.CombatFootwork: ExecuteCombatFootwork(ws); break;
            case TaskType.DodgeBackward: ExecuteDodgeBackward(); break;
            case TaskType.Roll: ExecuteRoll(); break;
            default: ExecuteIdle(); break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Behaviour implementations
    // ─────────────────────────────────────────────────────────────────────────

    private void ExecuteIdle()
    {
        SetTargetLayerWeight(0f);
        SetSpeed(0f);
    }

    private void ExecutePatrol()
    {
        SetTargetLayerWeight(0f);

        if (patrolPoints == null || patrolPoints.Length == 0) return;

        if (_isResting)
        {
            _restTimer -= Time.deltaTime;
            SetSpeed(0f);
            if (_restTimer <= 0f) _isResting = false;
            return;
        }

        Transform target = patrolPoints[_currentPatrolIndex];
        LookAt(target.position);
        MoveTowards(target.position, personality.walkSpeed * 0.5f);
        SetSpeed(1f);
        SetAnimatorBlend(1f, 0f);

        if (Vector3.Distance(transform.position, target.position) < 2f)
        {
            _currentPatrolIndex = (_currentPatrolIndex + 1) % patrolPoints.Length;
            _isResting = true;
            _restTimer = Random.Range(personality.minRestTime, personality.maxRestTime);
        }
    }

    private void ExecuteLookAtPlayer()
    {
        SetTargetLayerWeight(1f);
        SetSpeed(0f);
        LookAt(player.position);
        Vector3 localVel = transform.InverseTransformDirection(agent.desiredVelocity);

        SetAnimatorBlend(0f, Mathf.Round(localVel.x));

        // Advance to next task after brief look
        if (_planner.CurrentTaskTimer > 0.8f)
            _planner.AdvanceTask();
    }

    private void ExecuteChase()
    {
        SetTargetLayerWeight(0f);
        Vector3 localVel = transform.InverseTransformDirection(agent.velocity);

        SetAnimatorBlend(Mathf.Round(localVel.z), Mathf.Round(localVel.x));
        SetSpeedFromMovement((player.position - transform.position).normalized, 2f);
        Vector3 dir = (player.position - transform.position).normalized;
        dir.y = 0f;
        //_controller.Move(dir * personality.chaseSpeed * Time.deltaTime);

        // Advance when we've closed to optimal range
        float dist = Vector3.Distance(transform.position, player.position);
        if (dist <= personality.optimalCombatRange + personality.distanceBuffer)
            _planner.AdvanceTask();
    }

    private void ExecuteReposition()
    {
        SetTargetLayerWeight(1f);

        int dir;
        // choose a direction once
        if (Random.value < 0.01)
            dir = -1; // -1 or 1 stored in the AI
        else
            dir = 1;

        Vector3 right = transform.right;

        Vector3 target = transform.position + right * dir * 3f;

        MoveTowards(target, personality.combatSpeed * 1.2f);

        SetAnimatorBlend(0f, dir);
        SetSpeed(0);
        if (_planner.CurrentTaskTimer > 1.5f)
            _planner.AdvanceTask();
    }

    private void ExecuteCirclePlayer()
    {
        SetTargetLayerWeight(1f);
        LookAt(player.position);

        if (Random.value < 0.01f)
            _orbitDirection *= -1;

        _orbitAngle += _orbitDirection * personality.orbitSpeed * Time.deltaTime;

        float ox = Mathf.Cos(_orbitAngle) * personality.orbitRadius;
        float oz = Mathf.Sin(_orbitAngle) * personality.orbitRadius;
        Vector3 orbitTarget = player.position + new Vector3(ox, 0, oz);

        MoveTowards(orbitTarget, personality.combatSpeed * 0.8f);
        SetSpeedFromMovement((orbitTarget - transform.position).normalized, 1f);

        Vector3 orbitMove = (orbitTarget - transform.position).normalized;
        Vector3 localOrbit = transform.InverseTransformDirection(orbitMove);
        SetAnimatorBlend(Mathf.Round(localOrbit.z), Mathf.Round(localOrbit.x));
    }

    private void ExecuteRetreat()
    {
        SetTargetLayerWeight(1f);

        Vector3 dir = (transform.position - player.position).normalized;
        Vector3 lateral = Vector3.Cross(Vector3.up, dir) * Mathf.Sin(Time.time * 2f) * 0.3f;
        Vector3 moveDir = (dir + lateral).normalized;

        MoveInDirection(moveDir, personality.combatSpeed);
        SetSpeedFromMovement(moveDir, 1f);

        LookAt(player.position);
        Vector3 toPlayer = transform.InverseTransformDirection(
           (player.position - transform.position).normalized);
        SetAnimatorBlend(toPlayer.z * 0.5f, toPlayer.x * 0.5f);

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist >= personality.maxRetreatDistance || _planner.CurrentTaskTimer > 2f)
            _planner.AdvanceTask();
    }

    private void ExecuteAttackMelee()
    {
        // Fire only on the first frame of this task
        if (_planner.CurrentTaskTimer <= Time.deltaTime * 1.5f)
        {
            int attackType = Random.Range(1, 3);
            _animator.SetInteger("attackType", attackType);
            _animator.SetBool("isAttacking", true);
            _lastAttackTime = Time.time;
            _consecutiveAttacks++;

            // Open the weapon collider — WeaponHitbox.OnTriggerEnter handles the rest
            _weaponHitbox?.Enable();

            // Fallback: no weapon prefab, use plain distance + HealthSystem
            if (_weaponHitbox == null && _playerHealth != null)
            {
                float dist = Vector3.Distance(transform.position, player.position);
                if (dist <= personality.meleeAttackRange)
                    _playerHealth.TakeDamage(damage);
            }
        }

        // Advance once the animation finishes
        if (!_animator.GetBool("isAttacking") && _planner.CurrentTaskTimer > 0.2f)
        {
            _weaponHitbox?.Disable();

            // Chance to reset consecutive count (mirrors original logic)
            if (_consecutiveAttacks >= personality.maxConsecutiveAttacks)
                _consecutiveAttacks = 0;

            _planner.AdvanceTask();
        }
    }

    private void ExecutePrepare()
    {
        SetTargetLayerWeight(1f);
        SetSpeed(0f);
        Vector3 localVel = transform.InverseTransformDirection(agent.desiredVelocity);

        SetAnimatorBlend(0f, Mathf.Round(localVel.x));

        if (_planner.CurrentTaskTimer > 1f)
            _planner.AdvanceTask();
    }

    private void ExecuteCombatFootwork(WorldState ws)
    {
        // Init footwork offset on first frame
        if (_planner.CurrentTaskTimer <= Time.deltaTime * 1.5f)
        {
            _footworkTimer = Random.Range(personality.minFootworkTime, personality.maxFootworkTime);
            _footworkOffset = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
        }

        SetTargetLayerWeight(1f);

        float dist = ws.distanceToPlayer;
        Vector3 toPlayer = (player.position - transform.position).normalized;
        Vector3 lateral = Vector3.Cross(Vector3.up, toPlayer) * _footworkOffset.x;
        Vector3 rangeCorrect = Vector3.zero;
        float buf = personality.distanceBuffer;
        float opt = personality.optimalCombatRange;

        if (dist > opt + buf) rangeCorrect = toPlayer * 0.4f;
        else if (dist < opt - buf) rangeCorrect = -toPlayer * 0.4f;

        Vector3 moveDir = (lateral + rangeCorrect).normalized;
        MoveInDirection(moveDir, personality.footworkSpeed);

        Vector3 localMove = transform.InverseTransformDirection(moveDir);
        SetAnimatorBlend(Mathf.Round(localMove.z), Mathf.Round(localMove.x));
        SetSpeedFromMovement(moveDir, 1f);

        _footworkTimer -= Time.deltaTime;
        if (_footworkTimer <= 0f)
            _planner.AdvanceTask();
    }

    private void ExecuteDodgeBackward()
    {
        if (_planner.CurrentTaskTimer <= Time.deltaTime * 1.5f)
        {
            SetTargetLayerWeight(1f);
            _animator.SetTrigger("dodgeBackwards");
            _lastRollTime = Time.time;
        }

        Vector3 dodgeDir = (transform.position - player.position).normalized;
        dodgeDir.y = 0f;
        MoveInDirection(dodgeDir, personality.combatSpeed * 1.8f);

        if (_planner.CurrentTaskTimer > 0.6f)
        {
            _animator.ResetTrigger("dodgeBackwards");
            _planner.AdvanceTask();
        }
    }

    private void ExecuteRoll()
    {
        if (_planner.CurrentTaskTimer <= Time.deltaTime * 1.5f)
        {
            SetTargetLayerWeight(0f);
            _animator.SetTrigger("Roll");
            _lastRollTime = Time.time;
        }

        Vector3 rollDir = (transform.position - player.position).normalized;
        rollDir.y = 0f;
        //_controller.Move(rollDir * personality.combatSpeed * 1.8f * Time.deltaTime);

        if (_planner.CurrentTaskTimer > 0.8f)
            _planner.AdvanceTask();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Engage — used for TacticalEngage (in-range combat stance)
    //  This is a continuous behaviour that runs while the planner keeps
    //  assigning CirclePlayer or CombatFootwork tasks.
    // ─────────────────────────────────────────────────────────────────────────

    // NOTE: TacticalEngage is implemented as CombatFootwork + CirclePlayer
    // combined; the planner naturally produces those tasks in sequence.

    // ─────────────────────────────────────────────────────────────────────────
    //  Player behavior analysis
    // ─────────────────────────────────────────────────────────────────────────

    private void AnalyzePlayerBehavior()
    {
        if (!player) return;

        Vector3 current = player.position;
        Vector3 displacement = current - _lastPlayerPos;
        float dt = Time.deltaTime;

        if (dt <= Mathf.Epsilon) { _lastPlayerPos = current; return; }

        Vector3 toEnemy = transform.position - current;
        displacement.y = 0f;
        toEnemy.y = 0f;

        float playerSpeed = displacement.magnitude / dt;
        if (playerSpeed < 0.1f)
        {
            _playerApproaching = false;
            _playerRetreating = false;
            _smoothedApproach = Mathf.Lerp(_smoothedApproach, 0f, 0.1f);
            _lastPlayerPos = current;
            return;
        }

        float approachSpeed = Vector3.Dot(displacement, toEnemy.normalized) / dt;
        _smoothedApproach = Mathf.Lerp(_smoothedApproach, approachSpeed, 0.1f);

        const float threshold = 0.2f;
        if (_smoothedApproach > threshold) { _playerApproaching = true; _playerRetreating = false; }
        else if (_smoothedApproach < -threshold) { _playerApproaching = false; _playerRetreating = true; }
        else { _playerApproaching = false; _playerRetreating = false; }

        _lastPlayerPos = current;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Vision cone detection (unchanged from original)
    // ─────────────────────────────────────────────────────────────────────────

    private bool CheckPlayerInVisionCone()
    {
        Vector3 dir = (player.position - transform.position).normalized;
        float dist = Vector3.Distance(transform.position, player.position);

        if (dist >= personality.detectionRadius) return false;

        float angle = Vector3.Angle(transform.forward, dir);
        if (angle >= personality.detectionAngle) return false;

        Vector3 rayOrigin = transform.position + Vector3.up * 1.5f;
        if (Physics.Raycast(rayOrigin, dir, out RaycastHit hit, personality.detectionRadius))
            return hit.collider.CompareTag("Player");

        return false;
    }

    private bool CheckPlayerInfront()
    {
        Vector3 dir = (player.position - transform.position).normalized;
        float dist = Vector3.Distance(transform.position, player.position);

        if (dist >= personality.detectionRadius) return false;

        float angle = Vector3.Angle(transform.forward, dir);
        if (angle >= 45) return false; // not infront (45 is a small degree to check if infront)

        Vector3 rayOrigin = transform.position + Vector3.up * 1.5f;
        if (Physics.Raycast(rayOrigin, dir, out RaycastHit hit, personality.detectionRadius))
            return hit.collider.CompareTag("Player");

        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Movement helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplyGravity()
    {
        if (_controller.isGrounded && _velocity.y < 0f)
            _velocity.y = 0f;

        _velocity.y += gravity * Time.deltaTime;   // gravity is negative already
        _controller.Move(_velocity * Time.deltaTime);
    }

    private void MoveInDirection(Vector3 dir, float spd)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        //_controller.Move(dir.normalized * spd * Time.deltaTime);
    }

    private void MoveTowards(Vector3 target, float spd)
    {
        Vector3 dir = (target - transform.position).normalized;
        dir.y = 0f;
        //_controller.Move(dir * spd * Time.deltaTime);
    }

    private void LookAt(Vector3 target)
    {
        Vector3 dir = (target - transform.position).normalized;
        dir.y = 0f;
        if (dir == Vector3.zero) return;
        transform.rotation = Quaternion.Lerp(transform.rotation,
                                             Quaternion.LookRotation(dir),
                                             Time.deltaTime * 8f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Animator helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void SetTargetLayerWeight(float w)
    {
        int idx = _animator.GetLayerIndex("Target_layer");
        if (idx >= 0) _animator.SetLayerWeight(idx, w);
    }

    private void SetAnimatorBlend(float vertical, float horizontal)
    {
        _animator.SetFloat("Vertical", vertical, 0.1f, Time.deltaTime);
        _animator.SetFloat("Horizontal", horizontal, 0.1f, Time.deltaTime);
    }

    private void SetSpeed(float value, float damp = 0.15f)
    {
        _animator.SetFloat("speed", value, damp, Time.deltaTime);
    }

    private void SetSpeedFromMovement(Vector3 worldMoveDir, float maxSpeed)
    {
        float magnitude = worldMoveDir.sqrMagnitude > 0.001f ? maxSpeed : 0f;
        _animator.SetFloat("speed", magnitude, 0.15f, Time.deltaTime);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Debug
    // ─────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!personality) return;

        // Detection radius
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, personality.detectionRadius);

        // Optimal combat range
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, personality.optimalCombatRange);

        // Melee range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, personality.meleeAttackRange);
    }

    private void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (_planner == null) return;

        Vector3 screen = Camera.main
            ? Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2.5f)
            : Vector3.zero;

        if (screen.z < 0) return;
        screen.y = Screen.height - screen.y;

        string label = $"[{personality.personalityName}]\n"
                     + $"Task: {_currentTask}\n"
                     + $"Plan: {_planner.LastMethodName}";

        GUI.Label(new Rect(screen.x - 80, screen.y, 180, 60), label);
    }
#endif
}
