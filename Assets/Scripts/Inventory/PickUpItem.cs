using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.InputSystem;

public class PickUpItem : MonoBehaviour
{
    public ItemDataInstance Item;
    public float detectionRadius = 3.0f;
    [SerializeField] private PlayerInput playerInput;

    void Awake()
    {
        Item = GetComponent<ItemDataInstance>();
        playerInput = FindAnyObjectByType<PlayerInput>();
    }

    public ItemDataInstance IsPickUpItemInRange()
    {
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, detectionRadius);
        foreach (var hitCollider in hitColliders)
        {
            if (hitCollider.CompareTag("Player") && playerInput.actions["Interact"].triggered)
            {
                return Item;
            }
        }
        return null;
    }

}
