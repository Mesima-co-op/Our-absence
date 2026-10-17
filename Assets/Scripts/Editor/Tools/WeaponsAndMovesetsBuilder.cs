using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Our Absence > Combat > Build Weapons And Movesets
/// 1. Imports the Quaternius Universal Animation Library clips (clean names, the heavy combo cut into hits).
/// 2. Writes the movesets (Data/Combat): Unarmed, Sword, Dagger, Heavy. Hit windows were measured from
///    the hand speed in each clip.
/// 3. Rebuilds the attack layers of PlayerAnimator for PlayerCombat (Hit1..4 / Rec1..4 per hand with
///    placeholder clips, the left hand mirrored) and adds a hit-reaction layer.
/// 4. Turns the new weapon models into items: prefab, icon, inventory item, saving registration.
/// 5. Gives the existing swords the Sword moveset and Player_Base the PlayerCombat component.
/// Safe to re-run.
/// </summary>
public static class WeaponsAndMovesetsBuilder
{
    const string UalFolder = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary";
    const string Ual1 = UalFolder + "/UAL1_Standard_RM.fbx";
    const string Ual2 = UalFolder + "/UAL2_Standard_RM.fbx";
    const string ControllerPath = "Assets/animations/Animators/PlayerAnimator.controller";
    const string PlayerModel = "Assets/ThirdParty/Quaternius/UniversalBaseCharacters/Models/Superhero_Male_FullBody.fbx";
    const string SlotFolder = "Assets/animations/CombatSlots";
    const string MovesetFolder = "Assets/Data/Combat";
    const string WeaponPrefabFolder = "Assets/Objects/Items/Weapons/GameObjects";
    const string WeaponMaterialFolder = "Assets/Objects/Items/Weapons/Materials";
    const string ItemFolder = "Assets/Data/Items";

    // The heavy combo is one 130-frame clip with four blows; cut at the still moments between them
    static readonly (string name, int first, int last)[] HeavySplits =
    {
        ("Sword_Heavy_1", 0, 21), ("Sword_Heavy_2", 21, 47), ("Sword_Heavy_3", 47, 61), ("Sword_Heavy_4", 61, 130),
    };

    class NewWeapon
    {
        public string id, display, model, moveset;
        public int itemId;
        public MainMenuController.ItemType type;
        public float length, damage;
        public Color tint; // used when the model has no texture
    }

    static readonly NewWeapon[] NewWeapons =
    {
        new NewWeapon { id = "DragonTailSword", display = "Dragon Tail Sword", itemId = 3, type = MainMenuController.ItemType.HeavyWeapon,
            model = "Assets/Objects/Items/Weapons/GameObjects/Meshy_AI_Dragon_Tail_Sword_1017082430_generate.obj",
            length = 1.3f, damage = 30f, moveset = "Heavy", tint = new Color(0.55f, 0.5f, 0.45f) },
        new NewWeapon { id = "RustyCurvedDagger", display = "Rusty Curved Dagger", itemId = 4, type = MainMenuController.ItemType.LightWeapon,
            model = "Assets/Objects/Items/Weapons/Daggers/Rusty_Dagger_c/Meshy_AI_make_a_rusty_Dagger_1017082335_texture.obj",
            length = 0.5f, damage = 14f, moveset = "Dagger" },
    };

    // Existing weapons and their movesets
    static readonly (string item, string moveset)[] ExistingWeapons =
    {
        ("Assets/Data/Items/ShortSword1.asset", "Sword"),
        ("Assets/Data/Items/RustyShortSword.asset", "Sword"),
    };

