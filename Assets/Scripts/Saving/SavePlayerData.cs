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
    private bool inventoryLoaded;

    private void Start()
    {
        // Item definitions are needed for pickups and scene state even when saving is switched off
        if (!useSaveAndLoadSystem) ItemsManager.Initialize();

        if (useSaveAndLoadSystem)
        {
            // Start automatic saving
            InvokeRepeating(nameof(AutoSave), autoSaveInterval, autoSaveInterval);
            data = SaveAndLoad.Load<PlayerSaveData.Data>(SaveAndLoad.savePathPlayerState);
            // Attributes, level and experience (saves from before stats have none: the defaults stay)
            var stats = playerController.GetComponent<CharacterStats>();
            if (stats != null) stats.Load(data?.stats);
            StartCoroutine(RestoreInventoryWhenReady());
            // Position only applies to the scene it was saved in (saves from before this field are from Game)
            string savedScene = string.IsNullOrEmpty(data?.scene) ? "Game" : data.scene;
            if (data?.position != null && data.position.Length == 3 && savedScene == gameObject.scene.name)
            {
                // The CharacterController would otherwise snap the player back to where the scene placed it
                var controller = playerController.GetComponent<CharacterController>();
                if (controller) controller.enabled = false;
                playerController.transform.position = new Vector3(data.position[0], data.position[1], data.position[2]);
                if (controller) controller.enabled = true;
            }
        }
    }

    private IEnumerator RestoreInventoryWhenReady()
    {
        bool itemsReady = false;
        ItemsManager.Initialize(() => itemsReady = true);
        while (!itemsReady) yield return null;

        // When the definitions were already loaded (any scene after the first) the callback fires at once,
        // before MainMenuController.Start has set up its categories, which would wipe what we add. Wait a frame.
        yield return null;

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
        inventoryLoaded = true;
    }

    private void Update()
    {
        // Keep manual save with F5 for debugging
        if (Input.GetKeyDown(KeyCode.F5))
        {
            GameSave.SaveGame();
        }
    }

    private void AutoSave()
    {
        GameSave.SaveGame();
    }

    private void OnApplicationQuit()
    {
        if (useSaveAndLoadSystem) GameSave.SaveGame();
    }

    private PlayerSaveData.Data NewSaveData()
    {
        data = new PlayerSaveData.Data()
        {
            position = new float[] { playerController.transform.position.x, playerController.transform.position.y, playerController.transform.position.z },
            scene = gameObject.scene.name,
            items = new Dictionary<int, PlayerSaveData.ItemData>(),
        };
        var stats = playerController.GetComponent<CharacterStats>();
        if (stats != null) data.stats = stats.ToSave();

        foreach (var itemInstance in mainMenuController.GetAllInventory())
        {
            foreach (var item in itemInstance.items)
                data.items[item.GetHashCode()] = new PlayerSaveData.ItemData(item);
        }
        return data;
    }

    /// <summary>Writes inventory and position. Call <see cref="GameSave.SaveGame"/> to save everything.</summary>
    public bool Save()
    {
        if (!useSaveAndLoadSystem) return false;
        if (playerController == null || mainMenuController == null)
        {
            Debug.LogWarning("Cannot save: missing references.");
            return false;
        }
        // Before the saved inventory is restored, saving would overwrite it with an empty one
        if (!inventoryLoaded) return false;
        // Dying reloads the area; don't remember the spot where the player fell
        var health = playerController.GetComponent<HealthSystem>();
        if (health != null && health.IsDead) return false;

        var newData = NewSaveData();
        SaveAndLoad.Save<PlayerSaveData.Data>(SaveAndLoad.savePathPlayerState, newData);
        return true;
    }

    [System.Serializable]
    public class PlayerSaveData
    {
        [System.Serializable]
        public class Data
        {
            public float[] position;
            public string scene;
            public Dictionary<int, ItemData> items = new Dictionary<int, ItemData>();
            public CharacterStats.SaveData stats;

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
