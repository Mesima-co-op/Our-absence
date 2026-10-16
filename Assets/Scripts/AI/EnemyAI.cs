using System.Collections;
using System.Collections.Generic;
using UnityEditor.ShaderGraph.Internal;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

public class EnemyAI : MonoBehaviour
{
    #region Enums

    public enum State
    {
        Idle,
        Patrol,
        Detect_Player,
        Chase,
        Attack_Melee,
        Attack_Ranged,
        Patrol_Player,
        Retreat,
        Prepare,
        Engage,
        Reposition,
        Roll,
        Standing_Dodge_Backward,
        CombatFootwork
    }

    #endregion

    #region Inspector Fields

    [Header("References")]
    public Transform player;

    [Header("Movement")]
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private float speed;
    public State currentState;
    public float gravity = -9.81f;

    [Header("Speed Settings")]
    [SerializeField] private float walkSpeed = 2f;
    [SerializeField] private float combatSpeed = 3.5f;
    [SerializeField] private float footworkSpeed = 1.8f;
    [SerializeField] private float chaseSpeed = 5.5f;

    [Header("Combat")]
    [SerializeField] private float meleeAttackRange = 2f;
    [SerializeField] private float stopChasingRange = 10f;
    [SerializeField] private float optimalCombatRange = 4f;
    [SerializeField] private float distanceBuffer = 0.5f;

    [Header("Detection")]
    public float detectionRadius = 10f;
    public float detectionAngle = 45f;
    public float maxRetreatDistance = 5f;

    [Header("Orbit / Strafe")]
    [SerializeField] private float orbitRadius = 7f;
    [SerializeField] private float orbitSpeed = 1.5f;
    [SerializeField] private float strafeJitter = 0.3f;

    [Header("Tactical Combat System")]
    [Range(0f, 1f)] public float aggression = 0.6f;
    [Range(0f, 1f)] public float caution = 0.3f;
    public float combatStateTimer = 0f;

    [Header("Combat Conditions")]
    public int consecutiveAttacks = 0;
    public int maxConsecutiveAttacks = 2;
    public float lastAttackTime = 0f;
    public float attackCooldown = 1.5f;
    public bool playerMovingTowardsMe = false;
    public bool playerMovingAway = false;
    public Vector3 lastPlayerPosition;
    [Header("Combat Timing")]
    public float minCombatTime = 1.5f;
    public float maxCombatTime = 4f;

    // After each decision the enemy does a brief footwork micro-move before committing.
    [SerializeField] private float minFootworkTime = 0.4f;
    [SerializeField] private float maxFootworkTime = 1.2f;

    [Header("Player Analysis")]
    public float minSpeed = 0.1f;
    public float approachThreshold = 0.2f;
    public bool ignoreYForAnalysis = true;
    [Range(0f, 1f)] public float smoothing = 0.1f;

    [Header("State Transition Control")]
    public float stateTransitionCooldown = 0.3f;

    [Header("Pathfinding")]
    public NavMeshAgent agent;

    [Header("Weapons and Damage")]
    public GameObject hend;
    public GameObject weapon;
    public int damage = 0;

    [Header("Patrol Rest")]
    [SerializeField] private float minRestTime = 1f;
    [SerializeField] private float maxRestTime = 3f;

    [Header("Base Layer Animation")]
    [SerializeField] private float rollCooldown = 3f;
    [SerializeField][Range(0f, 1f)] private float rollChance = 0.3f;


    #endregion

    #region Private Fields

    private Animator animator;
    private CharacterController characterController;

    private Vector3 velocity;
    private int currentPatrolIndex = 0;
    private int orbitDirection = 1;
    private float orbitAngle = 0f;
    private float decisionTimer;
    private float smoothedApproachSpeed = 0f;
    private float lastStateChangeTime = 0f;
    private float restTimer = 0f;
    private bool isResting = false;
    private float lastRollTime = 0f;
    private float footworkTimer = 0f;
    private Vector3 footworkOffset = Vector3.zero;


    #endregion

    #region Unity Lifecycle

    void Start()
    {
        animator = GetComponent<Animator>();
        characterController = GetComponent<CharacterController>();

        currentState = State.Idle;
        lastPlayerPosition = player ? player.position : Vector3.zero;
        decisionTimer = Random.Range(minCombatTime, maxCombatTime);
        InitializeWeapon();
    }

