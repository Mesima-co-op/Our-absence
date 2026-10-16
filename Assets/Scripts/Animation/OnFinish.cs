using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;

public class OnFinish : StateMachineBehaviour
{
    [SerializeField] private string triggerName = "";
    [SerializeField] private bool isTriggerBoolField = true;
    [SerializeField] private bool setBoolTrue = false;

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (isTriggerBoolField)
            animator.SetBool(triggerName, setBoolTrue);
        else
            animator.ResetTrigger(triggerName);
    }

}
