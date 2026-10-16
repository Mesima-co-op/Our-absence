using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public static class ItemsManager
{

    public static List<InventoryItem> allItems = new List<InventoryItem>();
    public static Dictionary<int, InventoryItem> itemsById = new Dictionary<int, InventoryItem>();
    public static bool isLoaded = false;

    public static void Initialize(System.Action onComplete = null)
    {
        if (isLoaded)
        {
            onComplete?.Invoke();
            return;
        }

        allItems.Clear();
        itemsById.Clear();

        Addressables.LoadAssetsAsync<InventoryItem>("InventoryItems", item =>
        {
            allItems.Add(item);
        }).Completed += handle =>
        {
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                foreach (var item in allItems)
                {
                    if (!itemsById.ContainsKey(item.ItemID))
                        itemsById.Add(item.ItemID, item);
                    else
                        Debug.LogWarning($"Duplicate ID detected: {item.ItemID} ({item.name})");
                }

                isLoaded = true;
                Debug.Log($"All items loaded! Count: {allItems.Count}");
                onComplete?.Invoke();
            }
            else
            {
                Debug.LogError("Failed to load items via Addressables!");
            }
        };
    }

    public static List<ItemDataInstance> FilterItemsByType(MainMenuController.CategoryType type, Dictionary<int, ItemDataInstance> items = null)
    {
        List<ItemDataInstance> filteredItems = new List<ItemDataInstance>();
        foreach (var (myItemKey, myItemValue) in items)
        {
            foreach (var item in allItems)
            {
                Debug.Log($"Comparing item ID {myItemValue.itemID} with {item.ItemID} of type {item.ItemCategory}");
                if (item.ItemCategory == type && myItemValue.itemID == item.ItemID)
                {
                    // Create a hidden GameObject for the ItemDataInstance
                    GameObject go = new GameObject("Item_" + item.ItemID);
                    go.hideFlags = HideFlags.HideInHierarchy; // invisible in the hierarchy

                    // Attach the MonoBehaviour
                    var instance = go.AddComponent<ItemDataInstance>();

                    // Initialize fields
                    instance._definition = item;
                    instance.itemID = item.ItemID;
                    instance.quantity = myItemValue.quantity;
                    instance.currentDurability = myItemValue.currentDurability;
                    instance.equippedSlotName = myItemValue.equippedSlotName;
                    instance.isFavorite = myItemValue.isFavorite;
                    instance.enchantmentLevel = myItemValue.enchantmentLevel;
                    instance.damageModifier = myItemValue.damageModifier;

                    // Add to your list
                    filteredItems.Add(instance);
                }
            }
        }
        return filteredItems;
    }
    public static List<ItemDataInstance> ConvertDataDicToInventoryItemsArrayOptimized(Dictionary<int, SavePlayerData.PlayerSaveData.ItemData> items = null)
    {
        List<ItemDataInstance> filteredItems = new List<ItemDataInstance>();

        // Null checks
        if (items == null || allItems == null)
        {
            Debug.LogError("ConvertDataDicToInventoryItemsArray: items or allItems is null");
            return filteredItems;
        }

        // Create a lookup dictionary for better performance
        var itemLookup = allItems.Where(item => item != null)
                                 .ToDictionary(item => item.ItemID, item => item);

        foreach (var kvp in items)
        {
            if (kvp.Value == null)
            {
                Debug.LogWarning($"Null ItemDataInstance found at key {kvp.Key}");
                continue;
            }

            if (itemLookup.TryGetValue(kvp.Value.itemID, out InventoryItem matchingItem))
            {
                try
                {
                    // Create a hidden GameObject for the item
                    GameObject go = new GameObject("Item_" + kvp.Key);
                    go.hideFlags = HideFlags.HideInHierarchy; // invisible in editor

                    // Add the ItemDataInstance component
                    var newItem = go.AddComponent<ItemDataInstance>();

                    // Initialize fields
                    newItem._definition = matchingItem;
                    newItem.itemID = matchingItem.ItemID;
                    newItem.quantity = kvp.Value.quantity;
                    newItem.currentDurability = kvp.Value.currentDurability;
                    newItem.equippedSlotName = kvp.Value.equippedSlotName ?? string.Empty;
                    newItem.isFavorite = kvp.Value.isFavorite;
                    newItem.enchantmentLevel = kvp.Value.enchantmentLevel;
                    newItem.damageModifier = kvp.Value.damageModifier;

                    filteredItems.Add(newItem);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error creating ItemDataInstance for itemID {kvp.Value.itemID}: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"No matching item found for itemID {kvp.Value.itemID}");
            }
        }

        return filteredItems;
    }

    internal static InventoryItem GetItemByID(int ID)
    {
        foreach (var item in allItems)
        {
            if (item.ItemID == ID)
            {
                return item;
            }
        }
        return null;
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/Items/Check for Duplicate IDs")]
    public static void CheckForDuplicateIDs()
    {
        if (allItems == null || allItems.Count == 0)
        {
            Debug.LogWarning("No items found in Resources!");
            return;
        }

        Dictionary<int, List<InventoryItem>> idMap = new Dictionary<int, List<InventoryItem>>();
        foreach (var item in allItems)
        {
            if (item == null) continue;

            if (!idMap.ContainsKey(item.ItemID))
                idMap[item.ItemID] = new List<InventoryItem>();

            idMap[item.ItemID].Add(item);
        }

        // Check duplicates
        bool hasDuplicates = false;
        foreach (var kvp in idMap)
        {
            if (kvp.Value.Count > 1)
            {
                hasDuplicates = true;
                Debug.LogError($"Duplicate ID {kvp.Key} found in:");
                foreach (var item in kvp.Value)
                {
                    Debug.LogError($"   {item.name}", item);
                }
            }
        }

        if (!hasDuplicates)
            Debug.Log("✅ No duplicate Item IDs found!");
    }
#endif
}
