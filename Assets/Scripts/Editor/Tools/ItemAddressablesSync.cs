using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// ItemsManager loads item definitions through the Addressables label "InventoryItems".
/// An InventoryItem missing from that label can be picked up but never saved or restored.
/// This registers every InventoryItem asset under the label.
/// </summary>
public static class ItemAddressablesSync
{
    const string Label = "InventoryItems";

    [MenuItem("Our Absence/Items/Register All Items For Saving (Addressables)")]
    public static void RegisterAll()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[Items] Addressables settings not found.");
            return;
        }

        int added = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:InventoryItem"))
        {
            var entry = settings.FindAssetEntry(guid);
            if (entry != null && entry.labels.Contains(Label)) continue;

            entry ??= settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            entry.SetLabel(Label, true, true);
            added++;
            Debug.Log($"[Items] Registered {AssetDatabase.GUIDToAssetPath(guid)}");
        }

        settings.SetDirty(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.EntryModified, null, true, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Items] {added} item(s) newly registered under '{Label}'.");
    }
}
