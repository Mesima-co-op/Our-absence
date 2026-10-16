using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Our Absence > Animation > Calibrate Player Locomotion
///
/// 1. Adds the "moveAnimSpeed" parameter to the player animator and makes the locomotion
///    states (Movement, CombatMovement, LockOnMovement) use it as their speed multiplier.
/// 2. Measures how fast each walk/run clip's feet travel on the player model (in m/s).
/// 3. Adds/updates LocomotionSync on every player prefab with those numbers, and sets the
///    walk/run speeds on Player_controller.
/// Re-run it after changing locomotion clips or the character model.
/// </summary>
public static class PlayerLocomotionCalibrator
{
    const string ControllerPath = "Assets/animations/Animators/PlayerAnimator.controller";

    /// <summary>Player prefabs to calibrate (missing ones are skipped).</summary>
    public static readonly string[] PlayerPrefabs =
    {
        "Assets/Objects/Characters/Characters Collection/Paladin J Nordstrom@Idle.prefab",
        PlayerCharacterBuilder.PrefabPath,
    };

    static readonly string[] LocomotionStates = { "Movement", "CombatMovement", "LockOnMovement" };

    // Gameplay speeds (m/s). The animations are sped up/slowed down to match them.
    public const float WalkSpeed = 1.8f;
    public const float RunSpeed = 4.2f;
    public const float Acceleration = 14f;

