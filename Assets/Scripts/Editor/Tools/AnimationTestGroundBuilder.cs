using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Builds Assets/Scenes/Testing/AnimationTestGround.unity: every player animation looping side by side on
/// stripped copies of Player_Base (ClipPreviewer), for spotting and tuning animation problems.
/// - One row per weapon moveset (each combo step, then its recovery), holding that moveset's weapon.
///   These read the moveset assets live, so edits during Play show at once.
/// - Then every other clip in the PlayerAnimator (locomotion, rolls, draws, hit reactions...).
/// Press Play in the scene; the camera (AnimationTestGround) has the controls. Re-run after adding clips.
/// </summary>
public static class AnimationTestGroundBuilder
{
    const string Folder = "Assets/Scenes/Testing";
    const string ScenePath = Folder + "/AnimationTestGround.unity";
    const string DataFolder = Folder + "/AnimationTestGround";
    const string ControllerPath = DataFolder + "/ClipPreview.controller";
    const string PlayerAnimatorPath = "Assets/animations/Animators/PlayerAnimator.controller";
    const string MovesetFolder = "Assets/Data/Combat";

    const float Spacing = 2.6f;    // between clips in a row
    const float RowSpacing = 5f;   // between rows (more than the longest reach)
    const int OtherClipsPerRow = 10;

    static readonly string[] MovesetOrder = { "Unarmed", "Sword", "Dagger", "Heavy" };

    static Font font;
    static Material faceMat, reachMat;

    [MenuItem("Our Absence/Testing/Build Animation Test Ground")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCharacterBuilder.PrefabPath);
        if (prefab == null) { Debug.LogError($"[Anim Test] No player prefab at {PlayerCharacterBuilder.PrefabPath}"); return; }

        if (!AssetDatabase.IsValidFolder(DataFolder)) AssetDatabase.CreateFolder(Folder, "AnimationTestGround");
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        faceMat = UnlitMaterial("Facing", new Color(0.2f, 0.85f, 0.3f));
        reachMat = UnlitMaterial("Reach", new Color(0.95f, 0.25f, 0.2f));
        var controller = PreviewController();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Environment();

        var root = new GameObject("Clips").transform;
        int row = 0;
        float widest = 0f;
        var used = new HashSet<AnimationClip>();

