using System;
using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using UnityEngine;
using UnityEngine.InputSystem;

// AnimationManager.cs - Main animation management class

public class AnimationManager : MonoBehaviour
{
    [Header("Animation")]
    public Animator animator;
    public string currentAnimation = "Movement";

    private MovementAnimationController movementAnimations;
    private AttackAnimationController attackAnimations;
    private DodgeAnimationController dodgeAnimations;

    public int[] ActiveLayersIndex { get; private set; } = new int[3];

    private void Awake()
    {
        movementAnimations = new MovementAnimationController(this);
        attackAnimations = new AttackAnimationController(this);
        dodgeAnimations = new DodgeAnimationController(this);
    }

    private void Update()
    {
        for (int i = 0; i < animator.layerCount; i++)
        {
            if (animator.GetLayerWeight(i) == 1.0f)
            {
                ActiveLayersIndex[i] = 1;
            }
            else
            {
                ActiveLayersIndex[i] = 0;
            }
        }
        // Will output ActiveLayersIndex [layer: 0 active 1/ not active 0, layer: 1 active 1/ not active 0, ...]
    }

    public void HandleRollAnimation(Vector3 direction, bool isGrounded)
    {
        if (!isGrounded) return;
        dodgeAnimations.HandleRoll(direction);
    }

    public bool IsInSpecialAnimation()
    {
        return dodgeAnimations.IsInDodgeAnimation();
    }

    public void SetFloatParam(string paramName, float value)
    {
        animator.SetFloat(paramName, value);
    }

    public void SetBoolParam(string paramName, bool value)
    {
        animator.SetBool(paramName, value);
    }

    public void TriggerAnimation(string triggerName, bool param = false)
    {
        if (param)
        {
            animator.SetBool(triggerName, true);
            return;
        }
        animator.SetTrigger(triggerName);
    }

    public bool IsTriggered(string triggerName)
    {
        return animator.parameters.Any(p => p.name == triggerName && p.type == AnimatorControllerParameterType.Bool && animator.GetBool(triggerName));
    }

    public void SetLayerWeight(int layerIndex, float weight)
    {
        animator.SetLayerWeight(layerIndex, weight);
    }

    public int GetLayerIndexByName(string layerName)
    {
        for (int i = 0; i < animator.layerCount; i++)
        {
            if (animator.GetLayerName(i) == layerName)
            {
                return i;
            }
        }
        return 0; // Layer not found return default
    }
}

// AttackAnimationController.cs - Updated with better attack state tracking
public class AttackAnimationController
{
    private AnimationManager animationManager;

    // Triggers names
    private const string ATTACK_TRIGGER_LEFT = "isAttackingLeft";
    private const string ATTACK_TRIGGER_RIGHT = "isAttackingRight";

    public AttackAnimationController(AnimationManager manager)
    {
        animationManager = manager;
    }

    public void TriggerLeftWeaponAttack(float type = 0)
    {
        animationManager.TriggerAnimation(ATTACK_TRIGGER_LEFT, true);
        animationManager.SetFloatParam("attackType", type);
    }

    public bool IsAttacking()
    {
        return animationManager.IsTriggered(ATTACK_TRIGGER_LEFT) || animationManager.IsTriggered(ATTACK_TRIGGER_RIGHT);
    }

    public void TriggerRightWeaponAttack(float type = 0)
    {
        animationManager.TriggerAnimation(ATTACK_TRIGGER_RIGHT, true);
        animationManager.SetFloatParam("attackType", type);
    }

    public bool IsLockedOn()
    {
        return animationManager.animator.GetLayerWeight(1) == 1.0f; // Layer index 1 is the targeting layer
    }
}

// MovementAnimationController.cs - No changes needed
public class MovementAnimationController
{
    private AnimationManager animationManager;

    // Animation names
    private const string MOVEMENT = "Movement";

    public MovementAnimationController(AnimationManager manager)
    {
        animationManager = manager;
    }

    public void UpdateMovementParameters(Animator animator, Vector3 moveInput, Vector3 direction, bool isRunning)
    {
        animator.SetFloat("Horizontal", Mathf.Round(moveInput.x));
        animator.SetFloat("Vertical", Mathf.Round(moveInput.z));
        animator.SetFloat("speed", isRunning ? direction.magnitude * 2 : direction.magnitude, 0.1f, Time.deltaTime);
    }
}

// DodgeAnimationController.cs - No changes needed
public class DodgeAnimationController
{
    private AnimationManager animationManager;

    // Animation names
    private const string ROLL = "Roll";
    private const string STANDING_DODGE_BACKWARD = "Standing Dodge Backward";
    private const string LOCKED_ROLL = "Locked Roll";
    private const string LOCKED_STANDING_DODGE_BACKWARD = "Locked Standing Dodge Backward";

    // Triggers names
    private const string ROLL_TRIGGER = "roll";
    private const string STANDING_DODGE_BACKWARD_TRIGGER = "dodgeBackwards";

    public DodgeAnimationController(AnimationManager manager)
    {
        animationManager = manager;
    }

    public void HandleRoll(Vector3 direction)
    {

        if (direction.magnitude > 0.1f)
            animationManager.TriggerAnimation(ROLL_TRIGGER);
        else
            animationManager.TriggerAnimation(STANDING_DODGE_BACKWARD_TRIGGER);
    }

    public bool IsInDodgeAnimation()
    {
        return animationManager.currentAnimation == ROLL ||
               animationManager.currentAnimation == STANDING_DODGE_BACKWARD ||
               animationManager.currentAnimation == LOCKED_ROLL ||
               animationManager.currentAnimation == LOCKED_STANDING_DODGE_BACKWARD;
    }
}