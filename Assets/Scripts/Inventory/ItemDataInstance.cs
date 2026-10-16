using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ItemDataInstance : MonoBehaviour
{
    [Header("Reference")]
    public int itemID; // References the ScriptableObject

    [Header("Runtime Data")]
    public int quantity = 1;
    public float currentDurability;
    public string equippedSlotName = null;
    public bool isFavorite = false;

    [Header("Weapon-Specific (if applicable)")]
    public int enchantmentLevel = 0;
    public float damageModifier = 1f;

    // Cached reference to definition (not serialized)
    public InventoryItem _definition;
    public InventoryItem Definition
    {
        get
        {
            if (_definition == null)
                _definition = ItemsManager.GetItemByID(itemID);
            return _definition;
        }
    }

    // Constructor
    public ItemDataInstance(InventoryItem definition, int quantity = 1)
    {
        this.itemID = definition.ItemID;
        this.quantity = quantity;
        this.currentDurability = definition.MaxDurability;
        this._definition = definition;
    }

    // Helper methods
    public bool IsBroken => currentDurability <= 0;
    public float DurabilityPercentage => Definition != null ? currentDurability / Definition.MaxDurability : 0;
    public bool CanStack => Definition != null && Definition.IsStackable;
    public int MaxStackSize => Definition?.MaxStackSize ?? 1;

    public void TakeDamage(float damage)
    {
        currentDurability = Mathf.Max(0, currentDurability - damage);
    }

    public void Repair(float amount)
    {
        if (Definition != null)
            currentDurability = Mathf.Min(Definition.MaxDurability, currentDurability + amount);
    }
}
