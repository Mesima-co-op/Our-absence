using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

/// <summary>
/// Builds Assets/Scenes/Testing/FeatureTestLab.unity: a flat, well-lit lab with one station per feature
/// (weapons, dummies, every enemy personality, campfire hazard, NPC dialogue + mission, one-way ledge,
/// scene door / loading screen) plus the FeatureTestHelper panel and hotkeys.
/// Re-run it whenever features are added; extend BuildStations() with new stations.
/// </summary>
public static class FeatureTestSceneBuilder
{
    const string Folder = "Assets/Scenes/Testing";
    const string ScenePath = Folder + "/FeatureTestLab.unity";
    const string DataFolder = Folder + "/FeatureTestLab";

    const string EnemyPrefab = "Assets/Objects/Characters/Examples/Enemy.prefab";
    const string NpcPrefab = "Assets/Objects/Characters/Examples/NPC.prefab";
    const string CampfirePrefab = "Assets/Objects/environment/Campfire.prefab";
    const string PersonalityFolder = "Assets/Data/AI/Personalities";
    static readonly string[] WeaponPrefabs =
    {
        "Assets/Objects/Items/Weapons/GameObjects/ShortSword1.prefab",
        "Assets/Objects/Items/Weapons/GameObjects/RustyShortSword.prefab",
        "Assets/Objects/Items/Weapons/GameObjects/DragonTailSword.prefab",
        "Assets/Objects/Items/Weapons/GameObjects/RustyCurvedDagger.prefab",
    };

    const string TestMission = "test_mission";

    static Material groundMat, accentMat, darkMat;
    static Font signFont;

    [MenuItem("Our Absence/Testing/Build Feature Test Lab")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureFolder(Folder);
        EnsureFolder(DataFolder);
        CreateMaterials();
        signFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); // plain and readable at a distance

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SetupLighting();

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(16, 1, 16); // 160 m × 160 m
        ground.GetComponent<Renderer>().sharedMaterial = groundMat;
        ground.isStatic = true;

        var spawn = new GameObject("PlayerSpawn").transform;
        spawn.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var stations = new GameObject("Stations").transform;
        BuildStatic(stations);

        // Bake before characters exist so their colliders don't cut holes in the NavMesh
        BakeNavMesh(ground);

        TheDescentPopulator.CopyGameplayRoots(scene, "dialoguePanel");
        TheDescentPopulator.PlacePlayer(scene, spawn);
        PlayerSwapTool.SwapPlayer(scene); // the dressable Player_Base instead of the Paladin
        var player = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Player");

        BuildCharacters(stations, player, scene);
        AddHelper(spawn);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings();
        Debug.Log($"[Test Lab] Built {ScenePath}. Press Play in it; F1 shows the test panel.");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Stations without characters
    // ─────────────────────────────────────────────────────────────────────

    static void BuildStatic(Transform root)
    {
        // Weapon rack: pick up with E, equip in the menu (I), attack with the mouse
        var rack = Group(root, "Station - Weapons");
        Sign(rack, "WEAPONS", "E pick up · I equip (LW1/RW1) · LMB/RMB draw, then press again for combos · U sheathe", new Vector3(-8, 0, 5), 0);
        for (int i = 0; i < WeaponPrefabs.Length; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPrefabs[i]);
            if (prefab == null) continue;
            var weapon = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            weapon.transform.SetParent(rack, true);
            weapon.transform.position = new Vector3(-10.5f + i * 2f, 0.4f, 7f);
        }

        // Campfire hazard
        // Clothing: no props needed, the items come from G and are worn from the Equipment menu
        var wardrobe = Group(root, "Station - Wardrobe");
        Sign(wardrobe, "WARDROBE", "G gives the clothes · I > Equipment: Helmet, Chestplate, Gauntlets, Leggings, Boots · O cycles outfits", new Vector3(-16, 0, 5), 0);

        var fire = Group(root, "Station - Campfire");
        Sign(fire, "CAMPFIRE", "Stand in it: 5 damage per second, watch the HUD bar · H heals", new Vector3(9, 0, 4), 0);
        Spawn(CampfirePrefab, fire, new Vector3(9, 0, 8));

        // One-way ledge: climb the ramp, drop off the front, try to climb back
        var ledge = Group(root, "Station - Ledge");
        Sign(ledge, "ONE-WAY LEDGE", "Walk up the ramp behind it, drop off the front. You can't climb back.", new Vector3(17, 0, 13), 0);
        Box(ledge, "Tower", new Vector3(17, 2, 18), new Vector3(6, 4, 6), Quaternion.identity, darkMat);
        float rampLength = Mathf.Sqrt(11 * 11 + 4 * 4);
        Box(ledge, "Ramp", new Vector3(17, 1.85f, 26.5f), new Vector3(4, 0.4f, rampLength),
            Quaternion.Euler(Mathf.Atan2(4, 11) * Mathf.Rad2Deg, 0, 0), accentMat);

