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

    private Vector3 velocity;
    private Vector3 moveInput;
    public Vector3 direction;

    [Header("Player Settings")]
    [SerializeField] private float speed = 2.5f;
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

        if (!isLockOn && !attackAnimations.IsAttacking())
            PlayerRotation();

        if (rollAction.triggered)
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
        float currentSpeed = CalculateCurrentSpeed();
        ApplyGravity();

        Vector3 movement = (direction * currentSpeed) + Vector3.up * velocity.y;
        controller.Move(movement * Time.deltaTime);

        // Update movement parameters for animation
        movementAnimations.UpdateMovementParameters(animationManager.animator, moveInput, direction, IsRunning());
    }

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
            return speed * 2f;
        else
            return speed;
    }

    private bool IsRunning()
    {
        return runAction.ReadValue<float>() == 1;
    }

    private void ApplyGravity()
    {
        if (velocity.y > -10)
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