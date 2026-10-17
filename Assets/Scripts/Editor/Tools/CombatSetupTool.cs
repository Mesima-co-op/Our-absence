using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Sets up how weapons are held and how the player attacks:
/// - every weapon prefab gets a WeaponGrip with a "Grip" child at the middle of its handle;
/// - Player_Base gets BackSocket_L / BackSocket_R on its upper chest for sheathed weapons, its feet on the
///   ground and its clothing never culled mid-animation;
/// - the PlayerAnimator plays normal locomotion with a weapon drawn, and each hand attacks on its own
///   masked layer so the free arm keeps following the body.
/// Safe to re-run. Build Player Base Character runs the player part again after rebuilding the prefab.
/// </summary>
public static class CombatSetupTool
{
    const string WeaponsFolder = "Assets/Objects/Items/Weapons";
    const string ControllerPath = "Assets/animations/Animators/PlayerAnimator.controller";
    const string MaskFolder = "Assets/animations/Masks";
    const string OneHandedSwordClip = "BasicRightLightSwordAttack";

    [MenuItem("Our Absence/Combat/Set Up Weapons And Attacks")]
    public static void SetUpAll()
    {
        int weapons = SetUpWeaponGrips();
        SetUpPlayerPrefab(PlayerCharacterBuilder.PrefabPath);
        SetUpAnimator();
        AssetDatabase.SaveAssets();
        // Armed locomotion now uses the normal clips: measure their speeds again
        PlayerLocomotionCalibrator.Calibrate();
        Debug.Log($"[Combat] Grip points on {weapons} weapon(s), back sockets on Player_Base, attack layers in PlayerAnimator.");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Weapon grips
    // ─────────────────────────────────────────────────────────────────────

    public static int SetUpWeaponGrips()
    {
        int count = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { WeaponsFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || asset.GetComponent<WeaponDamageSystem>() == null) continue;

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!FindGrip(root.transform, out var position, out var rotation))
                {
                    Debug.LogWarning($"[Combat] {path}: no mesh to find the handle on.");
                    continue;
                }
                var grip = root.GetComponent<WeaponGrip>();
                if (grip == null) grip = root.AddComponent<WeaponGrip>();
                if (grip.grip == null)
                {
                    var existing = root.transform.Find("Grip");
                    grip.grip = existing != null ? existing : new GameObject("Grip").transform;
                    grip.grip.SetParent(root.transform, false);
                }
                grip.grip.localPosition = position;
                grip.grip.localRotation = rotation;
                grip.grip.localScale = Vector3.one;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                count++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        return count;
    }

    /// <summary>
    /// The handle is the short part between the crossguard (the widest slice along the blade's axis)
    /// and the nearer end. The grip goes at its middle, facing the far end.
    /// </summary>
    static bool FindGrip(Transform root, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        var points = new List<Vector3>();
        var toRoot = root.worldToLocalMatrix;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            if (mesh == null || !filter.GetComponent<Renderer>() || !filter.GetComponent<Renderer>().enabled) continue;
            var m = toRoot * filter.transform.localToWorldMatrix;
            foreach (var v in mesh.vertices) points.Add(m.MultiplyPoint3x4(v));
        }
        if (points.Count == 0) return false;

        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (var p in points) bounds.Encapsulate(p);
        var axes = new[] { 0, 1, 2 }.OrderByDescending(i => bounds.size[i]).ToArray();
        int along = axes[0], wide = axes[1];

        // Width of each slice along the long axis
        const int Bins = 48;
        var width = new float[Bins];
        float min = bounds.min[along], length = Mathf.Max(bounds.size[along], 1e-4f);
        foreach (var p in points)
        {
            int bin = Mathf.Clamp((int)((p[along] - min) / length * Bins), 0, Bins - 1);
            width[bin] = Mathf.Max(width[bin], Mathf.Abs(p[wide] - bounds.center[wide]));
        }
        int guard = System.Array.IndexOf(width, width.Max());
        bool gripAtMin = guard < Bins / 2;
        float guardPos = min + (guard + (gripAtMin ? 0f : 1f)) / Bins * length;
        float endPos = gripAtMin ? bounds.min[along] : bounds.max[along];
        float handleMid = Mathf.Lerp(endPos, guardPos, 0.55f);

