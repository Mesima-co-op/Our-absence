using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Our Absence > Characters > Use Player_Base In Open Scene
///
/// Replaces the scene's "Player" (e.g. the Paladin) with an instance of Player_Base.prefab at the same
/// spot, and re-points everything in the scene that referenced the old player (enemies, camera,
/// HUD, globals) to the new one. The camera's follow height is scaled to the new body height.
/// Undo-able (Ctrl+Z); save the scene afterwards.
/// </summary>
public static class PlayerSwapTool
{
    static readonly HashSet<string> InternalFields = new HashSet<string>
    {
        "m_GameObject", "m_Script", "m_PrefabInstance", "m_PrefabAsset", "m_CorrespondingSourceObject",
    };

    [MenuItem("Our Absence/Characters/Use Player_Base In Open Scene")]
    public static void SwapInActiveScene()
    {
        SwapPlayer(EditorSceneManager.GetActiveScene());
    }

    public static GameObject SwapPlayer(Scene scene)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCharacterBuilder.PrefabPath);
        if (prefab == null)
        {
            Debug.LogError("[Character] Build the player first: Our Absence > Characters > Build Player Base Character.");
            return null;
        }
        var old = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Player");
        if (old == null)
        {
            Debug.LogError($"[Character] No root object named 'Player' in {scene.name}.");
            return null;
        }
        if (PrefabUtility.GetCorrespondingObjectFromSource(old) == prefab)
        {
            Debug.Log("[Character] The scene already uses Player_Base.");
            return old;
        }

        var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        Undo.RegisterCreatedObjectUndo(player, "Use Player_Base");
        player.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
        player.transform.SetSiblingIndex(old.transform.GetSiblingIndex());

        // Old object → new object
        var map = new Dictionary<Object, Object> { [old] = player, [old.transform] = player.transform };
        foreach (var c in old.GetComponents<Component>())
        {
            var match = player.GetComponent(c.GetType());
            if (match != null) map[c] = match;
        }
        var oldAnimator = old.GetComponent<Animator>();
        var newAnimator = player.GetComponent<Animator>();
        if (oldAnimator != null && newAnimator != null && oldAnimator.isHuman)
            for (int b = 0; b < (int)HumanBodyBones.LastBone; b++)
            {
                var from = oldAnimator.GetBoneTransform((HumanBodyBones)b);
                var to = newAnimator.GetBoneTransform((HumanBodyBones)b);
                if (from == null || to == null) continue;
                map[from] = to;
                map[from.gameObject] = to.gameObject;
            }

        float heightRatio = Height(player) / Mathf.Max(Height(old), 0.01f);
        var oldParts = new HashSet<Object>(old.GetComponentsInChildren<Component>(true));
        oldParts.UnionWith(old.GetComponentsInChildren<Transform>(true).Select(t => (Object)t.gameObject));

        // The old player's own links to scene objects (camera follow, lock-on canvas, crosshair, ...)
        int carried = 0;
        foreach (var oldComponent in old.GetComponents<Component>())
        {
            if (oldComponent == null || !map.TryGetValue(oldComponent, out var newComponent)) continue;
            var from = new SerializedObject(oldComponent);
            var to = new SerializedObject(newComponent);
            var it = from.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                if (InternalFields.Contains(it.propertyPath)) continue; // prefab bookkeeping, not settings
                var value = it.objectReferenceValue;
                if (EditorUtility.IsPersistent(value) || oldParts.Contains(value)) continue; // assets / its own parts
                var target = to.FindProperty(it.propertyPath);
                if (target == null || target.propertyType != SerializedPropertyType.ObjectReference) continue;
                target.objectReferenceValue = value;
                carried++;
            }
            to.ApplyModifiedPropertiesWithoutUndo();
        }

        int repointed = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == old || root == player) continue;
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var so = new SerializedObject(component);
                var it = so.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    if (!oldParts.Contains(it.objectReferenceValue)) continue;
                    it.objectReferenceValue = map.TryGetValue(it.objectReferenceValue, out var to) ? to : null;
                    changed = true;
                    repointed++;
                }
                if (component is CameraFollow && changed)
                {
                    var offset = so.FindProperty("offset");
                    offset.vector3Value = Vector3.Scale(offset.vector3Value, new Vector3(1f, heightRatio, 1f));
                }
                if (changed)
                {
                    Undo.RecordObject(component, "Use Player_Base");
                    so.ApplyModifiedProperties();
                }
            }
        }

        string oldName = old.name;
        Undo.DestroyObjectImmediate(old);
        player.name = oldName;
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Character] {scene.name}: Player is now Player_Base ({repointed} references re-pointed, {carried} scene links carried over, camera height ×{heightRatio:F2}).");
        return player;
    }

    static float Height(GameObject go)
    {
        var cc = go.GetComponent<CharacterController>();
        return cc != null ? cc.height * go.transform.lossyScale.y : 1.8f;
    }
}
