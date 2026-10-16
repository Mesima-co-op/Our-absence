using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// EnemyLockOn is a Unity MonoBehaviour script that enables a player character to lock onto nearby enemies.
/// It scans for enemies within a specified radius and angle, checks for line-of-sight, and manages lock-on state.
/// When locked on, it updates the camera, crosshair UI, and player orientation to face the target.
/// The script also handles unlocking when the target is out of range or blocked, and provides visual debugging with gizmos.
/// </summary>
public class EnemyLockOn : MonoBehaviour
{

    [Header("References")]
    Animator animator;
    [Tooltip("Layer Mask for Target Detection")]
    [SerializeField] LayerMask targetLayers;
    [SerializeField] Canvas enemyCanvas;
    [SerializeField] RawImage crossHair;
    [Tooltip("A Script that make the camera follow a given target")]
    [SerializeField] CameraFollow camFollow;
    [Tooltip("UI Canvas for Lock-On Target Display")]
    Player_controller player_script;
    PlayerInput playerInput;
    [SerializeField] GameObject[] targetsInRange;
    Transform cam;

    [Header("Notice Settings")]
    [SerializeField] float noticeZone = 10;
    [SerializeField] float lookAtSmoothing = 2;
    [Tooltip("Angle Degree")]
    [SerializeField] float maxNoticeAngle = 60;

    [Header("crossHair Settings")]
    [SerializeField] Color crossHair_Color = Color.red;
    [SerializeField] float canvas_Scale = 0.1f;
    public Vector3 lockOnOffset = Vector3.zero;

    bool locked = false;

    void Start()
    {
        player_script = GetComponent<Player_controller>();
        cam = Camera.main.transform;
        playerInput = GetComponent<PlayerInput>();
        animator = GetComponent<Animator>();

        targetsInRange = new GameObject[] { };
    }

    void Update()
    {
        camFollow.lockedTarget = locked;
        player_script.isLockOn = locked;
        if (playerInput.actions["LockOn"].triggered)
        {
            FindTargets();
            if (targetsInRange.Length == 0)
            {
                animator.SetLayerWeight(1, weight: 0.0f); // Ensure Target_layer is inactive when not locked on
                return;
            }
            else
            {
                MakeEnemyUIVisible(targetsInRange[0]);
                locked = !locked;
            }
        }
        if (locked)
        {
            animator.SetLayerWeight(1, weight: 1.0f); // Ensure Target_layer is active when locked on
            LockOnCamera();
        }
        else
        {
            animator.SetLayerWeight(1, weight: 0.0f); // Ensure Target_layer is inactive when not locked on
            camFollow.Unlock();
            camFollow.SetAdditionalOffset(Vector3.zero);
            if (enemyCanvas != null)
            {
                enemyCanvas.enabled = false;
            }
        }

    }

    private void FindTargets()
    {
        targetsInRange = new GameObject[] { };

        // Get all CharacterControllers in the scene on the target layers
        CharacterController[] allCharControllers = FindObjectsByType<CharacterController>(FindObjectsSortMode.None);

        foreach (CharacterController cc in allCharControllers)
        {
            // Check if the object is on one of the target layers
            if ((targetLayers.value & (1 << cc.gameObject.layer)) == 0) continue;

            float distToTarget = Vector3.Distance(transform.position, cc.transform.position);

            // Check within notice zone radius
            if (distToTarget > noticeZone) continue;

            Vector3 dirToTarget = (cc.transform.position - transform.position).normalized;
            float angleToTarget = Vector3.Angle(transform.forward, dirToTarget);

            if (angleToTarget < maxNoticeAngle / 2)
            {
                if (cc.CompareTag("Enemy"))
                {
                    targetsInRange = targetsInRange.Append(cc.gameObject).ToArray();
                }
            }
        }
    }

    private void MakeEnemyUIVisible(GameObject enemyGameObject)
    {
        enemyCanvas = enemyGameObject.GetComponentInChildren<Canvas>();
        crossHair = enemyGameObject.GetComponentInChildren<RawImage>();
        enemyCanvas.enabled = true;
        crossHair.color = crossHair_Color;
    }

    private void LockOnCamera()
    {
        if (targetsInRange.Length == 0)
        {
            camFollow.Unlock();
            camFollow.SetAdditionalOffset(Vector3.zero);
            locked = false;
            return;
        }

        Transform target = targetsInRange[0].transform; // Todo make toggle between nearby enemies for now take only the first one
        Vector3 targetPosition = target.position + lockOnOffset;
        Vector3 direction = (targetPosition - transform.position).normalized;
        Quaternion lookRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * lookAtSmoothing);
        camFollow.LockOn(target);
        camFollow.SetAdditionalOffset(lockOnOffset);

        // Update enemy canvas scale relative to camera distance
        float distanceToCamera = Vector3.Distance(transform.position, Camera.main.transform.position);

        // Use inverse so closer = smaller scale factor, farther = larger factor
        float scale = distanceToCamera * canvas_Scale;

        // Apply scale uniformly
        enemyCanvas.transform.localScale = Vector3.one * scale;

        // Check if target is out of range or blocked
        float distanceToTarget = Vector3.Distance(transform.position, target.position);
        if (distanceToTarget > noticeZone)
        {
            camFollow.Unlock();
            camFollow.SetAdditionalOffset(Vector3.zero);
            locked = false;
            return;
        }

        if (target.tag != "Enemy")
        {
            camFollow.Unlock();
            camFollow.SetAdditionalOffset(Vector3.zero);
            locked = false;
            return;
        }
    }

}