        // Centre of the handle's cross-section
        var handle = points.Where(p => Mathf.Abs(p[along] - handleMid) < length * 0.04f).ToList();
        position = handle.Count > 0 ? handle.Aggregate(Vector3.zero, (a, p) => a + p) / handle.Count : bounds.center;
        position[along] = handleMid;

        Vector3 tip = Vector3.zero; tip[along] = gripAtMin ? 1f : -1f;
        Vector3 edge = Vector3.zero; edge[wide] = 1f;
        rotation = Quaternion.LookRotation(tip, edge);
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Player prefab
    // ─────────────────────────────────────────────────────────────────────

    public static void SetUpPlayerPrefab(string prefabPath)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var animator = root.GetComponent<Animator>();
            var chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            // The body's own frame in the prefab's pose (the mesh doesn't necessarily face the root's +Z)
            Vector3 bodyRight = (animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position
                               - animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position).normalized;
            Vector3 bodyUp = Vector3.ProjectOnPlane(animator.GetBoneTransform(HumanBodyBones.Head).position
                               - animator.GetBoneTransform(HumanBodyBones.Hips).position, bodyRight).normalized;
            var frame = (right: bodyRight, up: bodyUp, forward: Vector3.Cross(bodyRight, bodyUp));
            var right = BackSocket(chest, "BackSocket_R", 1f, frame);
            var left = BackSocket(chest, "BackSocket_L", -1f, frame);

            var draw = root.GetComponent<DrawAndUndrawWeapons>();
            if (draw != null)
            {
                var so = new SerializedObject(draw);
                so.FindProperty("rightBackSocket").objectReferenceValue = right;
                so.FindProperty("leftBackSocket").objectReferenceValue = left;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Soles on the ground: the root rests 0.017 m above it (measured idle and walking)
            var cc = root.GetComponent<CharacterController>();
            if (cc != null) cc.center = new Vector3(0f, 0.963f, 0f);

            // Body, face and hair follow the animated bones; fixed bounds let rolls cull them
            foreach (var part in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                part.updateWhenOffscreen = true;

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// A sheathed weapon hangs diagonally across the back: the handle above one shoulder,
    /// the blade down toward the other hip, flat against the back.
    /// </summary>
    static Transform BackSocket(Transform chest, string name, float side, (Vector3 right, Vector3 up, Vector3 forward) body)
    {
        var socket = chest.Find(name);
        if (socket == null)
        {
            socket = new GameObject(name).transform;
            socket.SetParent(chest, false);
        }
        Vector3 up = body.up, back = -body.forward, sideways = body.right * side;
        socket.position = chest.position + back * 0.17f + up * 0.22f + sideways * 0.13f;
        Vector3 blade = (-up - sideways * 0.45f).normalized;
        Vector3 edge = Vector3.Cross(back, blade).normalized; // in the plane of the back
        socket.rotation = Quaternion.LookRotation(blade, edge);
        return socket;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Animator
    // ─────────────────────────────────────────────────────────────────────

    public static void SetUpAnimator()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var layers = controller.layers;

        // 1. Armed locomotion = normal locomotion (the sword-and-shield clips raised the free fist)
        var baseMachine = layers[0].stateMachine;
        var movement = baseMachine.states.First(s => s.state.name == "Movement").state;
        var combatMovement = baseMachine.states.First(s => s.state.name == "CombatMovement").state;
        combatMovement.motion = movement.motion;

        // 2. Each hand attacks on its own layer, masked so the other arm isn't animated by the attack
        var combat = layers.First(l => l.name == "Combat_layer").stateMachine;
        var right = Find(combat, "AttackRight");
        var left = Find(combat, "AttackLeft");
        if (right != null) MoveToMaskedLayer(controller, combat, right, "AttackRight_layer", Mask("Mask_RightAttack", AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.LeftHandIK));
        if (left != null) MoveToMaskedLayer(controller, combat, left, "AttackLeft_layer", Mask("Mask_LeftAttack", AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.RightFingers, AvatarMaskBodyPart.RightHandIK));

        // 3. The left-hand sword attack is the right-hand one mirrored (it was a sword-and-shield slash)
        var leftState = FindInLayer(controller, "AttackLeft_layer", "AttackLeft");
        var rightState = FindInLayer(controller, "AttackRight_layer", "AttackRight");
        if (leftState?.motion is BlendTree leftTree && rightState?.motion is BlendTree rightTree)
        {
            var swordClip = rightTree.children.Select(c => c.motion).FirstOrDefault(m => m != null && m.name == OneHandedSwordClip);
            var children = leftTree.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].threshold < 0.5f || swordClip == null) continue;
                children[i].motion = swordClip;
                children[i].mirror = true;
            }
            leftTree.children = children;
        }