        // Moveset rows: each step and its recovery, with the weapon that uses the moveset
        var movesets = AssetDatabase.FindAssets("t:WeaponMoveset", new[] { MovesetFolder })
            .Select(g => AssetDatabase.LoadAssetAtPath<WeaponMoveset>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(m => m != null)
            .OrderBy(m => { int i = Array.FindIndex(MovesetOrder, n => m.name.EndsWith(n)); return i < 0 ? 99 : i; })
            .ThenBy(m => m.name)
            .ToList();
        foreach (var set in movesets)
        {
            var weaponPrefab = WeaponFor(set);
            RowSign(root, row, $"{set.displayName.ToUpper()}\n{set.name}{(weaponPrefab != null ? " · " + weaponPrefab.name : "")}");
            int col = 0;
            for (int i = 0; i < set.combo.Length; i++)
            {
                var step = set.combo[i];
                foreach (bool rec in new[] { false, true })
                {
                    var clip = rec ? step.recovery : step.clip;
                    if (clip == null) continue;
                    used.Add(clip);
                    var p = Mannequin(prefab, controller, root, row, col++);
                    p.moveset = set;
                    p.step = i;
                    p.recovery = rec;
                    if (weaponPrefab != null) p.weapon = Weapon(weaponPrefab, p.transform);
                    AddReachMarker(p);
                }
            }
            widest = Mathf.Max(widest, col);
            row++;
        }

        // Every other clip the player's animator uses
        var others = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerAnimatorPath).animationClips
            .Where(c => c != null && !used.Contains(c) && !AssetDatabase.GetAssetPath(c).Contains("/CombatSlots/"))
            .Distinct()
            .OrderBy(c => c.name)
            .ToList();
        for (int i = 0; i < others.Count; i++)
        {
            if (i % OtherClipsPerRow == 0) RowSign(root, row + i / OtherClipsPerRow, i == 0 ? "OTHER PLAYER CLIPS\nPlayerAnimator" : "");
            var p = Mannequin(prefab, controller, root, row + i / OtherClipsPerRow, i % OtherClipsPerRow);
            p.clip = others[i];
            if (others[i].name == "mixamo.com")
                p.clipTitle = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(others[i]));
            widest = Mathf.Max(widest, i % OtherClipsPerRow + 1);
        }

        PlaceCamera(widest);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[Anim Test] Built {ScenePath} with {Object.FindObjectsOfType<ClipPreviewer>().Length} clips. Press Play; Tab focuses a clip, F1 shows the controls.");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Mannequins
    // ─────────────────────────────────────────────────────────────────────

    static ClipPreviewer Mannequin(GameObject prefab, RuntimeAnimatorController controller, Transform root, int row, int col)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.transform.SetParent(root, false);
        // Facing the camera (which looks along +Z from the front), rows going back
        go.transform.SetPositionAndRotation(new Vector3(col * Spacing, 0f, row * RowSpacing), Quaternion.Euler(0f, 180f, 0f));
        // The Animation Rigging rig objects (aim/IK constraints) are empty once stripped
        var rigs = go.GetComponentsInChildren<MonoBehaviour>(true)
            .Where(c => c != null && c.GetType().Name == "Rig")
            .Select(c => c.gameObject)
            .ToList();
        Strip(go, keep: null);
        foreach (var rig in rigs) if (rig != null) Object.DestroyImmediate(rig);
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.tag = "Untagged"; // nothing should mistake a mannequin for the player
        var animator = go.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var p = go.AddComponent<ClipPreviewer>();
        go.name = "Clip";

        // Label above the head, facing bar on the ground
        var label = new GameObject("Label");
        label.transform.SetParent(go.transform, false);
        label.transform.localPosition = new Vector3(0f, 2.25f, 0f);
        var text = label.AddComponent<TextMesh>();
        text.font = font;
        text.fontSize = 48;
        text.characterSize = 0.025f;
        text.anchor = TextAnchor.LowerCenter;
        text.alignment = TextAlignment.Center;
        label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        p.label = text;

        Bar("Facing", go.transform, new Vector3(0f, 0.01f, 0.45f), new Vector3(0.05f, 0.01f, 0.9f), faceMat);
        return p;
    }

    static void AddReachMarker(ClipPreviewer p)
    {
        p.reachMarker = Bar("Reach", p.transform, new Vector3(0f, 0.01f, p.moveset.reach), new Vector3(0.7f, 0.01f, 0.05f), reachMat);
    }

    static Transform Bar(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat)
    {
        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = name;
        Object.DestroyImmediate(bar.GetComponent<Collider>());
        bar.transform.SetParent(parent, false);
        bar.transform.localPosition = localPos;
        bar.transform.localScale = size;
        bar.GetComponent<Renderer>().sharedMaterial = mat;
        return bar.transform;
    }

    /// <summary>A stripped copy of the weapon: only its mesh and grip. ClipPreviewer puts it in the fist at runtime.</summary>
    static WeaponGrip Weapon(GameObject prefab, Transform owner)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.transform.SetParent(owner, false);
        go.transform.localScale = prefab.transform.localScale;
        Strip(go, keep: typeof(WeaponGrip));
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.tag = "Untagged";
        return go.GetComponent<WeaponGrip>();
    }

    /// <summary>The first item that fights with this moveset and has a world model.</summary>
    static GameObject WeaponFor(WeaponMoveset set)
    {
        return AssetDatabase.FindAssets("t:InventoryItem")
            .Select(g => AssetDatabase.LoadAssetAtPath<InventoryItem>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(i => i != null && i.Moveset == set && i.ObjectRef != null)
            .OrderBy(i => i.ItemID)
            .Select(i => i.ObjectRef)
            .FirstOrDefault();
    }

    /// <summary>
    /// Removes every script (except <paramref name="keep"/>), collider, rigidbody and character controller,
    /// so the copy only animates. Components another one requires are removed after it.
    /// </summary>
    static void Strip(GameObject go, Type keep)
    {
        for (int pass = 0; pass < 8; pass++)
        {
            var doomed = go.GetComponentsInChildren<Component>(true)
                .Where(c => c is MonoBehaviour || c is Collider || c is Rigidbody || c is CharacterController)
                .Where(c => c != null && (keep == null || !keep.IsInstanceOfType(c)))
                .ToList();
            if (doomed.Count == 0) return;
            foreach (var c in doomed)
            {
                if (c == null) continue;
                bool required = c.GetComponents<Component>().Any(other => other != c && other != null && Requires(other.GetType(), c.GetType()));
                if (!required) Object.DestroyImmediate(c);
            }
        }
    }

    static bool Requires(Type owner, Type component)
    {
        foreach (RequireComponent r in owner.GetCustomAttributes(typeof(RequireComponent), true))
            if ((r.m_Type0 != null && r.m_Type0.IsAssignableFrom(component)) ||
                (r.m_Type1 != null && r.m_Type1.IsAssignableFrom(component)) ||
                (r.m_Type2 != null && r.m_Type2.IsAssignableFrom(component)))
                return true;
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Scene
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>One layer, one state "Clip" (mirrored by a parameter) whose placeholder each previewer overrides.</summary>
    static AnimatorController PreviewController()
    {
        string slotPath = DataFolder + "/PreviewSlot.anim";
        var slot = AssetDatabase.LoadAssetAtPath<AnimationClip>(slotPath);
        if (slot == null)
        {
            slot = new AnimationClip { name = "PreviewSlot" };
            AssetDatabase.CreateAsset(slot, slotPath);
        }

        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter(ClipPreviewer.MirrorParam, AnimatorControllerParameterType.Bool);
        var machine = controller.layers[0].stateMachine;
        var state = machine.AddState(ClipPreviewer.State);
        state.motion = slot;
        state.mirrorParameterActive = true;
        state.mirrorParameter = ClipPreviewer.MirrorParam;
        machine.defaultState = state;
        AssetDatabase.SaveAssets();
        return controller;
    }

    static void Environment()
    {
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.1f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(50f, 150f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.58f);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = new Vector3(12f, 0f, 15f);
        ground.transform.localScale = new Vector3(8f, 1f, 8f);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/FeatureTestLab/TestLab_Ground.mat");
        if (mat != null) ground.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static void RowSign(Transform root, int row, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var sign = new GameObject("Row " + row);
        sign.transform.SetParent(root, false);
        sign.transform.position = new Vector3(-2.6f, 0.05f, row * RowSpacing - 0.6f);
        sign.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // lying on the ground, readable from the camera
        var mesh = sign.AddComponent<TextMesh>();
        mesh.font = font;
        mesh.text = text;
        mesh.fontSize = 48;
        mesh.characterSize = 0.04f;
        mesh.anchor = TextAnchor.MiddleRight;
        mesh.alignment = TextAlignment.Right;
        mesh.color = new Color(1f, 0.85f, 0.5f);
        sign.GetComponent<MeshRenderer>().sharedMaterial = font.material;
    }

    static void PlaceCamera(float widest)
    {
        var go = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = go.AddComponent<UnityEngine.Camera>();
        cam.fieldOfView = 50f;
        cam.nearClipPlane = 0.05f;
        go.AddComponent<AudioListener>();
        go.AddComponent<AnimationTestGround>();
        // Overview of the moveset rows (the widest is about five clips); fly further for the rest
        float centre = Mathf.Min(widest - 1, 4) * Spacing * 0.5f;
        go.transform.position = new Vector3(centre, 6f, -8f);
        go.transform.LookAt(new Vector3(centre, 0.5f, 6f));
    }

    static Material UnlitMaterial(string name, Color color)
    {
        string path = $"{DataFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Unlit/Color"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = color;
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