    void Update()
    {
        ApplyGravity();

        if (!player) return;

        decisionTimer -= Time.deltaTime;
        combatStateTimer += Time.deltaTime;

        AnalyzePlayerBehavior();
        CheckState();
        HandleState();
    }

    #endregion

    #region Initialization

    private void InitializeWeapon()
    {
        if (weapon)
        {
            // TODO: Attach weapon to hend and set damage from weapon component
        }
        else
        {
            damage = 2; // Default unarmed damage
        }
    }

    #endregion

    #region State Machine

    private void CheckState()
    {
        float distance = Vector3.Distance(player.position, transform.position);
        bool canAttack = Time.time - lastAttackTime > attackCooldown;
        bool shouldReassess = combatStateTimer > maxCombatTime;
        bool hasMinTime = combatStateTimer >= minCombatTime;
        bool canChange = Time.time - lastStateChangeTime > stateTransitionCooldown;

        if (!canChange) return;

        switch (currentState)
        {
            case State.Idle: CheckStateFromIdle(); break;
            case State.Patrol: CheckStateFromPatrol(distance); break;
            case State.Detect_Player: CheckStateFromDetect(distance, hasMinTime); break;
            case State.Engage: CheckStateFromEngage(distance, canAttack, hasMinTime, shouldReassess); break;
            case State.Chase: CheckStateFromChase(distance); break;
            case State.Patrol_Player: CheckStateFromPatrolPlayer(distance, canAttack, hasMinTime, shouldReassess); break;
            case State.Attack_Melee: CheckStateFromMeleeAttack(distance); break;
            case State.Reposition: CheckStateFromReposition(distance, hasMinTime); break;
            case State.Retreat: CheckStateFromRetreat(distance, hasMinTime); break;
            case State.Prepare: CheckStateFromPrepare(hasMinTime); break;
            case State.Standing_Dodge_Backward: CheckStateFromDodge(); break;
            case State.CombatFootwork: CheckStateFromFootwork(distance, canAttack); break; // WAS MISSING
            default: ChangeState(State.Idle); break;
        }
    }

    private void HandleState()
    {
        switch (currentState)
        {
            case State.Idle: break;
            case State.Patrol: PatrolPath(); break;
            case State.Detect_Player: LookAt(player.position); break;
            case State.Engage: TacticalEngage(); break;
            case State.Reposition: TacticalReposition(); break;
            case State.Patrol_Player: CircleAroundPlayer(); break;
            case State.Retreat: RetreatFromPlayer(); break;
            case State.Prepare: PrepareForCombat(); break;
            case State.Chase: MoveForward(); break;
            case State.Attack_Melee: TriggerTacticalAttack(); break;
            case State.Attack_Ranged: animator.SetTrigger("Shoot"); break;
            case State.Standing_Dodge_Backward: HandleDodge(); break;
            case State.CombatFootwork: HandleCombatFootwork(); break; // WAS MISSING
        }
    }

    private void ChangeState(State newState)
    {
        if (currentState == newState) return;

        currentState = newState;
        lastStateChangeTime = Time.time;
        combatStateTimer = 0f;
        decisionTimer = Random.Range(minCombatTime, maxCombatTime); // ADD THIS — always fresh timer

        if (newState == State.CombatFootwork)
        {
            footworkTimer = Random.Range(minFootworkTime, maxFootworkTime);
            footworkOffset = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
        }
    }

    #endregion

    #region State Transition Checks

    private void CheckStateFromIdle()
    {
        if (patrolPoints.Length > 0)
            ChangeState(State.Patrol);
        else if (CheckPlayerInVisionCone())
            ChangeState(State.Detect_Player);
    }

    private void CheckStateFromPatrol(float distance)
    {
        if (CheckPlayerInVisionCone() && distance <= detectionRadius)
            ChangeState(State.Detect_Player);
    }

    private void CheckStateFromDetect(float distance, bool hasMinTime)
    {
        // Always engage once detected — don't gate on stopChasingRange here
        if (hasMinTime || distance < detectionRadius)
            ChangeState(State.Engage);
    }

