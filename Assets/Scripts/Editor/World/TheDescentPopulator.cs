using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

/// <summary>
/// Makes The Descent playable: copies the player rig, camera, HUD, menus and global systems
/// from Game.unity (keeping their cross-references), bakes NavMesh for the enemy zones,
/// places enemies and a starting weapon, and adds the scene to the build.
/// Runs automatically after "Generate The Descent"; can also be run on its own.
/// </summary>
public static class TheDescentPopulator
{
    const string ScenePath = "Assets/Scenes/World/TheDescent.unity";
    const string SourceScenePath = "Assets/Scenes/Game.unity";
    const string NavMeshFolder = "Assets/Scenes/World/TheDescent";

    // Root objects in Game.unity that make up the playable setup
    static readonly string[] GameplayRoots =
    {
        "Player", "Main Camera", "CameraTaerget", "EnemyTarget_Locator",
        "EventSystem", "MainMenu", "GlobalVars", "GameUI",
    };

    const string EnemyPrefab = "Assets/Objects/Examples/Enemy1patrol.prefab";
    const string StarterWeaponPrefab = "Assets/Objects/Items/Weapons/GameObjects/RustyShortSword.prefab";

    // Enemy encounters: progress along the path and side offset from it
    static readonly (string zone, float t, float side)[] Encounters =
    {
        ("The Withered Wood", 0.22f, 6f),
        ("The Withered Wood", 0.35f, -7f),
        ("Field of the Fallen", 0.52f, 8f),
        ("Field of the Fallen", 0.60f, -6f),
        ("The Narrowing", 0.72f, 3f),
    };

    [MenuItem("Our Absence/World/Populate The Descent (player, UI, enemies)")]
    public static void Populate()
    {
        TheDescentGenerator.EnsurePath();

        var scene = USceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        var terrain = Object.FindObjectsOfType<Terrain>().FirstOrDefault(t => t.gameObject.scene == scene);
        var spawn = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                         .FirstOrDefault(t => t.name == "PlayerSpawn");
        if (terrain == null || spawn == null)
        {
            Debug.LogError("[The Descent] Generate the map first (terrain or PlayerSpawn missing).");
            return;
        }

        RemovePreviousPopulation(scene);
        CopyGameplayRoots(scene);
        PlacePlayer(scene, spawn);

        // NavMesh is baked before enemies exist so their colliders don't punch holes into it
        var encounterPoints = Encounters.Select(e => EncounterPoint(terrain, e.t, e.side)).ToArray();
        BakeEncounterNavMesh(scene, encounterPoints);
        PlaceEnemies(scene, terrain, encounterPoints);
        PlaceStarterWeapon(scene, terrain, spawn);

        AddToBuildSettings();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[The Descent] Populated: player at spawn, {Encounters.Length} enemies, NavMesh baked.");
    }

    // ─────────────────────────────────────────────────────────────────────────

    static void RemovePreviousPopulation(Scene scene)
    {
        var doomed = new HashSet<string>(GameplayRoots) { "Enemies", "Encounter NavMesh", "Starter Weapon" };
        foreach (var root in scene.GetRootGameObjects())
            if (doomed.Contains(root.name)) Object.DestroyImmediate(root);
        // The generator's preview camera is replaced by the real camera rig ("Main Camera" removed above)
    }

    /// <summary>
    /// Copies the playable setup (player, camera rig, HUD, menus, globals) from Game.unity into <paramref name="target"/>,
    /// keeping the references between the copies. Also used by the feature test scene builder.
    /// </summary>
    internal static void CopyGameplayRoots(Scene target, params string[] extraRoots)
    {
        var wanted = GameplayRoots.Concat(extraRoots).ToArray();
        var source = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);
        try
        {
            var roots = source.GetRootGameObjects().Where(r => wanted.Contains(r.name)).ToArray();
            var missing = wanted.Except(roots.Select(r => r.name)).ToArray();
            if (missing.Length > 0) Debug.LogWarning($"[The Descent] Not found in Game.unity: {string.Join(", ", missing)}");

            // Duplicate together (like Ctrl+D) so references between these objects are remapped to the copies
            Selection.objects = roots.Cast<Object>().ToArray();
            Unsupported.DuplicateGameObjectsUsingPasteboard();
            var copies = Selection.gameObjects.Where(g => g.transform.parent == null && g.scene == source).ToArray();

            foreach (var copy in copies)
            {
                // Scripts look these up by name (GameObject.Find("GlobalVars"), "MainMenu", ...)
                copy.name = Regex.Replace(copy.name, @" \(\d+\)$", "");
                USceneManager.MoveGameObjectToScene(copy, target);
            }
            Selection.objects = new Object[0];
        }
        finally
        {
            EditorSceneManager.CloseScene(source, true); // discard: the duplicates were only staged there
        }