        // Scene door: tests SceneDoor + loading screen + save on exit
        var door = Group(root, "Station - Scene Door");
        Sign(door, "DOOR TO THE DESCENT", "Walk through: saves, then loads The Descent behind the loading screen", new Vector3(0, 0, -9), 180);
        Box(door, "Post L", new Vector3(-2, 2, -13), new Vector3(0.6f, 4, 0.6f), Quaternion.identity, darkMat);
        Box(door, "Post R", new Vector3(2, 2, -13), new Vector3(0.6f, 4, 0.6f), Quaternion.identity, darkMat);
        Box(door, "Lintel", new Vector3(0, 4.2f, -13), new Vector3(4.6f, 0.5f, 0.6f), Quaternion.identity, darkMat);
        var trigger = new GameObject("Door Trigger");
        trigger.transform.SetParent(door, false);
        trigger.transform.position = new Vector3(0, 1.5f, -13);
        var box = trigger.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(3.4f, 3f, 1f);
        trigger.AddComponent<SceneDoor>(); // default target: TheDescent
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Stations with characters
    // ─────────────────────────────────────────────────────────────────────

    static void BuildCharacters(Transform root, GameObject player, Scene scene)
    {
        var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);

        // Dummies: enemies with their AI switched off, for lock-on and damage tests
        var dummies = Group(root, "Station - Dummies");
        Sign(dummies, "TRAINING DUMMIES", "MMB lock on · hit them · left: 1000 HP, right: 40 HP (dies as a ragdoll)", new Vector3(0, 0, 13), 0);
        Dummy(enemyPrefab, dummies, "Training Dummy (1000 HP)", new Vector3(-3, 0, 17), 1000);
        Dummy(enemyPrefab, dummies, "Fragile Dummy (40 HP)", new Vector3(3, 0, 17), 40);

        // NPC with a mission-conditioned line
        var npcStation = Group(root, "Station - NPC");
        Sign(npcStation, "NPC DIALOGUE", "E talk / next line · M completes the test mission, then talk again", new Vector3(-14, 0, 13), 0);
        var npc = Spawn(NpcPrefab, npcStation, new Vector3(-14, 0, 17));
        if (npc != null) SetupNpc(npc, scene);