    private void CheckStateFromEngage(float distance, bool canAttack, bool hasMinTime, bool shouldReassess)
    {
        if (distance > stopChasingRange + distanceBuffer)
        {
            ChangeState(State.Idle);
            return;
        }

        if (distance <= meleeAttackRange && canAttack)
        {
            ChangeState(State.Attack_Melee);
            return;
        }

        if (distance > optimalCombatRange + distanceBuffer)
        {
            ChangeState(State.Chase);
            return;
        }

        // ONLY reassess after BOTH minCombatTime AND maxCombatTime have passed — not decisionTimer
        if (hasMinTime && shouldReassess)
            ChangeState(State.CombatFootwork);
    }

    private void CheckStateFromChase(float distance)
    {
        if (distance > stopChasingRange + distanceBuffer)
        {
            ChangeState(State.Idle);
            return;
        }

        // Minimum time in Chase before any transition — prevents instant flip
        if (combatStateTimer < 0.5f) return;

        if (distance <= meleeAttackRange + distanceBuffer)
        {
            ChangeState(State.Attack_Melee);
            return;
        }

        if (distance <= optimalCombatRange + distanceBuffer)
            ChangeState(State.Engage);
    }

    private void CheckStateFromPatrolPlayer(float distance, bool canAttack, bool hasMinTime, bool shouldReassess)
    {
        if (distance > stopChasingRange + distanceBuffer) { ChangeState(State.Idle); return; }

        if (distance <= meleeAttackRange && canAttack && CheckPlayerInVisionCone())
            ChangeState(State.Attack_Melee);
        else if (hasMinTime && shouldReassess)
            ChangeState(State.CombatFootwork);
    }

    private void CheckStateFromMeleeAttack(float distance)
    {
        if (IsAttacking()) return;

        if (CanRoll() && Random.value < rollChance)
            ChangeState(State.Standing_Dodge_Backward);
        else
            ChangeState(State.CombatFootwork);
    }

    private void CheckStateFromReposition(float distance, bool hasMinCombatTime)
    {
        if (hasMinCombatTime && (combatStateTimer > 2f || distance > stopChasingRange))
            ChangeState(State.Engage);
    }

    private void CheckStateFromRetreat(float distance, bool hasMinTime)
    {
        if (!hasMinTime) return;

        if (distance > maxRetreatDistance + distanceBuffer || Random.value < 0.3f)
            ChangeState(State.CombatFootwork);
    }

    private void CheckStateFromPrepare(bool hasMinCombatTime)
    {
        if (hasMinCombatTime && combatStateTimer > 1f)
            ChangeState(State.Engage);
    }

    private void CheckStateFromDodge()
    {
        if (combatStateTimer > 0.6f)
        {
            animator.ResetTrigger("dodgeBackwards");
            ChangeState(State.CombatFootwork);
        }
    }

    private void CheckStateFromFootwork(float distance, bool canAttack)
    {
        footworkTimer -= Time.deltaTime;
        if (footworkTimer > 0f) return;
        if (combatStateTimer < 0.3f) return; // safety: never decide on first frames

        DecideTacticalAction(distance, canAttack);
    }

    #endregion

    #region Tactical Decision Making

    private void DecideTacticalAction(float distance, bool canAttack)
    {
        float aggressionScore = CalculateAggressionScore(distance);
        float cautionScore = CalculateCautionScore(distance);

        decisionTimer = Random.Range(minCombatTime, maxCombatTime);

        if (TryAttack(distance, canAttack, aggressionScore, cautionScore)) return;
        if (TryReposition()) return;
        if (TryCounterPlayerApproach(aggressionScore)) return;
        if (TryPursueRetreatingPlayer(distance)) return;

        // ONE clean fallback — no double ChangeState
        if (distance < meleeAttackRange - distanceBuffer && cautionScore > aggressionScore)
            ChangeState(State.Retreat);
        else if (distance > optimalCombatRange + distanceBuffer)
            ChangeState(State.Chase);
        else if (Random.value < aggression)
            ChangeState(State.Engage);
        else
            ChangeState(State.Patrol_Player);

        if (Random.value < aggression)
            ChangeState(State.Chase);
    }

    private bool TryAttack(float distance, bool canAttack, float aggressionScore, float cautionScore)
    {
        if (distance <= meleeAttackRange && canAttack && aggressionScore > cautionScore)
        {
            if (consecutiveAttacks < maxConsecutiveAttacks)
            {
                ChangeState(State.Attack_Melee);
                consecutiveAttacks++;
                lastAttackTime = Time.time;
                return true;
            }
        }
        return false;
    }