        // GlobalVars carries the Game scene's baked NavMesh; it does not belong here
        var globals = target.GetRootGameObjects().FirstOrDefault(r => r.name == "GlobalVars");
        if (globals != null)
            foreach (var surface in globals.GetComponentsInChildren<NavMeshSurface>(true))
                surface.gameObject.SetActive(false);
    }

    internal static void PlacePlayer(Scene scene, Transform spawn)
    {
        var roots = scene.GetRootGameObjects().ToDictionary(r => r.name, r => r);
        if (!roots.TryGetValue("Player", out var player)) return;

        var controller = player.GetComponent<CharacterController>();
        if (controller) controller.enabled = false;
        player.transform.SetPositionAndRotation(spawn.position + Vector3.up * 0.2f, spawn.rotation);
        if (controller) controller.enabled = true;

        Vector3 back = -spawn.forward;
        if (roots.TryGetValue("CameraTaerget", out var camTarget))
            camTarget.transform.position = player.transform.position;
        if (roots.TryGetValue("Main Camera", out var cam))
        {
            cam.transform.position = player.transform.position + back * 6f + Vector3.up * 3f;
            cam.transform.LookAt(player.transform.position + Vector3.up * 1.5f);
        }
        if (roots.TryGetValue("EnemyTarget_Locator", out var locator))
            locator.transform.position = player.transform.position;
    }

    static Vector3 EncounterPoint(Terrain terrain, float t, float side)
    {
        var p = TheDescentGenerator.PathPoint(t, out _);
        var o = TheDescentGenerator.SideOffset(t, side);
        var pos = new Vector3(p.x + o.x, 0, p.z + o.y);
        pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;
        return pos;
    }

    static void BakeEncounterNavMesh(Scene scene, Vector3[] points)
    {
        if (!AssetDatabase.IsValidFolder(NavMeshFolder))
            AssetDatabase.CreateFolder("Assets/Scenes/World", "TheDescent");

        var parent = new GameObject("Encounter NavMesh");
        USceneManager.MoveGameObjectToScene(parent, scene);

        for (int i = 0; i < points.Length; i++)
        {
            var go = new GameObject($"NavMesh {i + 1:00}");
            go.transform.SetParent(parent.transform, false);
            go.transform.position = points[i];

            var surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            surface.size = new Vector3(170f, 120f, 170f);
            surface.center = Vector3.zero;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.25f;
            surface.BuildNavMesh();

            string path = $"{NavMeshFolder}/NavMesh-{i + 1:00}.asset";
            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surface.navMeshData, path); // persists the built data the surface already references
        }
        AssetDatabase.SaveAssets();
    }

    static void PlaceEnemies(Scene scene, Terrain terrain, Vector3[] points)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
        if (prefab == null)
        {
            Debug.LogError($"[The Descent] Missing {EnemyPrefab}");
            return;
        }

        var parent = new GameObject("Enemies");
        USceneManager.MoveGameObjectToScene(parent, scene);
        var player = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Player");

        for (int i = 0; i < points.Length; i++)
        {
            var enemy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            enemy.transform.SetParent(parent.transform, true);
            enemy.name = $"{Encounters[i].zone} - Enemy {i + 1}";
            enemy.transform.position = points[i];

            // Snap the enemy body and its patrol points onto the ground (patrol arrival checks include height)
            foreach (Transform child in enemy.transform)
            {
                var p = child.position;
                if (NavMesh.SamplePosition(p, out var hit, 6f, NavMesh.AllAreas)) p = hit.position;
                else p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                child.position = p + Vector3.up * 0.05f;
            }

            var ai = enemy.GetComponentInChildren<EnemyAI_HTN>(true);
            if (ai != null)
            {
                var so = new SerializedObject(ai);
                if (player != null) so.FindProperty("player").objectReferenceValue = player.transform;

                // Patrol between the prefab's "pos" markers
                var markers = enemy.transform.Cast<Transform>().Where(c => c.name.StartsWith("pos")).ToArray();
                var patrol = so.FindProperty("patrolPoints");
                patrol.arraySize = markers.Length;
                for (int m = 0; m < markers.Length; m++)
                    patrol.GetArrayElementAtIndex(m).objectReferenceValue = markers[m];
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    static void PlaceStarterWeapon(Scene scene, Terrain terrain, Transform spawn)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StarterWeaponPrefab);
        if (prefab == null) return;

        var sword = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        sword.name = "Starter Weapon";
        var pos = spawn.position + spawn.forward * 4f + spawn.right * 1.5f;
        pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y + 0.3f;
        sword.transform.position = pos;
    }

    static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == ScenePath)) return;
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
