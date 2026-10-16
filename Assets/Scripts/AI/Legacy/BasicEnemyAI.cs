using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization.Formatters;
using UnityEngine;

public class BasicEnemyAI : MonoBehaviour
{
    private Animator animator;
    private CharacterController controller;
    private Transform player;

    [Header("Movement")]
    public float speed = 2f; // how fast enemy moves
    public State currentState;

    [Header("Combat")]
    [SerializeField] private float meleeAttackRange = 2f;
    [SerializeField] private float stopChasingRange = 10f;

    [Header("Detection")]
    public float detectionRadius = 10f;
    public float detectionAngle = 45f;

    public enum State
    {
        Idle,
        Patrol,
        Chase,
        Attack_Melee,
        Attack_Ranged
    }

    void Start()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        currentState = State.Idle;

        player = GameObject.FindGameObjectWithTag("Player")?.transform;
    }

    void Update()
    {
        if (!player) return;

        CheckState();
        HandleState();
    }

    private void HandleState()
    {
        switch (currentState)
        {
            case State.Idle:
                SetSpeed(0f); // idle
                break;

            case State.Patrol:
                SetSpeed(0.5f); // slow walk
                break;

            case State.Chase:
                // Look at player
                Vector3 dir = (player.position - transform.position).normalized;
                dir.y = 0;
                if (dir != Vector3.zero)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 5f);

                // Move
                controller.Move(transform.forward * speed * Time.deltaTime);

                SetSpeed(2f); // running in blend tree
                break;

            case State.Attack_Melee:
                SetSpeed(0f); // stop movement
                TriggerRandomAttack();
                break;

            case State.Attack_Ranged:
                SetSpeed(0f);
                animator.SetTrigger("Shoot"); // if you add a ranged anim
                break;
        }
    }

    private void CheckState()
    {
        float distance = Vector3.Distance(player.position, transform.position);

        switch (currentState)
        {
            case State.Idle:
                if (CheckPlayerInVisionCone())
                    currentState = State.Chase;
                break;

            case State.Patrol:
                // TODO: patrol logic
                if (CheckPlayerInVisionCone())
                    currentState = State.Chase;
                break;

            case State.Chase:
                if (distance > stopChasingRange)
                {
                    currentState = State.Idle;
                }
                else if (distance < meleeAttackRange && CheckPlayerInVisionCone())
                {
                    currentState = State.Attack_Melee;
                }
                break;

            case State.Attack_Melee:
                if (distance > meleeAttackRange || !CheckPlayerInVisionCone())
                {
                    if (!IsAttacking())
                    {
                        currentState = State.Chase;
                    }
                }
                break;

            case State.Attack_Ranged:
                break;
        }
    }

    private bool IsAttacking()
    {
        return animator.GetBool("isAttacking");
    }

    private void SetSpeed(float value)
    {
        animator.SetFloat("speed", value); // drives the blend tree
    }

    private void TriggerRandomAttack()
    {
        int attackType = Random.Range(0, 2); // 0 or 1 (two boxing animations)
        animator.SetInteger("attackType", attackType);
        animator.SetTrigger("isAttacking");
    }

    private bool CheckPlayerInVisionCone()
    {
        Vector3 dirToPlayer = (player.position - transform.position).normalized;
        float distance = Vector3.Distance(transform.position, player.position);

        if (distance < detectionRadius)
        {
            float angle = Vector3.Angle(transform.forward, dirToPlayer);
            if (angle < detectionAngle)
            {
                Vector3 rayOrigin = transform.position + Vector3.up * 1.5f;
                if (Physics.Raycast(rayOrigin, dirToPlayer, out RaycastHit hit, detectionRadius))
                {
                    Debug.DrawLine(rayOrigin, hit.point, Color.red);
                    if (hit.collider.CompareTag("Player"))
                        return true;
                }
            }
        }
        return false;
    }
}
