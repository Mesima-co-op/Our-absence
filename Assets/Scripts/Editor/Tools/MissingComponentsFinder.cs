using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class MissingComponentsFinder : MonoBehaviour
{
    [MenuItem("Tools/Find Missing Scripts in Scene")]
    public static void FindMissingScripts()
    {
        GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
        int count = 0;
        foreach (GameObject go in allObjects)
        {
            Component[] comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null)
                {
                    Debug.LogWarning($"Missing script found on {go.name}", go);
                    count++;
                }
            }
        }
        Debug.Log($"Finished! Found {count} missing scripts.");
    }
}