    [MenuItem("Our Absence/Animation/Calibrate Player Locomotion")]
    public static void Calibrate()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[Locomotion] Missing {ControllerPath}");
            return;
        }
        SetupController(controller);

        foreach (var path in PlayerPrefabs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            var speeds = Measure(controller, prefab);
            ApplyToPrefab(path, speeds);
            Debug.Log($"[Locomotion] {prefab.name}: walk {speeds.walk:F2}, run {speeds.run:F2}, " +
                      $"combat walk {speeds.combatWalk:F2}, combat run {speeds.combatRun:F2}, lock-on {speeds.lockOn:F2} m/s " +
                      $"→ playback ×{WalkSpeed / speeds.walk:F2} walking, ×{RunSpeed / speeds.run:F2} running");
        }
        RevertSceneSpeedOverrides();
        AssetDatabase.SaveAssets();
    }

    // ── Animator controller ──────────────────────────────────────────────

    static void SetupController(AnimatorController controller)
    {
        if (controller.parameters.All(p => p.name != LocomotionSync.SpeedMultiplierParam))
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = LocomotionSync.SpeedMultiplierParam,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });

        foreach (var layer in controller.layers)
            foreach (var child in layer.stateMachine.states)
            {
                if (!LocomotionStates.Contains(child.state.name)) continue;
                child.state.speedParameterActive = true;
                child.state.speedParameter = LocomotionSync.SpeedMultiplierParam;
                EditorUtility.SetDirty(child.state);
            }
        EditorUtility.SetDirty(controller);
    }

    // ── Measuring ────────────────────────────────────────────────────────

    struct ClipSpeeds { public float walk, run, combatWalk, combatRun, lockOn; }

    static ClipSpeeds Measure(AnimatorController controller, GameObject prefab)
    {
        var go = (GameObject)Object.Instantiate(prefab);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
            var animator = go.GetComponent<Animator>();
            var left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var right = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            float Speed(ChildMotion child)
            {
                var clip = child.motion as AnimationClip;
                if (clip == null || left == null || right == null) return 0f;
                return FootSpeed(go, clip, left, right) * child.timeScale;
            }

            var speeds = new ClipSpeeds();
            var lockOn = new List<float>();
            foreach (var layer in controller.layers)
                foreach (var child in layer.stateMachine.states)
                {
                    if (!(child.state.motion is BlendTree tree)) continue;
                    switch (child.state.name)
                    {
                        case "Movement":
                            speeds.walk = Speed(ChildAt(tree, 1f));
                            speeds.run = Speed(ChildAt(tree, 2f));
                            break;
                        case "CombatMovement":
                            speeds.combatWalk = Speed(ChildAt(tree, 1f));
                            speeds.combatRun = Speed(ChildAt(tree, 2f));
                            break;
                        case "LockOnMovement":
                            lockOn.AddRange(tree.children.Where(c => c.position.sqrMagnitude > 0.01f).Select(Speed));
                            break;
                    }
                }
            speeds.lockOn = lockOn.Count > 0 ? lockOn.Average() : 1.75f;
            return speeds;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    static ChildMotion ChildAt(BlendTree tree, float threshold) =>
        tree.children.OrderBy(c => Mathf.Abs(c.threshold - threshold)).First();

    /// <summary>
    /// For an in-place cycle a foot that is on the ground slides backwards under the body at exactly
    /// the speed the body should move. A foot counts as "on the ground" in the frames where it is
    /// within the lowest 12% of its own height range; the median backward speed over those frames,
    /// averaged for both feet, is the clip's ground speed. (Validated in play mode: walking at the
    /// resulting speed leaves the planted foot still.)
    /// </summary>
    static float FootSpeed(GameObject go, AnimationClip clip, Transform left, Transform right)
    {
        const int samples = 120;
        float dt = clip.length / samples;
        var pos = new Vector3[2, samples + 1];
        for (int i = 0; i <= samples; i++)
        {
            clip.SampleAnimation(go, i * dt);
            // World meters relative to the body: InverseTransformPoint would divide by the
            // character's scale (the Paladin is scaled 1.7x) and under-measure the stride
            var unrotate = Quaternion.Inverse(go.transform.rotation);
            pos[0, i] = unrotate * (left.position - go.transform.position);
            pos[1, i] = unrotate * (right.position - go.transform.position);
        }

        var perFoot = new List<float>();
        for (int foot = 0; foot < 2; foot++)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i <= samples; i++)
            {
                lo = Mathf.Min(lo, pos[foot, i].y);
                hi = Mathf.Max(hi, pos[foot, i].y);
            }
            var planted = new List<float>();
            for (int i = 1; i <= samples; i++)
            {
                if (pos[foot, i].y > lo + 0.12f * (hi - lo)) continue;
                Vector3 d = pos[foot, i - 1] - pos[foot, i]; // backwards motion relative to the body
                d.y = 0f;
                planted.Add(d.magnitude / dt);
            }
            if (planted.Count > 0)
            {
                planted.Sort();
                perFoot.Add(planted[planted.Count / 2]);
            }
        }
        return perFoot.Count > 0 ? perFoot.Average() : 0f;
    }

    // ── Applying ─────────────────────────────────────────────────────────

    static void ApplyToPrefab(string path, ClipSpeeds speeds)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var sync = root.GetComponent<LocomotionSync>();
            if (sync == null) sync = root.AddComponent<LocomotionSync>();
            sync.walkClipSpeed = Mathf.Max(speeds.walk, 0.1f);
            sync.runClipSpeed = Mathf.Max(speeds.run, 0.1f);
            sync.combatWalkClipSpeed = Mathf.Max(speeds.combatWalk, 0.1f);
            sync.combatRunClipSpeed = Mathf.Max(speeds.combatRun, 0.1f);
            sync.lockOnClipSpeed = Mathf.Max(speeds.lockOn, 0.1f);

            var player = root.GetComponent<Player_controller>();
            if (player != null)
            {
                var so = new SerializedObject(player);
                so.FindProperty("speed").floatValue = WalkSpeed;
                so.FindProperty("runSpeed").floatValue = RunSpeed;
                so.FindProperty("acceleration").floatValue = Acceleration;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Scenes may override the old speeds on their Player instance; reset those to the prefab.</summary>
    static void RevertSceneSpeedOverrides()
    {
        string active = EditorSceneManager.GetActiveScene().path;
        foreach (var entry in EditorBuildSettings.scenes)
        {
            var scene = entry.path == active
                ? EditorSceneManager.GetActiveScene()
                : EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Additive);
            bool changed = false;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var player in root.GetComponentsInChildren<Player_controller>(true))
                {
                    if (!PrefabUtility.IsPartOfPrefabInstance(player)) continue;
                    var so = new SerializedObject(player);
                    foreach (var name in new[] { "speed", "runSpeed", "acceleration", "rotateSpeed" })
                    {
                        var prop = so.FindProperty(name);
                        if (prop != null && prop.prefabOverride)
                        {
                            PrefabUtility.RevertPropertyOverride(prop, InteractionMode.AutomatedAction);
                            changed = true;
                        }
                    }
                }
            if (changed) EditorSceneManager.SaveScene(scene);
            if (entry.path != active) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