        // Every enemy personality in a row
        var arena = Group(root, "Station - Enemy Personalities");
        Sign(arena, "ENEMY PERSONALITIES", "One live enemy per AI personality. Walk up to one to start a fight.", new Vector3(0, 0, 36), 0);
        var personalities = AssetDatabase.FindAssets("t:EnemyPersonality", new[] { PersonalityFolder })
            .Select(g => AssetDatabase.LoadAssetAtPath<EnemyPersonality>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(p => p != null).OrderBy(p => p.name).ToArray();
        float spacing = 15f;
        float startX = -(personalities.Length - 1) * spacing / 2f;
        for (int i = 0; i < personalities.Length; i++)
        {
            var pos = new Vector3(startX + i * spacing, 0, 48);
            string label = personalities[i].name.Replace("Personality_", "").Replace("_", " ");
            Sign(arena, label.ToUpper(), "", pos + new Vector3(0, 0, -5), 0);
            var enemy = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab, scene);
            enemy.transform.SetParent(arena, true);
            enemy.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, 180, 0)); // facing the spawn side
            enemy.name = $"Enemy - {label}";
            var ai = enemy.GetComponentInChildren<EnemyAI_HTN>(true);
            if (ai != null)
            {
                var so = new SerializedObject(ai);
                so.FindProperty("personality").objectReferenceValue = personalities[i];
                if (player != null) so.FindProperty("player").objectReferenceValue = player.transform;
                so.FindProperty("patrolPoints").arraySize = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    static void Dummy(GameObject prefab, Transform parent, string name, Vector3 pos, int health)
    {
        if (prefab == null) return;
        var dummy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
        dummy.transform.SetParent(parent, true);
        dummy.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, 180, 0));
        dummy.name = name;

        // Through SerializedObject so the overrides on these prefab instances are saved with the scene
        foreach (Behaviour behaviour in dummy.GetComponentsInChildren<EnemyAI_HTN>(true).Cast<Behaviour>()
                     .Concat(dummy.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)))
        {
            var so = new SerializedObject(behaviour);
            so.FindProperty("m_Enabled").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        foreach (var hs in dummy.GetComponentsInChildren<HealthSystem>(true))
        {
            var so = new SerializedObject(hs);
            so.FindProperty("health").intValue = health;
            so.FindProperty("maxHealth").intValue = health;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static void SetupNpc(GameObject npc, Scene scene)
    {
        var system = npc.GetComponentInChildren<NPCSystem>(true);
        if (system == null) return;

        var panel = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "dialoguePanel");
        var so = new SerializedObject(system);
        if (panel != null)
        {
            so.FindProperty("dialoguePanel").objectReferenceValue = panel;
            so.FindProperty("dialogueText").objectReferenceValue = panel.GetComponentInChildren<Text>(true);
        }

        var lines = so.FindProperty("dialogueLines");
        lines.arraySize = 3;
        SetLine(lines.GetArrayElementAtIndex(0), "Welcome to the test lab, Unlit.", false, "", true);
        SetLine(lines.GetArrayElementAtIndex(1), "Press M to finish the test mission, then speak to me again.", true, TestMission, false);
        SetLine(lines.GetArrayElementAtIndex(2), "The test mission is done. Mission conditions work.", true, TestMission, true);
        so.FindProperty("exitDialogueText").stringValue = "Go on. Test something.";
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetLine(SerializedProperty line, string text, bool conditional, string mission, bool showIfCompleted)
    {
        line.FindPropertyRelative("text").stringValue = text;
        line.FindPropertyRelative("letterDelay").floatValue = 0.03f;
        line.FindPropertyRelative("lineDelay").floatValue = 0.4f;
        line.FindPropertyRelative("useMissionCondition").boolValue = conditional;
        line.FindPropertyRelative("missionId").stringValue = mission;
        line.FindPropertyRelative("showIfCompleted").boolValue = showIfCompleted;
    }

    static void AddHelper(Transform spawn)
    {
        var helper = new GameObject("Feature Test Helper").AddComponent<FeatureTestHelper>();
        var so = new SerializedObject(helper);
        so.FindProperty("spawnPoint").objectReferenceValue = spawn;
        so.FindProperty("testMissionId").stringValue = TestMission;
        so.FindProperty("stationNotes").stringValue =
            "<b>Stations</b> (from spawn)\n" +
            "• ahead-left: weapons rack · ahead-right: campfire\n" +
            "• ahead: training dummies · left: NPC, wardrobe sign\n" +
            "• right: one-way ledge · behind: door to The Descent\n" +
            "• far ahead: one enemy per AI personality";
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Environment
    // ─────────────────────────────────────────────────────────────────────

    static void SetupLighting()
    {
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.1f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.sun = sun;
        RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        RenderSettings.fog = false;
    }

    static void BakeNavMesh(GameObject ground)
    {
        var surface = ground.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
        string path = DataFolder + "/NavMesh.asset";
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(surface.navMeshData, path);
    }

    static void CreateMaterials()
    {
        groundMat = MakeMaterial("Ground", new Color(0.36f, 0.36f, 0.34f));
        accentMat = MakeMaterial("Accent", new Color(0.78f, 0.52f, 0.25f));
        darkMat = MakeMaterial("Stone", new Color(0.22f, 0.22f, 0.24f));
    }

    static Material MakeMaterial(string name, Color color)
    {
        string path = $"{DataFolder}/TestLab_{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = color;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    static Transform Group(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static GameObject Spawn(string path, Transform parent, Vector3 pos)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning($"[Test Lab] Missing prefab {path}");
            return null;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
        go.transform.SetParent(parent, true);
        go.transform.position = pos;
        return go;
    }

    static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rotation, Material mat)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.SetPositionAndRotation(center, rotation);
        cube.transform.localScale = size;
        cube.GetComponent<Renderer>().sharedMaterial = mat;
        cube.isStatic = true;
        return cube;
    }

    /// <summary>A post with a title and a one-line hint, readable from the spawn side (or turned by yaw).</summary>
    static void Sign(Transform parent, string title, string hint, Vector3 groundPos, float yaw)
    {
        var sign = new GameObject("Sign - " + title);
        sign.transform.SetParent(parent, false);
        sign.transform.SetPositionAndRotation(groundPos, Quaternion.Euler(0, yaw, 0));

        var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
        post.name = "Post";
        post.transform.SetParent(sign.transform, false);
        post.transform.localPosition = new Vector3(0, 1f, 0.05f);
        post.transform.localScale = new Vector3(0.15f, 2f, 0.15f);
        post.GetComponent<Renderer>().sharedMaterial = darkMat;

        Text3D(sign.transform, title, new Vector3(0, 2.45f, 0), 0.05f, new Color(0.95f, 0.8f, 0.45f));
        if (!string.IsNullOrEmpty(hint))
            Text3D(sign.transform, hint, new Vector3(0, 2.15f, 0), 0.022f, Color.white);
    }

    static void Text3D(Transform parent, string text, Vector3 localPos, float size, Color color)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var mesh = go.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.characterSize = size;
        mesh.fontSize = 64;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = color;
        if (signFont != null)
        {
            mesh.font = signFont;
            go.GetComponent<MeshRenderer>().sharedMaterial = signFont.material;
        }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace(System.IO.Path.DirectorySeparatorChar, '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == ScenePath)) return;
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true)); // needed for death reloads and the door back
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
