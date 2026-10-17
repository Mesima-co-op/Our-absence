using System;
using System.Linq;
using System.Net.WebSockets;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.AI;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(AnimationManager))]
public class Player_controller : MonoBehaviour
{
    public Action OnHealthChange;
    public int CurrentHealth => healthSystem != null ? healthSystem.Health : 0;
    public int MaxHealth => healthSystem != null ? healthSystem.MaxHealth : 0;
    private HealthSystem healthSystem;

    private CharacterController controller;
    private PlayerInput playerInput;
    private Camera mainCamera;
    private AnimationManager animationManager;
    private MovementAnimationController movementAnimations;
    private AttackAnimationController attackAnimations;
    private EquipmentControl equipmentControl;
    private Rig playerRig;
    private LocomotionSync locomotionSync;
    private StaminaSystem stamina;
    private bool sprinting;
    private Vector3 planarVelocity;

    private Vector3 velocity;
    private Vector3 moveInput;
    public Vector3 direction;

    [Header("Player Settings")]
    [Tooltip("Walking speed (m/s)")]
    [SerializeField] private float speed = 2.5f;
    [Tooltip("Running speed while holding Run (m/s). 0 = twice the walking speed.")]
    [SerializeField] private float runSpeed = 0f;
    [Tooltip("How fast the player speeds up and slows down (m/s²). Higher = snappier.")]
    [SerializeField] private float acceleration = 14f;
    [SerializeField] private float gravity = 9.81f;
    [SerializeField] private float rotateSpeed = 3f;
    [SerializeField] private float pushForce = 1f;
    [SerializeField] private float rangeOfItemDetection = 2.0f;
    public bool isPickingUp = false;

    [Header("Target Settings")]

    private InputAction moveAction;
    private InputAction rollAction;
    private InputAction runAction;

    public bool isLockOn = false;

    private ObjectsState GlobalVariables;

    private void Awake()
    {
        InitializeComponents();
        InitializeInput();
    }

    private void InitializeComponents()
    {
        controller = GetComponent<CharacterController>();
        playerInput = GetComponent<PlayerInput>();
        animationManager = GetComponent<AnimationManager>();
        playerRig = GetComponentInChildren<Rig>();
        mainCamera = Camera.main;
        equipmentControl = FindObjectOfType<EquipmentControl>();
        healthSystem = GetComponent<HealthSystem>();
        locomotionSync = GetComponent<LocomotionSync>();
        stamina = GetComponent<StaminaSystem>();
        if (healthSystem != null) healthSystem.OnHealthChanged += (_, _) => OnHealthChange?.Invoke();

        // Initialize movement animation controller
        movementAnimations = new MovementAnimationController(animationManager);
        attackAnimations = new AttackAnimationController(animationManager);
    }

    private void InitializeInput()
    {
        moveAction = playerInput.actions["Move"];
        rollAction = playerInput.actions["Roll"];
        runAction = playerInput.actions["Run"];
    }

    void Start()
    {
        GlobalVariables = FindObjectOfType<ObjectsState>();
    }

    private void Update()
    {
        GetInput();

        if (isPickingUp == false && equipmentControl != null) equipmentControl.PickUpItem(this.gameObject, rangeOfItemDetection);

        if (!attackAnimations.IsAttacking())
        {
            PlayerMovement();
        }
        else if (locomotionSync != null)
        {
            // Stand still while attacking; the controller's velocity must drop to zero too,
            // or the leg animation keeps running mid-swing
            planarVelocity = Vector3.zero;
            ApplyGravity();
            controller.Move(Vector3.up * velocity.y * Time.deltaTime);
        }

        bool rolling = locomotionSync != null && locomotionSync.InRootMotionState;
        if (!isLockOn && !attackAnimations.IsAttacking() && !rolling)
            PlayerRotation();

        // A roll costs stamina (none left: no roll), and can't be chained while one is playing
        if (rollAction.triggered && controller.isGrounded && !rolling &&
            (stamina == null || stamina.TrySpend(stamina.RollCost)))
            animationManager.HandleRollAnimation(direction, controller.isGrounded);

        if (isPickingUp || playerRig.weight > 0.0f)
            PickUpItemAnimation();
    }

