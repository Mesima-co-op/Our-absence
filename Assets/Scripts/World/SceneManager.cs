using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SceneManager : MonoBehaviour
{
    private Dictionary<int, PickableItem> pickableItemsInScene;
    private GameObject[] allObjects;
    [SerializeField] private float autoSaveInterval = 60 * 5f; // Auto-save every 5 minutes default
    [SerializeField] private bool useSaveAndLoadSystem = true;

    void Start()
    {
        if (useSaveAndLoadSystem)
        {
            allObjects = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(obj => obj.CompareTag("PickableItem") && obj.scene.IsValid())
            .ToArray();

            pickableItemsInScene = new Dictionary<int, PickableItem>();
            StartCoroutine(WaitForItemsManagerAndLoad());
            InvokeRepeating(nameof(AutoSave), autoSaveInterval, autoSaveInterval);
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

    private void Save()
    {
        allObjects = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(obj => obj.CompareTag("PickableItem") && obj.scene.IsValid())
            .ToArray();
        pickableItemsInScene.Clear();
        int i = 0;
        foreach (GameObject obj in allObjects)
        {
            pickableItemsInScene[i++] = new PickableItem(obj);
        }
        SaveAndLoad.Save<Dictionary<int, PickableItem>>(SaveAndLoad.savePathSceneState, pickableItemsInScene);
    }

    private IEnumerator WaitForItemsManagerAndLoad()
    {
        // Wait until ItemsManager finishes loading
        while (!ItemsManager.isLoaded)
        {
            yield return null; // wait one frame
        }

        Load(); // now safe to load
    }

    private void Load()
    {
        pickableItemsInScene = SaveAndLoad.Load<Dictionary<int, PickableItem>>(SaveAndLoad.savePathSceneState);

        if (pickableItemsInScene == null)
        {
            Debug.LogWarning("pickableItemsInScene is NULL after LoadScenePickableItems!");
            pickableItemsInScene = new Dictionary<int, PickableItem>();
            return;
        }
        int i = (pickableItemsInScene.Count() > 0 ? pickableItemsInScene.Last().Key : 0) + 1;
        foreach (var obj in allObjects)
        {
            pickableItemsInScene[i++] = new PickableItem(obj);
            Destroy(obj);
        }

        foreach (var obj in pickableItemsInScene.Values)
        {
            if (obj.itemID == -1)
            {
                Debug.LogWarning("Found invalid PickableItem (itemID == -1) in pickableItemsInScene!");
                continue;
            }

            if (obj.isExistInScene == true)
            {
                var itemDef = ItemsManager.GetItemByID(obj.itemID);
                if (itemDef == null)
                {
                    Debug.LogWarning($"No InventoryItem found for itemID {obj.itemID}");
                    continue;
                }

                if (obj.position == null || obj.position.Length < 3)
                {
                    Debug.LogError($"Invalid position array for itemID {obj.itemID}");
                    continue;
                }

                Debug.Log($"Spawning item {obj.itemID} at {obj.position[0]}, {obj.position[1]}, {obj.position[2]}");

                Instantiate(
                    itemDef.ObjectRef,
                    new Vector3(obj.position[0], obj.position[1], obj.position[2]),
                    Quaternion.identity
                );
            }
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
            var itemComponent = obj.GetComponent<PickUpItem>();
            itemID = itemComponent != null ? itemComponent.Item._definition.ItemID : -1;
        }
    }

    struct Interactable
    {

    }
}