    private bool TryReposition()
    {
        if (consecutiveAttacks >= maxConsecutiveAttacks)
        {
            ChangeState(State.Reposition);
            consecutiveAttacks = 0;
            return true;
        }
        return false;
    }

    private bool TryCounterPlayerApproach(float aggressionScore)
    {
        if (playerMovingTowardsMe)
        {
            if (CanRoll() && Random.value < rollChance)
            {
                ChangeState(State.Standing_Dodge_Backward);       // dodge the incoming player
                return true;
            }
            ChangeState(aggressionScore > 0.7f ? State.Prepare : State.Retreat);
            return true;
        }
        return false;
    }

    private bool TryPursueRetreatingPlayer(float distance)
    {
        if (playerMovingAway && distance > meleeAttackRange)
        {
            ChangeState(distance > optimalCombatRange + distanceBuffer ? State.Chase : State.Patrol_Player);
            return true;
        }
        return false;
    }

    private float CalculateAggressionScore(float distance)
    {
        float score = aggression;

        if (distance <= optimalCombatRange)
            score += 0.2f;
        if (playerMovingTowardsMe)
            score += 0.3f;
        if (consecutiveAttacks >= maxConsecutiveAttacks - 1)
            score -= 0.4f;

        return Mathf.Clamp01(score);
    }

    private float CalculateCautionScore(float distance)
    {
        float score = caution;

        if (distance < meleeAttackRange * 0.7f)
            score += 0.3f;
        if (playerMovingAway)
            score += 0.2f;
        if (consecutiveAttacks > 0)
            score += 0.1f * consecutiveAttacks;

        return Mathf.Clamp01(score);
    }

    private void ResetCombatState()
    {
        combatStateTimer = 0f;
    }

    #endregion

    #region Player Analysis

    private void AnalyzePlayerBehavior()
    {
        if (!player) return;

        Vector3 currentPlayerPos = player.position;
        Vector3 displacement = currentPlayerPos - lastPlayerPosition;
        float dt = Time.deltaTime;

        if (dt <= Mathf.Epsilon)
        {
            lastPlayerPosition = currentPlayerPos;
            return;
        }

        Vector3 toEnemy = transform.position - currentPlayerPos;
        if (ignoreYForAnalysis)
        {
            displacement.y = 0f;
            toEnemy.y = 0f;
        }

        float playerSpeed = displacement.magnitude / dt;
        if (playerSpeed < minSpeed)
        {
            ClearPlayerMovementFlags();
            smoothedApproachSpeed = Mathf.Lerp(smoothedApproachSpeed, 0f, smoothing);
            lastPlayerPosition = currentPlayerPos;
            return;
        }

        float approachSpeed = Vector3.Dot(displacement, toEnemy.normalized) / dt;
        smoothedApproachSpeed = Mathf.Lerp(smoothedApproachSpeed, approachSpeed, smoothing);

        UpdatePlayerMovementFlags();

        lastPlayerPosition = currentPlayerPos;
    }

    private void ClearPlayerMovementFlags()
    {
        playerMovingTowardsMe = false;
        playerMovingAway = false;
    }

    private void UpdatePlayerMovementFlags()
    {
        if (smoothedApproachSpeed > approachThreshold)
        {
            playerMovingTowardsMe = true;
            playerMovingAway = false;
        }
        else if (smoothedApproachSpeed < -approachThreshold)
        {
            playerMovingTowardsMe = false;
            playerMovingAway = true;
        }
        else
        {
            ClearPlayerMovementFlags();
        }
    }

    #endregion

    #region Combat Actions

    private void TriggerTacticalAttack()
    {
        if (combatStateTimer > 0.1f) return; // guard: only fire on first frame

        LookAt(player.position);
        int attackType = Random.Range(1, 4); // 3 attack variations
        animator.SetInteger("attackType", attackType);
        animator.SetBool("isAttacking", true);
        lastAttackTime = Time.time;
    }

    private int DetermineAttackType()
    {
        float distance = Vector3.Distance(transform.position, player.position);
        return (distance < meleeAttackRange + distanceBuffer) ? Random.Range(1, 2) : 0;
    }

    public bool IsAttacking()
    {
        return animator.GetBool("isAttacking");
    }

    private bool CanRoll()
    {
        return Time.time - lastRollTime > rollCooldown;
    }

    #endregion

    #region State Behaviours