    public void ChangeHealth(float changeAmount)
    {
        if (healthSystem != null) healthSystem.ChangeHealth(changeAmount);
    }

    private void GetInput()
    {
        HandleShortcuts();
        ProcessMovementInput();
    }

    private void ProcessMovementInput()
    {
        Vector2 input = moveAction.ReadValue<Vector2>();
        moveInput = new Vector3(input.x, 0, input.y);

        Vector3 forward = mainCamera.transform.forward;
        Vector3 right = mainCamera.transform.right;
        forward.y = 0;
        right.y = 0;
        forward.Normalize();
        right.Normalize();

        direction = (forward * input.y + right * input.x).normalized;
    }

    private void PlayerMovement()
    {
        ApplyGravity();

        // Sprinting drains stamina; when it runs dry the player drops to a walk until it recovers
        bool wantsToRun = runAction.ReadValue<float>() == 1 && direction.sqrMagnitude > 0.01f;
        sprinting = wantsToRun && (stamina == null || stamina.Sprint());

        if (locomotionSync != null)
        {
            locomotionSync.WalkSpeed = speed;
            locomotionSync.RunSpeed = RunSpeed;

            // Rolls and dodges are moved by their animation (LocomotionSync.OnAnimatorMove)
            Vector3 target = locomotionSync.InRootMotionState ? Vector3.zero : direction * CalculateCurrentSpeed();
            planarVelocity = Vector3.MoveTowards(planarVelocity, target, acceleration * Time.deltaTime);
            controller.Move((planarVelocity + Vector3.up * velocity.y) * Time.deltaTime);
            // Animator parameters come from the real velocity in LocomotionSync
            return;
        }

        float currentSpeed = CalculateCurrentSpeed();
        Vector3 movement = (direction * currentSpeed) + Vector3.up * velocity.y;
        controller.Move(movement * Time.deltaTime);

        // Update movement parameters for animation
        movementAnimations.UpdateMovementParameters(animationManager.animator, moveInput, direction, IsRunning());
    }

    private float RunSpeed => runSpeed > 0f ? runSpeed : speed * 2f;

    public void PickUpItemAnimation(float interval = 3.0f)
    {
        if (playerRig.weight < 1.0f && isPickingUp)
        {
            playerRig.weight += Time.deltaTime * interval;
        }
        else
        {
            playerRig.weight -= Time.deltaTime * interval;
            isPickingUp = false;
        }

    }

    private float CalculateCurrentSpeed()
    {
        bool isRunning = IsRunning();
        bool isRolling = animationManager.currentAnimation == "Roll";

        if (isRolling)
            return speed * 0.7f;
        else if (isRunning)
            return RunSpeed;
        else
            return speed;
    }

    private bool IsRunning()
    {
        return sprinting;
    }

    private void ApplyGravity()
    {
        // Keep a small downward push on the ground so isGrounded stays reliable
        if (controller.isGrounded && velocity.y < -2f)
            velocity.y = -2f;
        else if (velocity.y > -10)
            velocity.y -= Time.deltaTime * gravity;
    }

    private void PlayerRotation()
    {
        if (direction.magnitude == 0) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * rotateSpeed);
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody rb = hit.collider.attachedRigidbody;
        if (rb != null && !rb.isKinematic)
        {
            Vector3 pushDir = new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z);
            rb.AddForce(pushDir * pushForce, ForceMode.Impulse);
        }

    }

    private void HandleShortcuts()
    {
        if (Keyboard.current.iKey.wasPressedThisFrame ||
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            GlobalVariables.menuOpen = !GlobalVariables.menuOpen;
            GlobalVariables.mainMenuUI.rootVisualElement.style.display =
                GlobalVariables.menuOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}