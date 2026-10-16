using UnityEngine;

/// <summary>
/// Represents an item in the inventory system.
/// Note: This class is a ScriptableObject, allowing for easy creation and management of item assets in Unity. 
/// Do not modiffy in runtime
/// </summary>
[System.Serializable]
[CreateAssetMenu(fileName = "New Item", menuName = "Item/create new Item")]
public class InventoryItem : ScriptableObject
{

    [SerializeField] private string itemName;
    [SerializeField] private MainMenuController.ItemType itemSubType;
    [SerializeField] private MainMenuController.CategoryType itemCategory;
    [SerializeField] private int itemID;
    [SerializeField] private Texture2D icon;
    [SerializeField] private GameObject objectRef;
    [SerializeField] private bool isStackable;
    [SerializeField] private int maxStackSize;
    [SerializeField] private float maxDurability; // For items that can degrade
    [SerializeField] private bool isDroppable;

    public override string ToString()
    {
        return $"{itemName} ({itemSubType})";
    }

    public override bool Equals(object obj)
    {
        return obj is InventoryItem other && itemID == other.itemID;
    }

    public override int GetHashCode()
    {
        return itemID.GetHashCode();
    }

    public string ItemName => itemName;
    public MainMenuController.ItemType ItemSubType => itemSubType;
    public MainMenuController.CategoryType ItemCategory => itemCategory;
    public int ItemID => itemID;
    public Texture2D Icon => icon;
    public GameObject ObjectRef => objectRef;
    public bool IsStackable => isStackable;
    public int MaxStackSize => maxStackSize;
    public float MaxDurability => maxDurability;
    public bool IsDroppable => isDroppable;

}
