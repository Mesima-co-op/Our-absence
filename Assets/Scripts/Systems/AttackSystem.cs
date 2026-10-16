using System.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(PlayerInput))]
public class AttackSystem : MonoBehaviour
{
    private PlayerInput playerInput;
    private Animator animator;
    private InputAction playerLeftAttack;
    private InputAction playerRightAttack;
    private AnimationManager animationManager;
    private ObjectsState GlobalVariables;
    private bool menuOpen;

    void Start()
    {
        playerInput = GetComponent<PlayerInput>();
        GlobalVariables = GameObject.Find("GlobalVars").GetComponent<ObjectsState>();

        playerLeftAttack = playerInput.actions["Fire"];
        playerRightAttack = playerInput.actions["SecFire"];

        animator = GetComponent<Animator>();

        animationManager = GetComponent<AnimationManager>();
    }

    void Update()
    {
        menuOpen = (bool)GlobalVariables.GetType().GetField("menuOpen").GetValue(GlobalVariables);
        if (animationManager.currentAnimation == "Roll"
        || animationManager.currentAnimation == "Standing Dodge Backward") return;

        if (menuOpen) return;

        if (playerLeftAttack.triggered)
        {
            LeftAttack();
        }
        if (playerRightAttack.triggered)
        {
            RightAttack();
        }
    }

    private void LeftAttack()
    {
        if (animationManager.GetComponent<Animator>().GetLayerWeight(1) == 1)
        {
            animationManager.TriggerAnimation("Boxing left lock on");
        }
        else
        {
            animationManager.TriggerAnimation("Boxing left");

        }
    }

    private void RightAttack()
    {
        if (animationManager.GetComponent<Animator>().GetLayerWeight(1) == 1)
        {
            animationManager.TriggerAnimation("Boxing right lock on");
        }
        else
        {
            animationManager.TriggerAnimation("Boxing right");
        }
    }


}
