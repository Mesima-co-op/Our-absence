using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Our Absence > Player > Set Up Stats And Stamina: adds CharacterStats and StaminaSystem to Player_Base and
/// gives each weapon moveset its stamina cost per attack. Only that field is written, so hand-tuned movesets
/// keep everything else. Safe to re-run.
/// </summary>
public static class StatsSetupTool
{
    const string MovesetFolder = "Assets/Data/Combat";

    [MenuItem("Our Absence/Player/Set Up Stats And Stamina")]
    public static void SetUp()
    {
        SetUpPlayer(PlayerCharacterBuilder.PrefabPath);

        var sets = AssetDatabase.FindAssets("t:WeaponMoveset", new[] { MovesetFolder })
            .Select(g => AssetDatabase.LoadAssetAtPath<WeaponMoveset>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(m => m != null)
            .ToDictionary(m => m.name.Replace("Moveset_", ""), m => m);
        WeaponsAndMovesetsBuilder.ApplyStaminaCosts(sets);
        AssetDatabase.SaveAssets();
        Debug.Log("[Stats] Player_Base has CharacterStats and StaminaSystem; moveset stamina costs set.");
    }

    public static void SetUpPlayer(string prefabPath)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            if (root.GetComponent<CharacterStats>() == null) root.AddComponent<CharacterStats>();
            if (root.GetComponent<StaminaSystem>() == null) root.AddComponent<StaminaSystem>();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