    private void TacticalEngage()
    {
        animator.SetLayerWeight(animator.GetLayerIndex("Target_layer"), 1f);
        LookAt(player.position);

        float distance = Vector3.Distance(transform.position, player.position);

        if (distance > optimalCombatRange + distanceBuffer)
        {
            MoveInDirection((player.position - transform.position).normalized, combatSpeed);
            SetAnimatorBlend(1f, 0f);
            SetSpeed(1.5f);
        }
        else if (distance < optimalCombatRange - distanceBuffer)
        {
            MoveInDirection((transform.position - player.position).normalized, combatSpeed * 0.6f);
            SetAnimatorBlend(-1f, 0f);
            SetSpeed(1f);
        }
        else
        {
            // Gentle sway — enemy looks alive instead of frozen
            float sway = Mathf.Sin(Time.time * orbitSpeed) * 0.5f;
            Vector3 right = Vector3.Cross(Vector3.up, (player.position - transform.position).normalized);
            MoveInDirection(right * sway, combatSpeed * 0.3f);
            SetAnimatorBlend(0f, sway);
            SetSpeed(0.5f);
        }
    }

    private void TacticalReposition()
    {
        animator.SetLayerWeight(animator.GetLayerIndex("Target_layer"), 1f);

        Vector3 toPlayer = (player.position - transform.position).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, toPlayer);
        int direction = (transform.position.x > player.position.x) ? 1 : -1;
        Vector3 repositionTarget = transform.position + right * direction * 3f;