        EditorUtility.SetDirty(controller);
    }

    static AnimatorState Find(AnimatorStateMachine machine, string name) =>
        machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name);

    static AnimatorState FindInLayer(AnimatorController controller, string layer, string state)
    {
        var l = controller.layers.FirstOrDefault(x => x.name == layer);
        return l == null ? null : Find(l.stateMachine, state);
    }

    static void MoveToMaskedLayer(AnimatorController controller, AnimatorStateMachine from, AnimatorState source, string layerName, AvatarMask mask)
    {
        if (controller.layers.All(l => l.name != layerName))
        {
            var machine = new AnimatorStateMachine { name = layerName, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(machine, controller);
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = layerName,
                stateMachine = machine,
                avatarMask = mask,
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override,
            });
        }
        var layers = controller.layers;
        int index = System.Array.FindIndex(layers, l => l.name == layerName);
        layers[index].avatarMask = mask;
        layers[index].defaultWeight = 1f;
        controller.layers = layers;
        var target = layers[index].stateMachine;

        // An empty idle state that passes the layers below through
        var idle = Find(target, "Default") ?? target.AddState("Default", new Vector3(250, 0));
        target.defaultState = idle;
        var fromDefault = Find(from, "Default");
        if (fromDefault != null) idle.writeDefaultValues = fromDefault.writeDefaultValues;

        var moved = Find(target, source.name) ?? target.AddState(source.name, new Vector3(250, 120));
        moved.motion = source.motion;
        moved.speed = source.speed;
        moved.writeDefaultValues = source.writeDefaultValues;
        foreach (var behaviour in moved.behaviours) Object.DestroyImmediate(behaviour, true);
        moved.behaviours = new StateMachineBehaviour[0];
        foreach (var behaviour in source.behaviours)
        {
            var copy = moved.AddStateMachineBehaviour(behaviour.GetType());
            EditorUtility.CopySerialized(behaviour, copy);
        }

        // Same entry and exit as in the combat layer: Default -> attack on its bool, back when it ends
        var entry = fromDefault?.transitions.FirstOrDefault(t => t.destinationState == source);
        foreach (var t in idle.transitions.Where(t => t.destinationState == moved).ToArray()) idle.RemoveTransition(t);
        var go = idle.AddTransition(moved);
        if (entry != null) CopyTransition(entry, go);

        var exit = source.transitions.FirstOrDefault();
        foreach (var t in moved.transitions.ToArray()) moved.RemoveTransition(t);
        var back = moved.AddTransition(idle);
        if (exit != null) CopyTransition(exit, back);

        // RemoveState also deletes the state's blend tree, which the moved state now uses
        source.motion = null;
        from.RemoveState(source);
    }

    static void CopyTransition(AnimatorStateTransition from, AnimatorStateTransition to)
    {
        to.hasExitTime = from.hasExitTime;
        to.exitTime = from.exitTime;
        to.duration = from.duration;
        to.hasFixedDuration = from.hasFixedDuration;
        to.offset = from.offset;
        to.interruptionSource = from.interruptionSource;
        to.conditions = from.conditions;
    }

    static AvatarMask Mask(string name, params AvatarMaskBodyPart[] excluded)
    {
        if (!AssetDatabase.IsValidFolder(MaskFolder))
            AssetDatabase.CreateFolder("Assets/animations", "Masks");
        string path = $"{MaskFolder}/{name}.mask";
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, path);
        }
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, !excluded.Contains((AvatarMaskBodyPart)i));
        EditorUtility.SetDirty(mask);
        return mask;
    }
}