    [MenuItem("Our Absence/Combat/Build Weapons And Movesets")]
    public static void Build()
    {
        try
        {
            EditorUtility.DisplayProgressBar("Combat", "Animation clips", 0.1f);
            ImportClips();
            EditorUtility.DisplayProgressBar("Combat", "Movesets", 0.3f);
            var movesets = BuildMovesets();
            EditorUtility.DisplayProgressBar("Combat", "Animator", 0.5f);
            BuildAnimator();
            EditorUtility.DisplayProgressBar("Combat", "Weapons", 0.7f);
            BuildWeapons(movesets);
            EditorUtility.DisplayProgressBar("Combat", "Player", 0.9f);
            SetUpPlayer(movesets["Unarmed"]);
            AssetDatabase.SaveAssets();
            CombatSetupTool.SetUpWeaponGrips(); // grips for the new weapons
            ItemAddressablesSync.RegisterAll();
            Debug.Log("[Combat] Built the movesets, the attack layers and the new weapons.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  1. Clips
    // ─────────────────────────────────────────────────────────────────────

    static void ImportClips()
    {
        foreach (var path in new[] { Ual1, Ual2 })
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Human;
            // Same skeleton as the player: use its avatar, so the clips face and stand exactly like it
            var playerAvatar = AssetDatabase.LoadAllAssetsAtPath(PlayerModel).OfType<Avatar>().FirstOrDefault();
            if (playerAvatar != null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = playerAvatar;
            }
            else importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;

            var clips = new List<ModelImporterClipAnimation>();
            foreach (var source in importer.defaultClipAnimations)
            {
                string name = source.takeName.Replace("Armature|", "");
                if (name == "A_TPose") continue;
                clips.Add(Clip(source, name, source.firstFrame, source.lastFrame));
                if (name == "Sword_Heavy_Combo")
                    foreach (var split in HeavySplits)
                        clips.Add(Clip(source, split.name, split.first, split.last));
            }
            importer.clipAnimations = clips.ToArray();
            importer.SaveAndReimport();
        }
    }

    static ModelImporterClipAnimation Clip(ModelImporterClipAnimation source, string name, float first, float last)
    {
        bool loop = name.EndsWith("_Loop") || name.EndsWith("Idle");
        return new ModelImporterClipAnimation
        {
            takeName = source.takeName,
            name = name,
            firstFrame = first,
            lastFrame = last,
            loopTime = loop,
            loopPose = loop,
            // The root-motion files keep the character's turning and stepping on the root bone. Leaving
            // them as root motion (not baked into the body) means the body animates relative to the player,
            // who keeps facing where it faces: LocomotionSync ignores root motion outside rolls and
            // PlayerCombat does its own lunge. Baking them made the whole body spin with the combo.
            lockRootRotation = false,
            lockRootPositionXZ = false,
            // Rotation follows the body's own orientation: the pose is stored relative to where the body faces,
            // and the net turn goes to root motion (ignored). The library's combos spin the character between
            // steps (Sword_Regular_A ends facing away and B starts from there); this keeps every swing facing
            // where the player faces, while the torso still twists through the swing.
            keepOriginalOrientation = false,
            rotationOffset = 0f,
            keepOriginalPositionXZ = true,
            // Height stays on the feet, so crouching attacks don't float
            lockRootHeightY = true,
            keepOriginalPositionY = false,
            heightFromFeet = true,
        };
    }

    static AnimationClip Ual(string name)
    {
        foreach (var path in new[] { Ual2, Ual1 })
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
            if (clip != null) return clip;
        }
        Debug.LogWarning($"[Combat] Clip {name} not found in the Universal Animation Library.");
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  2. Movesets (hit windows measured from the hand speed in each clip)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>A combo step; its hit window is measured from the clip (see MeasureHitWindow).</summary>
    static WeaponMoveset.Step Step(string clip, string recovery, float speed, float damage, float lunge, bool fists = false)
    {
        var step = new WeaponMoveset.Step
        {
            clip = Ual(clip), recovery = recovery != null ? Ual(recovery) : null,
            speed = speed, damage = damage, lunge = lunge,
        };
        (step.hitStart, step.hitEnd, step.mirror) = MeasureHitWindow(step.clip, fists);
        step.chainFrom = Mathf.Min(0.95f, step.hitEnd + 0.06f);
        return step;
    }

    static GameObject measureRig;

    static Dictionary<string, WeaponMoveset> BuildMovesets()
    {
        Folder(MovesetFolder);
        measureRig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCharacterBuilder.PrefabPath));
        measureRig.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var sets = new Dictionary<string, WeaponMoveset>
            {
                // Light attacks barely step forward, so they land where the player aims; heavier blows commit more.
                // Jab, cross, and around again (Melee_Hook is a diving lunge, too much for boxing)
                ["Unarmed"] = Moveset("Unarmed", "Bare hands", 0.4f,
                    Step("Punch_Jab", null, 1.15f, 1f, 0.05f, fists: true),
                    Step("Punch_Cross", null, 1.1f, 1.25f, 0.08f, fists: true)),
                // One-handed sword: two quick cuts and a spinning finisher
                ["Sword"] = Moveset("Sword", "One-handed sword", 0.45f,
                    Step("Sword_Regular_A", "Sword_Regular_A_Rec", 1f, 1f, 0.12f),
                    Step("Sword_Regular_B", "Sword_Regular_B_Rec", 1f, 1.1f, 0.12f),
                    Step("Sword_Regular_C", null, 1f, 1.6f, 0.2f)),
                // Dagger: the same cuts, faster and lighter, and a straight stab
                ["Dagger"] = Moveset("Dagger", "Dagger", 0.4f,
                    Step("Sword_Regular_A", "Sword_Regular_A_Rec", 1.4f, 0.8f, 0.08f),
                    Step("Sword_Regular_B", "Sword_Regular_B_Rec", 1.4f, 0.85f, 0.08f),
                    Step("Punch_Cross", null, 1.3f, 1.3f, 0.1f)),
                // Heavy blade: four slow, wide blows
                ["Heavy"] = Moveset("Heavy", "Heavy blade", 0.6f,
                    Step("Sword_Heavy_1", null, 0.95f, 1f, 0.2f),
                    Step("Sword_Heavy_2", null, 0.95f, 1.1f, 0.2f),
                    Step("Sword_Heavy_3", null, 0.95f, 1.25f, 0.25f),
                    Step("Sword_Heavy_4", null, 0.95f, 1.8f, 0.3f)),
            };
            // How each weapon class points out of the fist, fitted to its own animations
            sets["Sword"].bladeInFist = CalibrateBlade(sets["Sword"], 0.95f, allowReverseGrip: false);
            sets["Dagger"].bladeInFist = CalibrateBlade(sets["Dagger"], 0.45f, allowReverseGrip: true);
            sets["Heavy"].bladeInFist = CalibrateBlade(sets["Heavy"], 1.25f, allowReverseGrip: false);
            // How far each swing reaches in front of the player (arm + weapon)
            sets["Unarmed"].reach = 0.95f;
            sets["Dagger"].reach = 1.15f;
            sets["Sword"].reach = 1.5f;
            sets["Heavy"].reach = 1.85f;
            sets["Heavy"].arc = 85f; // wide sweeping blows
            foreach (var set in sets.Values) EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return sets;
        }
        finally
        {
            Object.DestroyImmediate(measureRig);
        }
    }

    /// <summary>
    /// The blade direction in the fist (x = thumb side, y = knuckles, z = palm) that, over all hit windows
    /// of the moveset, most often puts the blade in front of the player at striking height, without going
    /// through the player's own body. Tries ~600 directions over the sphere.
    /// </summary>
    static Vector3 CalibrateBlade(WeaponMoveset set, float bladeLength, bool allowReverseGrip)
    {
        var animator = measureRig.GetComponent<Animator>();
        animator.applyRootMotion = false;
        var root = measureRig.transform;

        // The fist in each sampled frame of each hit window, in the player's space
        var frames = new List<(Vector3 pos, Vector3 across, Vector3 knuckles, Vector3 palm, int step)>();
        for (int s = 0; s < set.combo.Length; s++)
        {
            var step = set.combo[s];
            if (step.clip == null) continue;
            var graph = UnityEngine.Playables.PlayableGraph.Create();
            try
            {
                graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
                var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "calibrate", animator);
                var playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, step.clip);
                UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, playable);
                for (int i = 0; i <= 12; i++)
                {
                    float t = Mathf.Lerp(step.hitStart, step.hitEnd, i / 12f);
                    UnityEngine.Playables.PlayableExtensions.SetTime(playable, step.clip.length * t);
                    graph.Evaluate();
                    if (WeaponGrip.FistFrame(animator, true, out var pos, out var across, out var knuckles, out var palm))
                        frames.Add((root.InverseTransformPoint(pos), root.InverseTransformDirection(across),
                                    root.InverseTransformDirection(knuckles), root.InverseTransformDirection(palm), s));
                }
            }
            finally
            {
                graph.Destroy();
            }
        }
        if (frames.Count == 0) return WeaponGrip.StraightOut;

        Vector3 best = WeaponGrip.StraightOut;
        float bestScore = float.MinValue;
        const int Candidates = 600;
        for (int c = 0; c < Candidates; c++)
        {
            // Even spread over the sphere (Fibonacci)
            float y = 1f - 2f * (c + 0.5f) / Candidates;
            float r = Mathf.Sqrt(1f - y * y);
            float phi = c * 2.39996323f;
            var dir = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);

            // Only grips a hand can hold: the blade leaves the fist through the thumb side (or, for a
            // reverse grip, the little-finger side), leaning at most a little toward the wrist
            bool forward = dir.x >= 0.55f, reverse = allowReverseGrip && dir.x <= -0.55f;
            if (!forward && !reverse) continue;
            if (dir.y < -0.3f || Mathf.Abs(dir.z) > 0.6f) continue;

            float score = 0f;
            var perStep = new float[set.combo.Length];
            foreach (var f in frames)
            {
                Vector3 blade = (f.across * dir.x + f.knuckles * dir.y + f.palm * dir.z).normalized;
                foreach (float k in new[] { 0.55f, 0.8f, 1f })
                {
                    Vector3 p = f.pos + blade * (bladeLength * k);
                    // In front of the player at striking height
                    if (Mathf.Abs(p.x) < 0.55f && p.y > 0.4f && p.y < 1.9f && p.z > 0.5f && p.z < 1.5f) perStep[f.step] += 1f;
                    // Through the player's own body
                    if (Mathf.Abs(p.x) < 0.25f && Mathf.Abs(p.z) < 0.2f && p.y > 0.2f && p.y < 1.75f) score -= 2f;
                }
            }
            // Every step should connect, not just the easy ones
            foreach (float s in perStep) score += Mathf.Sqrt(s) * 4f;
            // Slight preference for a natural grip, straight out of the fist
            score += Vector3.Dot(dir, WeaponGrip.StraightOut) * 0.5f;
            if (score > bestScore) { bestScore = score; best = dir; }
        }
        Debug.Log($"[Combat] {set.name}: blade in fist {best:F2}");
        return best;
    }

    /// <summary>
    /// The part of the clip (0-1) where the blow lands: the hand is in front of the body and moving fast.
    /// (Hand speed alone also catches the wind-up behind the back.) Weapons use the right hand; fists either,
    /// and leftHand reports a blow thrown with the left (the step is then mirrored).
    /// </summary>
    static (float start, float end, bool leftHand) MeasureHitWindow(AnimationClip clip, bool fists)
    {
        if (clip == null || measureRig == null) return (0.3f, 0.6f, false);
        var animator = measureRig.GetComponent<Animator>();
        animator.applyRootMotion = false;
        var hands = fists
            ? new[] { animator.GetBoneTransform(HumanBodyBones.RightHand), animator.GetBoneTransform(HumanBodyBones.LeftHand) }
            : new[] { animator.GetBoneTransform(HumanBodyBones.RightHand) };

        var graph = UnityEngine.Playables.PlayableGraph.Create();
        try
        {
            graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "measure", animator);
            var playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clip);
            UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, playable);

            int frames = Mathf.Max(2, Mathf.RoundToInt(clip.length * 60f));
            var score = new float[frames + 1];
            var handPeak = new float[hands.Length];
            var previous = new Vector3[hands.Length];
            for (int f = 0; f <= frames; f++)
            {
                UnityEngine.Playables.PlayableExtensions.SetTime(playable, clip.length * f / frames);
                graph.Evaluate();
                for (int h = 0; h < hands.Length; h++)
                {
                    Vector3 local = measureRig.transform.InverseTransformPoint(hands[h].position);
                    if (f > 0 && local.z > 0.12f) // in front of the chest
                    {
                        float speed = (local - previous[h]).magnitude * frames / clip.length;
                        score[f] = Mathf.Max(score[f], speed);
                        handPeak[h] = Mathf.Max(handPeak[h], speed);
                    }
                    previous[h] = local;
                }
            }

            int peak = System.Array.IndexOf(score, score.Max());
            if (score[peak] <= 0f) return (0.3f, 0.6f, false);
            bool leftHand = hands.Length > 1 && handPeak[1] > handPeak[0];
            float threshold = score[peak] * 0.3f;
            int first = peak, last = peak;
            while (first > 1 && score[first - 1] >= threshold) first--;
            while (last < frames && score[last + 1] >= threshold) last++;
            // A frame of margin each side: the blade leads and trails the hand
            return (Mathf.Clamp01((first - 2f) / frames), Mathf.Clamp01((last + 2f) / frames), leftHand);
        }
        finally
        {
            graph.Destroy();
        }
    }

    static WeaponMoveset Moveset(string id, string display, float buffer, params WeaponMoveset.Step[] steps)
    {
        string path = $"{MovesetFolder}/Moveset_{id}.asset";
        var set = AssetDatabase.LoadAssetAtPath<WeaponMoveset>(path);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<WeaponMoveset>();
            AssetDatabase.CreateAsset(set, path);
        }
        set.displayName = display;
        set.inputBuffer = buffer;
        set.combo = steps;
        EditorUtility.SetDirty(set);
        return set;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  3. Animator
    // ─────────────────────────────────────────────────────────────────────

    static void BuildAnimator()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Folder(SlotFolder);

        foreach (var param in new[] { PlayerCombat.SpeedParam(true), PlayerCombat.SpeedParam(false) })
            if (controller.parameters.All(p => p.name != param))
                controller.AddParameter(new AnimatorControllerParameter { name = param, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        foreach (var param in new[] { PlayerCombat.MirrorParam(true), PlayerCombat.MirrorParam(false) })
            if (controller.parameters.All(p => p.name != param))
                controller.AddParameter(new AnimatorControllerParameter { name = param, type = AnimatorControllerParameterType.Bool });

        BuildHandLayer(controller, true, "AttackRight_layer", "Mask_RightAttack");
        BuildHandLayer(controller, false, "AttackLeft_layer", "Mask_LeftAttack");
        BuildHitReactLayer(controller);
        MaskDrawLayer(controller);
        EditorUtility.SetDirty(controller);
    }

    /// <summary>
    /// The sword draw (shared with the enemies) turns the whole body sideways. On the player it only moves the
    /// arms and head, so drawing keeps the player facing where they aim.
    /// </summary>
    static void MaskDrawLayer(AnimatorController controller)
    {
        var mask = CombatSetupTool.Mask("Mask_Draw",
            AvatarMaskBodyPart.Root, AvatarMaskBodyPart.Body, AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg,
            AvatarMaskBodyPart.LeftFootIK, AvatarMaskBodyPart.RightFootIK);
        var layers = controller.layers;
        int index = System.Array.FindIndex(layers, l => l.name == PlayerCombat.DrawLayer);
        if (index < 0) return;
        layers[index].avatarMask = mask;
        controller.layers = layers;
    }

    static void BuildHandLayer(AnimatorController controller, bool right, string layerName, string maskName)
    {
        var machine = LayerMachine(controller, layerName, AssetDatabase.LoadAssetAtPath<AvatarMask>($"Assets/animations/Masks/{maskName}.mask"));
        ClearStates(machine);

        var idle = machine.AddState(PlayerCombat.IdleState, new Vector3(300, 0));
        idle.writeDefaultValues = false;
        machine.defaultState = idle;

        for (int i = 0; i < PlayerCombat.MaxSteps; i++)
        {
            for (int r = 0; r < 2; r++)
            {
                bool recovery = r == 1;
                var state = machine.AddState(PlayerCombat.StateName(recovery, i), new Vector3(550 + r * 250, i * 70));
                state.motion = Placeholder(PlayerCombat.SlotClip(right, recovery, i));
                // Mirrored per step by PlayerCombat: the left hand plays its clips mirrored, and a step thrown
                // with the left hand (like a jab) is mirrored back
                state.mirrorParameterActive = true;
                state.mirrorParameter = PlayerCombat.MirrorParam(right);
                state.writeDefaultValues = false;
                if (!recovery)
                {
                    state.speedParameterActive = true;
                    state.speedParameter = PlayerCombat.SpeedParam(right);
                }
            }
        }
    }

    static void BuildHitReactLayer(AnimatorController controller)
    {
        var machine = LayerMachine(controller, PlayerCombat.HitReactLayer, null);
        ClearStates(machine);
        var idle = machine.AddState(PlayerCombat.IdleState, new Vector3(300, 0));
        idle.writeDefaultValues = false;
        machine.defaultState = idle;
        int row = 0;
        foreach (var (state, clip) in new[] { ("HitChest", "Hit_Chest"), ("HitHead", "Hit_Head") })
        {
            var hit = machine.AddState(state, new Vector3(550, row++ * 70));
            hit.motion = Ual(clip);
            hit.writeDefaultValues = false;
            var back = hit.AddTransition(idle);
            back.hasExitTime = true;
            back.exitTime = 0.85f;
            back.duration = 0.15f;
        }
    }

    static AnimatorStateMachine LayerMachine(AnimatorController controller, string name, AvatarMask mask)
    {
        if (controller.layers.All(l => l.name != name))
        {
            var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(machine, controller);
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = name, stateMachine = machine, defaultWeight = 1f, blendingMode = AnimatorLayerBlendingMode.Override,
            });
        }
        var layers = controller.layers;
        int index = System.Array.FindIndex(layers, l => l.name == name);
        layers[index].avatarMask = mask;
        layers[index].defaultWeight = 1f;
        controller.layers = layers;
        return layers[index].stateMachine;
    }

    static void ClearStates(AnimatorStateMachine machine)
    {
        foreach (var child in machine.states.ToArray())
        {
            // RemoveState also deletes a blend tree the state owns; the attack blend trees are no longer used
            machine.RemoveState(child.state);
        }
    }

    /// <summary>An empty clip that only exists to be overridden (one per state, so each can be swapped alone).</summary>
    static AnimationClip Placeholder(string name)
    {
        string path = $"{SlotFolder}/{name}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip { name = name };
            AssetDatabase.CreateAsset(clip, path);
        }
        return clip;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  4. Weapons
    // ─────────────────────────────────────────────────────────────────────

    static void BuildWeapons(Dictionary<string, WeaponMoveset> movesets)
    {
        Folder(WeaponMaterialFolder);
        var template = AssetDatabase.LoadAssetAtPath<GameObject>($"{WeaponPrefabFolder}/RustyShortSword.prefab");

        foreach (var w in NewWeapons)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(w.model);
            if (model == null) { Debug.LogWarning($"[Combat] Missing model {w.model}"); continue; }

            // Item first: the prefab's ItemDataInstance points to it
            string itemPath = $"{ItemFolder}/{w.id}.asset";
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(itemPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(item, itemPath);
            }

            string prefabPath = $"{WeaponPrefabFolder}/{w.id}.prefab";
            var prefab = BuildWeaponPrefab(w, model, item, template, prefabPath);
            var icon = RenderIcon(prefab, $"{ItemFolder}/{w.id}_Icon.png");

            var so = new SerializedObject(item);
            so.FindProperty("itemName").stringValue = w.display;
            so.FindProperty("itemSubType").intValue = (int)w.type;
            so.FindProperty("itemCategory").intValue = (int)MainMenuController.CategoryType.Weapon;
            so.FindProperty("itemID").intValue = w.itemId;
            so.FindProperty("icon").objectReferenceValue = icon;
            so.FindProperty("objectRef").objectReferenceValue = prefab;
            so.FindProperty("isStackable").boolValue = false;
            so.FindProperty("maxStackSize").intValue = 1;
            so.FindProperty("maxDurability").floatValue = 100f;
            so.FindProperty("isDroppable").boolValue = true;
            so.FindProperty("moveset").objectReferenceValue = movesets[w.moveset];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        foreach (var (itemPath, moveset) in ExistingWeapons)
        {
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(itemPath);
            if (item == null) continue;
            var so = new SerializedObject(item);
            so.FindProperty("moveset").objectReferenceValue = movesets[moveset];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static GameObject BuildWeaponPrefab(NewWeapon w, GameObject model, InventoryItem item, GameObject template, string path)
    {
        // Same components as the existing swords: pick-up, item data, damage, trigger collider, rigidbody
        var root = (GameObject)PrefabUtility.InstantiatePrefab(template);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        try
        {
            root.name = w.id;
            root.transform.localScale = Vector3.one;
            foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                if (child.GetComponent<Light>() == null) Object.DestroyImmediate(child.gameObject); // keep the pickup glow
            Object.DestroyImmediate(root.GetComponent<WeaponGrip>());

            // The model, straightened so the blade runs along +Y with the handle at the bottom
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            visual.name = "Model";
            visual.transform.SetParent(root.transform, false);
            Straighten(visual.transform, w.length);
            if (w.tint.a > 0f) Retint(visual, w);

            var bounds = LocalBounds(root.transform);
            var box = root.GetComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.03f);
            var body = root.GetComponent<Rigidbody>();
            body.mass = w.type == MainMenuController.ItemType.HeavyWeapon ? 3f : 0.6f;
            foreach (var light in root.GetComponentsInChildren<Light>()) light.transform.localPosition = bounds.center;

            var data = root.GetComponent<ItemDataInstance>();
            data.itemID = w.itemId;
            data._definition = item;
            data.damageModifier = 1f;
            var damage = new SerializedObject(root.GetComponent<WeaponDamageSystem>());
            damage.FindProperty("baseDamage").floatValue = w.damage;
            damage.FindProperty("WeaponItem").objectReferenceValue = data;
            damage.ApplyModifiedPropertiesWithoutUndo();

            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// Rotates the model so its longest direction (found from the vertices, so diagonal models work too)
    /// is +Y with the handle end (the narrower, shorter part past the crossguard) at the origin, and scales
    /// it to <paramref name="length"/> metres.
    /// </summary>
    static void Straighten(Transform visual, float length)
    {
        var points = new List<Vector3>();
        foreach (var f in visual.GetComponentsInChildren<MeshFilter>())
        {
            var m = visual.worldToLocalMatrix * f.transform.localToWorldMatrix;
            var verts = f.sharedMesh.vertices;
            for (int i = 0; i < verts.Length; i += 4) points.Add(m.MultiplyPoint3x4(verts[i]));
        }
        Vector3 mean = points.Aggregate(Vector3.zero, (a, p) => a + p) / points.Count;

        // Principal axis by power iteration on the covariance
        var c = new float[3, 3];
        foreach (var p in points)
        {
            Vector3 d = p - mean;
            for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) c[a, b] += d[a] * d[b];
        }
        Vector3 axis = Vector3.one.normalized;
        for (int it = 0; it < 64; it++)
        {
            var n = new Vector3(
                c[0, 0] * axis.x + c[0, 1] * axis.y + c[0, 2] * axis.z,
                c[1, 0] * axis.x + c[1, 1] * axis.y + c[1, 2] * axis.z,
                c[2, 0] * axis.x + c[2, 1] * axis.y + c[2, 2] * axis.z);
            axis = n.normalized;
        }

        // Which end is the handle: the blade end is longer from the widest slice (crossguard)
        float min = points.Min(p => Vector3.Dot(p - mean, axis)), max = points.Max(p => Vector3.Dot(p - mean, axis));
        const int Bins = 40;
        var width = new float[Bins];
        foreach (var p in points)
        {
            float s = Vector3.Dot(p - mean, axis);
            int bin = Mathf.Clamp((int)((s - min) / (max - min) * Bins), 0, Bins - 1);
            width[bin] = Mathf.Max(width[bin], (p - mean - axis * s).magnitude);
        }
        int guard = System.Array.IndexOf(width, width.Max());
        if (guard > Bins / 2) axis = -axis; // handle must be at the low end
        float handleEnd = Mathf.Min(Vector3.Dot(points.OrderBy(p => Vector3.Dot(p - mean, axis)).First() - mean, axis), 0f);

        float scale = length / (max - min);
        var rotation = Quaternion.FromToRotation(axis, Vector3.up);
        visual.localRotation = rotation;
        visual.localScale = Vector3.one * scale;
        // Put the end of the handle at the root
        visual.localPosition = -(rotation * (mean + axis * handleEnd)) * scale;
    }

    static void Retint(GameObject visual, NewWeapon w)
    {
        string path = $"{WeaponMaterialFolder}/{w.id}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        foreach (var r in visual.GetComponentsInChildren<Renderer>())
        {
            if (r.sharedMaterial != null && r.sharedMaterial.mainTexture != null) return; // textured: keep it
            r.sharedMaterial = mat;
        }
        mat.color = w.tint;
        mat.SetFloat("_Metallic", 0.75f);
        mat.SetFloat("_Glossiness", 0.45f);
        EditorUtility.SetDirty(mat);
    }

    static Bounds LocalBounds(Transform root)
    {
        var b = new Bounds();
        bool first = true;
        foreach (var f in root.GetComponentsInChildren<MeshFilter>())
        {
            // From the vertices: the mesh's own box is far too big once the model is rotated
            var m = root.worldToLocalMatrix * f.transform.localToWorldMatrix;
            var verts = f.sharedMesh.vertices;
            for (int i = 0; i < verts.Length; i += 2)
            {
                var p = m.MultiplyPoint3x4(verts[i]);
                if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
            }
        }
        return b;
    }

    static Texture2D RenderIcon(GameObject prefab, string path)
    {
        const int size = 256;
        var preview = new PreviewRenderUtility();
        try
        {
            var go = preview.InstantiatePrefabInScene(prefab);
            foreach (var light in go.GetComponentsInChildren<Light>()) light.enabled = false;
            go.transform.rotation = Quaternion.Euler(0, 0, -45f); // diagonal, tip to the top right
            var bounds = new Bounds();
            bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
            }
            var cam = preview.camera;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.1f;
            cam.transform.position = bounds.center + new Vector3(0, 0, -5);
            cam.transform.rotation = Quaternion.identity;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            preview.lights[0].intensity = 2.2f;
            preview.lights[0].transform.rotation = Quaternion.Euler(30, 30, 0);
            preview.lights[1].intensity = 1.3f;
            preview.ambientColor = new Color(0.45f, 0.45f, 0.45f);
            preview.BeginPreview(new Rect(0, 0, size, size), GUIStyle.none);
            cam.Render();
            var rt = (RenderTexture)preview.EndPreview();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        finally
        {
            preview.Cleanup();
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.isReadable = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  5. Player
    // ─────────────────────────────────────────────────────────────────────

    static void SetUpPlayer(WeaponMoveset unarmed)
    {
        var root = PrefabUtility.LoadPrefabContents(PlayerCharacterBuilder.PrefabPath);
        try
        {
            var combat = root.GetComponent<PlayerCombat>();
            if (combat == null) combat = root.AddComponent<PlayerCombat>();
            var so = new SerializedObject(combat);
            so.FindProperty("unarmed").objectReferenceValue = unarmed;
            so.FindProperty("lungeStopDistance").floatValue = 0.75f;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PlayerCharacterBuilder.PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        Folder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
