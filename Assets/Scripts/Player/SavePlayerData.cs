using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions.Must;
using Newtonsoft.Json;
using System.Net.WebSockets;


public class SavePlayerData : MonoBehaviour
{
    public Player_controller playerController;
    public MainMenuController mainMenuController;
    public EquipmentControl equipmentControl;
    [SerializeField] private float autoSaveInterval = 60 * 5f; // Auto-save every 5 minutes default
    [SerializeField] private bool useSaveAndLoadSystem = true;
    PlayerSaveData.Data data;

    private void Start()
    {
        if (useSaveAndLoadSystem)
        {
            // Start automatic saving
            InvokeRepeating(nameof(AutoSave), autoSaveInterval, autoSaveInterval);
            data = SaveAndLoad.Load<PlayerSaveData.Data>(SaveAndLoad.savePathPlayerState);
            ItemsManager.Initialize(() =>
            {
                var itemData = ItemsManager.ConvertDataDicToInventoryItemsArrayOptimized(data?.items ?? new Dictionary<int, PlayerSaveData.ItemData>());
                mainMenuController?.InitializeSavedItemsInInventory(itemData);
                if (equipmentControl != null)
                {
                    foreach (var item in itemData)
                    {
                        if (!string.IsNullOrEmpty(item.equippedSlotName))
                        {
                            equipmentControl.EquipItem(item, equipmentControl.GetSlot(item.equippedSlotName));
                        }
                    }
                }
            }
            );
            if (data?.position != null && data.position.Length == 3)
                playerController.transform.position = new Vector3(data.position[0], data.position[1], data.position[2]);
        }
    }

    private void Update()
    {
        // Keep manual save with F5 for debugging
        if (Input.GetKeyDown(KeyCode.F5))
        {
            Save();
        }
    }

    private void AutoSave()
    {
        Save();
        Debug.Log("Autosave complete!");
    }

    private PlayerSaveData.Data NewSaveData()
    {
        data = new PlayerSaveData.Data()
        {
            position = new float[] { playerController.transform.position.x, playerController.transform.position.y, playerController.transform.position.z },
            items = new Dictionary<int, PlayerSaveData.ItemData>()
        };

        foreach (var itemInstance in mainMenuController.GetAllInventory())
        {
            foreach (var item in itemInstance.items)
                data.items[item.GetHashCode()] = new PlayerSaveData.ItemData(item);
        }
        return data;
    }

    private void Save()
    {
        if (playerController == null || mainMenuController == null)
        {
            Debug.LogWarning("Cannot save: missing references.");
            return;
        }

        var newData = NewSaveData();
        SaveAndLoad.Save<PlayerSaveData.Data>(SaveAndLoad.savePathPlayerState, newData);
    }

    [System.Serializable]
    public class PlayerSaveData
    {
        [System.Serializable]
        public class Data
        {
            public float[] position;
            public Dictionary<int, ItemData> items = new Dictionary<int, ItemData>();

            [JsonIgnore]
            public Dictionary<int, ItemDataInstance> itemsInstance;

            // Rebuild runtime objects after load
            public void RebuildRuntimeItems()
            {
                itemsInstance = new Dictionary<int, ItemDataInstance>();
                foreach (var kvp in items)
                {
                    InventoryItem def = ItemsManager.GetItemByID(kvp.Value.itemID);
                    if (def != null)
                    {
                        // Create a hidden GameObject
                        GameObject go = new GameObject("Item_" + kvp.Key);
                        go.hideFlags = HideFlags.HideInHierarchy; // invisible in hierarchy

                        // Add the ItemDataInstance component
                        var instance = go.AddComponent<ItemDataInstance>();

                        // Initialize fields
                        instance._definition = def;
                        instance.itemID = def.ItemID;
                        instance.quantity = kvp.Value.quantity;
                        instance.currentDurability = kvp.Value.currentDurability;
                        instance.equippedSlotName = kvp.Value.equippedSlotName ?? string.Empty;
                        instance.isFavorite = kvp.Value.isFavorite;
                        instance.enchantmentLevel = kvp.Value.enchantmentLevel;
                        instance.damageModifier = kvp.Value.damageModifier;

                        // Add to the dictionary
                        itemsInstance[kvp.Key] = instance;
                    }
                    else
                    {
                        Debug.LogWarning($"Cannot find InventoryItem for ID {kvp.Value.itemID}");
                    }
                }
            }
        }

        [System.Serializable]
        public class ItemData
        {
            public int itemID;
            public int quantity;
            public float currentDurability;
            public string equippedSlotName;
            public bool isFavorite;
            public int enchantmentLevel;
            public float damageModifier;

            public ItemData() { }

            public ItemData(ItemDataInstance item)
            {
                itemID = item._definition != null ? item._definition.ItemID : item.itemID;
                quantity = item.quantity;
                currentDurability = item.currentDurability;
                equippedSlotName = item.equippedSlotName;
                isFavorite = item.isFavorite;
                enchantmentLevel = item.enchantmentLevel;
                damageModifier = item.damageModifier;
            }
        }
    }
}