        MoveTowards(repositionTarget, speed * 1.2f);
        LookAt(player.position);
        SetAnimatorBlend(0f, Mathf.Sign(direction));
    }

    private void PrepareForCombat()
    {
        animator.SetLayerWeight(animator.GetLayerIndex("Target_layer"), 1f);
        SetAnimatorBlend(0f, 0f);
        LookAt(player.position);
        SetSpeed(0f);
    }

    private void PatrolPath()
    {
        animator.SetLayerWeight(animator.GetLayerIndex("Target_layer"), 0f);


        if (patrolPoints.Length == 0) return;

        if (isResting)
        {
            restTimer -= Time.deltaTime;
            SetSpeed(0f); // idle animation
            if (restTimer <= 0f)
                isResting = false;
            return;
        }

        Transform target = patrolPoints[currentPatrolIndex];

        LookAt(target.position);
        speed = walkSpeed;
        MoveTowards(target.position, speed * 0.5f);
        SetSpeed(1f);   // 0=idle, 1=walk, 2=run in your blend tree
        SetAnimatorBlend(1f, 0f);

        if (Vector3.Distance(transform.position, target.position) < 2f)
        {
            currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
            isResting = true;
            restTimer = Random.Range(minRestTime, maxRestTime);
        }
    }

    private void CircleAroundPlayer()
    {
        animator.SetLayerWeight(animator.GetLayerIndex("Target_layer"), 1f);

        if (Random.value < 0.01f)
            orbitDirection *= -1;

        orbitAngle += orbitDirection * orbitSpeed * Time.deltaTime;

        float offsetX = Mathf.Cos(orbitAngle) * orbitRadius;
        float offsetZ = Mathf.Sin(orbitAngle) * orbitRadius;
        Vector3 orbitTarget = player.position + new Vector3(offsetX, 0, offsetZ);

        MoveTowards(orbitTarget, speed * 0.8f);
        LookAt(player.position);
        SetAnimatorBlend(Mathf.Sign(offsetZ), Mathf.Sign(offsetX));
    }

    private void RetreatFromPlayer()
    {
        animator.SetLayerWeight(animator.GetLayerIndex("Target_layer"), 1f);

        Vector3 dir = (transform.position - player.position).normalized;
        Vector3 lateral = Vector3.Cross(Vector3.up, dir) * Mathf.Sin(Time.time * 2f) * 0.3f;
        Vector3 moveDir = (dir + lateral).normalized;

        MoveInDirection(moveDir, combatSpeed);
        LookAt(player.position);
        SetAnimatorBlend(-1f, lateral.x > 0 ? 0.3f : -0.3f);
        SetSpeed(1.5f);
    }

    private void MoveForward()
    {
        animator.SetLayerWeight(animator.GetLayerIndex("Target_layer"), 1f);

        speed = chaseSpeed;                          // switch to run speed
        SetSpeed(2f);           // drive blend tree to Run

        Vector3 dir = (player.position - transform.position).normalized;
        dir.y = 0f;
        characterController.Move(dir * speed * Time.deltaTime);
        LookAt(player.position);
        SetAnimatorBlend(1f, 0f);
    }

    private void HandleRoll()
    {
        animator.SetLayerWeight(animator.GetLayerIndex("Target_layer"), 0f); // Base Layer owns the roll
        animator.SetTrigger("Roll");

        animator.SetTrigger("Roll");
        lastRollTime = Time.time;

        // Roll away from player
        Vector3 rollDir = (transform.position - player.position).normalized;
        rollDir.y = 0f;
        characterController.Move(rollDir * speed * 1.8f * Time.deltaTime);
    }

    private void HandleDodge()
    {
        if (combatStateTimer < 0.05f)
        {
            animator.SetLayerWeight(animator.GetLayerIndex("Target_layer"), 0f);
            animator.SetTrigger("dodgeBackwards");
            lastRollTime = Time.time;
        }

        Vector3 dodgeDir = (transform.position - player.position).normalized;
        dodgeDir.y = 0f;
        MoveInDirection(dodgeDir, combatSpeed * 1.8f);
    }

    private void HandleCombatFootwork()
    {
        animator.SetLayerWeight(animator.GetLayerIndex("Target_layer"), 1f);
        LookAt(player.position);

        float distance = Vector3.Distance(transform.position, player.position);
        Vector3 toPlayer = (player.position - transform.position).normalized;
        Vector3 lateral = Vector3.Cross(Vector3.up, toPlayer) * footworkOffset.x;

        Vector3 rangeCorrection = Vector3.zero;
        if (distance > optimalCombatRange + distanceBuffer)
            rangeCorrection = toPlayer * 0.4f;
        else if (distance < optimalCombatRange - distanceBuffer)
            rangeCorrection = -toPlayer * 0.4f;

        Vector3 moveDir = (lateral + rangeCorrection).normalized;
        MoveInDirection(moveDir, footworkSpeed);

        float lateralDot = Vector3.Dot(moveDir, Vector3.Cross(Vector3.up, toPlayer));
        SetAnimatorBlend(rangeCorrection.magnitude > 0.1f ? Mathf.Sign(Vector3.Dot(moveDir, toPlayer)) * 0.4f : 0f, lateralDot);
        SetSpeed(1f);
    }

    #endregion

    #region Movement Helpers

    private void ApplyGravity()
    {
        if (characterController.isGrounded && velocity.y < 0f)
            velocity.y = 0f;

        velocity.y -= gravity * Time.deltaTime;
        characterController.Move(velocity * Time.deltaTime);
    }

    private void MoveInDirection(Vector3 dir, float spd)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        characterController.Move(dir.normalized * spd * Time.deltaTime);
    }

    private void MoveTowards(Vector3 target, float spd)
    {
        Vector3 dir = (target - transform.position).normalized;
        dir.y = 0f;
        characterController.Move(dir * spd * Time.deltaTime);
    }

    private void LookAt(Vector3 targetPosition)
    {
        Vector3 dir = (targetPosition - transform.position).normalized;
        dir.y = 0;
        if (dir != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * 8f);
        }


    }


    #endregion

    #region Animator Helpers

    private void SetAnimatorBlend(float vertical, float horizontal)
    {
        animator.SetFloat("Vertical", vertical, 0.1f, Time.deltaTime);
        animator.SetFloat("Horizontal", horizontal, 0.1f, Time.deltaTime);
    }

    private void SetSpeed(float value)
    {
        animator.SetFloat("speed", value, 0.1f, Time.deltaTime);
    }

    #endregion

    #region Detection

    private bool CheckPlayerInVisionCone()
    {
        Vector3 dirToPlayer = (player.position - transform.position).normalized;
        float distance = Vector3.Distance(transform.position, player.position);

        if (distance >= detectionRadius) return false;

        float angle = Vector3.Angle(transform.forward, dirToPlayer);
        if (angle >= detectionAngle) return false;

        Vector3 rayOrigin = transform.position + Vector3.up * 1.5f;
        if (Physics.Raycast(rayOrigin, dirToPlayer, out RaycastHit hit, detectionRadius))
            return hit.collider.CompareTag("Player");

        return false;
    }

    #endregion
}