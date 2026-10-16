using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Saves and restores which pickable items still lie in this scene (and where), per save slot.
/// Saving is driven by <see cref="GameSave.SaveGame"/>.
/// </summary>
public class SceneManager : MonoBehaviour
{
    [SerializeField] private bool useSaveAndLoadSystem = true;

    private bool loaded;

    void Start()
    {
        if (useSaveAndLoadSystem)
            StartCoroutine(WaitForItemsManagerAndLoad());
    }

    private static GameObject[] FindPickablesInLoadedScenes()
    {
        // Includes inactive objects: picked-up items are deactivated, not destroyed
        return Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(obj => obj.scene.IsValid() && obj.CompareTag("PickableItem"))
            .ToArray();
    }

    public void Save()
    {
        // Saving before the saved state was applied would overwrite it with the scene's initial items
        if (!useSaveAndLoadSystem || !loaded) return;

        var state = new Dictionary<int, PickableItem>();
        int i = 0;
        foreach (GameObject obj in FindPickablesInLoadedScenes())
        {
            if (obj.scene != gameObject.scene) continue;
            var item = new PickableItem(obj);
            if (item.itemID != -1) state[i++] = item;
        }
        SaveAndLoad.Save(SaveAndLoad.SceneStatePath(gameObject.scene.name), state);
    }

    private IEnumerator WaitForItemsManagerAndLoad()
    {
        // ItemsManager is initialized by SavePlayerData; wait until the item definitions exist
        while (!ItemsManager.isLoaded)
            yield return null;

        Load();
        loaded = true;
    }

    private void Load()
    {
        var saved = SaveAndLoad.Load<Dictionary<int, PickableItem>>(SaveAndLoad.SceneStatePath(gameObject.scene.name));
        if (saved == null) return; // first visit: keep the items placed in the scene

        // The save is the truth: remove the scene's authored pickups and respawn exactly what was saved
        foreach (var obj in FindPickablesInLoadedScenes())
            if (obj.scene == gameObject.scene) Destroy(obj);

        foreach (var entry in saved.Values)
        {
            if (!entry.isExistInScene || entry.itemID == -1) continue;

            var itemDef = ItemsManager.GetItemByID(entry.itemID);
            if (itemDef == null || itemDef.ObjectRef == null)
            {
                Debug.LogWarning($"[Save] No item definition for itemID {entry.itemID}");
                continue;
            }
            if (entry.position == null || entry.position.Length < 3)
            {
                Debug.LogWarning($"[Save] Invalid position for itemID {entry.itemID}");
                continue;
            }

            Instantiate(itemDef.ObjectRef, new Vector3(entry.position[0], entry.position[1], entry.position[2]), Quaternion.identity);
        }
    }

    public struct PickableItem
    {
        public bool isExistInScene;
        public float[] position;
        public int itemID;

        public PickableItem(GameObject obj)
        {
            isExistInScene = obj.activeSelf;
            position = new float[] { obj.transform.position.x, obj.transform.position.y, obj.transform.position.z };
            var pickUp = obj.GetComponent<PickUpItem>(); // no ?. here: Unity's fake-null objects bypass it
            var item = pickUp != null ? pickUp.Item : null;
            itemID = item == null ? -1 : (item.Definition != null ? item.Definition.ItemID : item.itemID);
        }
    }
}
