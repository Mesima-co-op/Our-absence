using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// Our Absence > Characters > Build Player Base Character
///
/// Builds the naked, dressable player from the Quaternius packs in Assets/ThirdParty/Quaternius:
/// 1. Materials for the body, eyes, hair and outfits (built-in Standard shader), remapped onto the FBX files.
/// 2. Player_Base.prefab: the Superhero_Male body with every gameplay component of the Paladin player
///    (movement, input, lock-on, health, saving, weapons, pick-up IK), weapon sockets in the hands,
///    a CharacterOutfit component and a hairstyle.
/// 3. An OutfitPiece + an armour InventoryItem for each Peasant and Ranger clothing part, registered
///    for saving, so they can be picked up / given (G in the test lab) and worn from the Equipment menu.
/// 4. Runs the locomotion calibration so the new body's walk/run playback matches its speed.
/// Safe to re-run: everything is regenerated in place (asset GUIDs are kept).
/// </summary>
public static class PlayerCharacterBuilder
{
    const string Root = "Assets/ThirdParty/Quaternius";
    const string BaseModel = Root + "/UniversalBaseCharacters/Models/Superhero_Male_FullBody.fbx";
    const string HairModel = Root + "/UniversalBaseCharacters/Hair/Hair_SimpleParted.fbx";
    const string MaterialsDir = Root + "/Materials";
    const string PaladinPrefab = "Assets/Objects/Characters/Characters Collection/Paladin J Nordstrom@Idle.prefab";
    public const string PrefabPath = "Assets/Objects/Characters/Player/Player_Base.prefab";
    const string OutfitDir = "Assets/Data/Outfits";
    const string ItemsDir = "Assets/Data/Items/Clothing";
    const string ControllerPath = "Assets/animations/Animators/PlayerAnimator.controller";

    [MenuItem("Our Absence/Characters/Build Player Base Character")]
    public static void Build()
    {
        try
        {
            EditorUtility.DisplayProgressBar("Player character", "Materials", 0.1f);
            BuildMaterials();
            EditorUtility.DisplayProgressBar("Player character", "Prefab", 0.4f);
            BuildPrefab();
            CombatSetupTool.SetUpPlayerPrefab(PrefabPath); // back sockets, grounded feet
            EditorUtility.DisplayProgressBar("Player character", "Clothing", 0.7f);
            BuildClothing();
            ItemAddressablesSync.RegisterAll();
            AssetDatabase.SaveAssets();
            // The components were copied from the Paladin: measure this body's own clip speeds
            PlayerLocomotionCalibrator.Calibrate();
            Debug.Log($"[Character] Built {PrefabPath} and the clothing items in {ItemsDir}.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ── Materials ────────────────────────────────────────────────────────

    static readonly string BaseTex = Root + "/UniversalBaseCharacters/Textures/";
    static readonly string HairTex = Root + "/UniversalBaseCharacters/Hair/Textures/";
    static readonly string OutfitTex = Root + "/ModularOutfitsFantasy/Textures/";

    /// <summary>Material name used inside the FBX files → (albedo, normal, roughness map, ORM map).</summary>
    static readonly (string name, string albedo, string normal, string roughness, string orm)[] MaterialDefs =
    {
        ("MI_Superhero_Male", BaseTex + "T_Superhero_Male_Dark.png", BaseTex + "T_Superhero_Male_Normal.png", BaseTex + "T_Superhero_Male_Roughness.png", null),
        ("MI_Superhero_Female", BaseTex + "T_Superhero_Female_Dark_BaseColor.png", BaseTex + "T_Superhero_Female_Normal.png", BaseTex + "T_Superhero_Female_Roughness.png", null),
        ("MI_Eyes", BaseTex + "T_Eye_Brown.png", BaseTex + "T_Eye_Normal.png", null, null),
        ("MI_Hair_1", HairTex + "T_Hair_1_BaseColor.png", HairTex + "T_Hair_1_Normal.png", null, null),
        ("MI_Hair_2", HairTex + "T_Hair_2_BaseColor.png", HairTex + "T_Hair_2_Normal.png", null, null),
        ("MI_Regular_Male", OutfitTex + "T_Regular_Male_Dark_BaseColor.png", OutfitTex + "T_Regular_Male_Normal.png", OutfitTex + "T_Regular_Male_Roughness.png", null),
        ("MI_Regular_Female", OutfitTex + "T_Regular_Female_Dark_BaseColor.png", OutfitTex + "T_Regular_Female_Normal.png", OutfitTex + "T_Regular_Female_Roughness.png", null),
        ("MI_Peasant", OutfitTex + "T_Peasant_BaseColor.png", OutfitTex + "T_Peasant_Normal.png", null, OutfitTex + "T_Peasant_ORM.png"),
        ("MI_Ranger", OutfitTex + "T_Ranger_BaseColor.png", OutfitTex + "T_Ranger_Normal.png", null, OutfitTex + "T_Ranger_ORM.png"),
    };

    static void BuildMaterials()
    {
        Directory.CreateDirectory(MaterialsDir);
        var materials = new Dictionary<string, Material>();
        foreach (var def in MaterialDefs)
        {
            string path = $"{MaterialsDir}/{def.name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard")) { name = def.name };
                AssetDatabase.CreateAsset(mat, path);
            }

            var albedo = LoadTexture(def.albedo, normalMap: false);
            mat.SetTexture("_MainTex", albedo);
            // The hair textures are light grey, made to be tinted: dark brown
            mat.SetColor("_Color", def.name.StartsWith("MI_Hair") ? new Color(0.3f, 0.2f, 0.13f) : Color.white);

            var normal = LoadTexture(def.normal, normalMap: true);
            mat.SetTexture("_BumpMap", normal);
            if (normal != null) mat.EnableKeyword("_NORMALMAP");

            // Standard shader wants metallic in R and smoothness in A
            var gloss = def.orm != null ? MetallicGlossFromOrm(def.orm) : def.roughness != null ? MetallicGlossFromRoughness(def.roughness) : null;
            if (gloss != null)
            {
                mat.SetTexture("_MetallicGlossMap", gloss);
                mat.EnableKeyword("_METALLICGLOSSMAP");
                mat.SetFloat("_GlossMapScale", 1f);
            }
            else
            {
                mat.SetFloat("_Glossiness", def.name.StartsWith("MI_Eyes") ? 0.8f : 0.25f);
            }

            // Hair cards need alpha cut-out
            if (def.name.StartsWith("MI_Hair") && albedo != null && HasAlpha(def.albedo))
            {
                mat.SetFloat("_Mode", 1f);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                mat.SetOverrideTag("RenderType", "TransparentCutout");
            }
            EditorUtility.SetDirty(mat);
            materials[def.name] = mat;
        }
        AssetDatabase.SaveAssets();

        // Point every Quaternius FBX at these materials, and let the base body be masked at runtime
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) continue;
            bool changed = false;
            foreach (var embedded in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                if (!materials.TryGetValue(embedded.name, out var mat)) continue;
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), mat);
                changed = true;
            }
            // Already remapped materials no longer show up as embedded; keep their remaps
            foreach (var kv in importer.GetExternalObjectMap())
                if (kv.Key.type == typeof(Material) && materials.TryGetValue(kv.Key.name, out var mat) && kv.Value != mat)
                {
                    importer.AddRemap(kv.Key, mat);
                    changed = true;
                }
            // Readable meshes: the body is masked under clothing, and the clothing is measured for that
            if (!importer.isReadable)
            {
                importer.isReadable = true;
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
        }
    }

    static Texture2D LoadTexture(string path, bool normalMap)
    {
        if (path == null || !File.Exists(path)) return null;
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (importer.textureType != type)
        {
            importer.textureType = type;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static bool HasAlpha(string path) =>
        AssetImporter.GetAtPath(path) is TextureImporter ti && ti.DoesSourceTextureHaveAlpha();

    static Texture2D MetallicGlossFromRoughness(string roughnessPath) =>
        ConvertMap(roughnessPath, "_MetallicSmoothness", c => new Color(0f, 0f, 0f, 1f - c.r));

    /// <summary>ORM = occlusion (R), roughness (G), metallic (B)</summary>
    static Texture2D MetallicGlossFromOrm(string ormPath) =>
        ConvertMap(ormPath, "_MetallicSmoothness", c => new Color(c.b, c.b, c.b, 1f - c.g));

    static Texture2D ConvertMap(string sourcePath, string suffix, System.Func<Color, Color> convert)
    {
        if (!File.Exists(sourcePath)) return null;
        string outPath = Path.Combine(Path.GetDirectoryName(sourcePath), Path.GetFileNameWithoutExtension(sourcePath) + suffix + ".png").Replace('\\', '/');
        if (!File.Exists(outPath))
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            src.LoadImage(File.ReadAllBytes(sourcePath));
            var pixels = src.GetPixels();
            for (int i = 0; i < pixels.Length; i++) pixels[i] = convert(pixels[i]);
            var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
            dst.SetPixels(pixels);
            File.WriteAllBytes(outPath, dst.EncodeToPNG());
            Object.DestroyImmediate(src);
            Object.DestroyImmediate(dst);
            AssetDatabase.ImportAsset(outPath);
        }
        var importer = (TextureImporter)AssetImporter.GetAtPath(outPath);
        if (importer.sRGBTexture)
        {
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }

    // ── Player prefab ────────────────────────────────────────────────────

    static void BuildPrefab()
    {
        var paladin = AssetDatabase.LoadAssetAtPath<GameObject>(PaladinPrefab);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(BaseModel);
        if (paladin == null || model == null)
        {
            Debug.LogError("[Character] Paladin prefab or Quaternius base model missing.");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));

        var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = "Player_Base";
        try
        {
            var palAnimator = paladin.GetComponent<Animator>();
            var animator = GetOrAdd<Animator>(root);
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            animator.applyRootMotion = palAnimator.applyRootMotion;
            animator.cullingMode = palAnimator.cullingMode;
            animator.updateMode = palAnimator.updateMode;

            // Old object → new object, for re-pointing copied references
            var map = new Dictionary<Object, Object> { [paladin] = root, [paladin.transform] = root.transform };
            var sockets = MakeWeaponSockets(paladin, root, palAnimator, animator);
            for (int b = 0; b < (int)HumanBodyBones.LastBone; b++)
            {
                var from = palAnimator.GetBoneTransform((HumanBodyBones)b);
                var to = animator.GetBoneTransform((HumanBodyBones)b);
                if (from == null || to == null) continue;
                map[from] = to;
                map[from.gameObject] = to.gameObject;
            }
            // Weapons are held by the hand GameObjects: use sockets that match the Paladin's hand orientation
            map[palAnimator.GetBoneTransform(HumanBodyBones.LeftHand).gameObject] = sockets.left;
            map[palAnimator.GetBoneTransform(HumanBodyBones.RightHand).gameObject] = sockets.right;
            map[palAnimator] = animator;

            // Gameplay components, in dependency order
            var types = new[]
            {
                typeof(CharacterController), typeof(UnityEngine.InputSystem.PlayerInput), typeof(AnimationManager),
                typeof(HealthSystem), typeof(LocomotionSync), typeof(Player_controller), typeof(EnemyLockOn),
                typeof(SavePlayerData), typeof(DrawAndUndrawWeapons), typeof(RigBuilder),
            };
            var copied = new List<Component>();
            foreach (var type in types)
            {
                var src = paladin.GetComponent(type);
                if (src == null) continue;
                var dst = root.GetComponent(type);
                if (dst == null) dst = root.AddComponent(type);
                EditorUtility.CopySerialized(src, dst);
                map[src] = dst;
                copied.Add(dst);
            }

            // Pick-up IK rig (Rig + constraints): copy and point its bones at the new skeleton
            var palRig = paladin.GetComponentInChildren<Rig>(true);
            if (palRig != null)
            {
                var rigCopy = Object.Instantiate(palRig.gameObject, root.transform);
                rigCopy.name = palRig.gameObject.name;
                rigCopy.transform.localPosition = Vector3.zero;
                // The avatar finds bones by name: a rig child called "Head" would make it invalid
                foreach (var t in rigCopy.GetComponentsInChildren<Transform>(true))
                    if (t != rigCopy.transform && !t.name.StartsWith("Rig_")) t.name = "Rig_" + t.name;
                var srcAll = palRig.GetComponentsInChildren<Component>(true);
                var dstAll = rigCopy.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < srcAll.Length && i < dstAll.Length; i++)
                {
                    map[srcAll[i]] = dstAll[i];
                    map[srcAll[i].gameObject] = dstAll[i].gameObject;
                }
                copied.AddRange(dstAll.Where(c => c is MonoBehaviour));
            }

            foreach (var component in copied)
                Remap(component, map, paladin);

            // Body size: ~1.9 m tall at scale 1 (the Paladin was scaled 1.7x)
            var cc = root.GetComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.28f;
            cc.skinWidth = 0.08f;
            cc.center = new Vector3(0f, 0.963f, 0f); // root sits 0.017 m above the ground, so the soles touch it (measured idle and walking)
            cc.stepOffset = 0.3f;
            cc.slopeLimit = 50f;

            // Hair and the dressing component
            var body = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0).First();
            GetOrAdd<MovementFX>(root); // footstep, roll and landing dust
            var outfit = GetOrAdd<CharacterOutfit>(root);
            outfit.body = body;
            outfit.hair.Clear();
            var hairModel = AssetDatabase.LoadAssetAtPath<GameObject>(HairModel);
            if (hairModel != null)
                foreach (var src in hairModel.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    outfit.hair.Add(BindToSkeleton(src, root.transform, body, "Hair"));
            // Every part (body, eyes, brows, hair) follows the animated bones, so none gets culled mid-roll
            foreach (var part in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) part.updateWhenOffscreen = true;

            root.tag = paladin.tag;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = paladin.layer;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // GetComponent returns a fake-null object in the editor, so ?? can't be used
    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    /// <summary>
    /// Children of the hands whose rotation matches the Paladin's (mixamo) hand bones in the same pose,
    /// so weapons use the same grip offsets on both characters.
    /// </summary>
    static (GameObject left, GameObject right) MakeWeaponSockets(GameObject paladinPrefab, GameObject root, Animator palAnimatorPrefab, Animator animator)
    {
        var pal = (GameObject)Object.Instantiate(paladinPrefab);
        pal.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            pal.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var palAnimator = pal.GetComponent<Animator>();
            var idle = animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name.ToLower().Contains("idle"));
            if (idle != null)
            {
                idle.SampleAnimation(pal, 0f);
                idle.SampleAnimation(root, 0f);
            }

            GameObject Socket(HumanBodyBones bone, string name)
            {
                var hand = animator.GetBoneTransform(bone);
                var palHand = palAnimator.GetBoneTransform(bone);
                var old = hand.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var socket = new GameObject(name);
                socket.transform.SetParent(hand, false);
                socket.transform.rotation = palHand.rotation;
                return socket;
            }
            var sockets = (Socket(HumanBodyBones.LeftHand, "WeaponSocket_L"), Socket(HumanBodyBones.RightHand, "WeaponSocket_R"));

            // Back to the bind pose for saving
            var bind = AssetDatabase.LoadAssetAtPath<GameObject>(BaseModel);
            var bindBones = bind.GetComponentsInChildren<Transform>(true).ToDictionary(t => AnimationUtility.CalculateTransformPath(t, bind.transform));
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (bindBones.TryGetValue(AnimationUtility.CalculateTransformPath(t, root.transform), out var b) && t != root.transform)
                {
                    t.localPosition = b.localPosition;
                    t.localRotation = b.localRotation;
                    t.localScale = b.localScale;
                }
            return sockets;
        }
        finally
        {
            Object.DestroyImmediate(pal);
        }
    }

    /// <summary>Re-points references that still lead into the Paladin prefab.</summary>
    static void Remap(Component component, Dictionary<Object, Object> map, GameObject paladin)
    {
        var so = new SerializedObject(component);
        var it = so.GetIterator();
        while (it.Next(true))
        {
            if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
            var value = it.objectReferenceValue;
            if (map.TryGetValue(value, out var replacement))
                it.objectReferenceValue = replacement;
            else if (AssetDatabase.GetAssetPath(value) == PaladinPrefab)
            {
                Debug.LogWarning($"[Character] {component.GetType().Name}.{it.propertyPath} pointed into the Paladin ({value.name}); cleared.");
                it.objectReferenceValue = null;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static SkinnedMeshRenderer BindToSkeleton(SkinnedMeshRenderer source, Transform parent, SkinnedMeshRenderer body, string prefix)
    {
        var bones = body.rootBone.parent.GetComponentsInChildren<Transform>(true)
            .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        string name = $"{prefix}_{source.name}";
        var old = parent.Find(name);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var smr = go.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = source.sharedMesh;
        smr.sharedMaterials = source.sharedMaterials;
        smr.bones = source.bones.Select(b => b != null && bones.TryGetValue(b.name, out var t) ? t : null).ToArray();
        smr.rootBone = source.rootBone != null && bones.TryGetValue(source.rootBone.name, out var r) ? r : body.rootBone;
        smr.localBounds = body.localBounds;
        return smr;
    }

    // ── Clothing items ───────────────────────────────────────────────────

    static readonly string Parts = Root + "/ModularOutfitsFantasy/Parts/";

    static readonly (string id, int itemId, string display, OutfitSlot slot, MainMenuController.ItemType type, string[] models)[] Clothing =
    {
        ("Peasant_Shirt", 101, "Peasant Shirt", OutfitSlot.Body, MainMenuController.ItemType.Chestplate, new[] { "Male_Peasant_Body" }),
        ("Peasant_Sleeves", 102, "Peasant Sleeves", OutfitSlot.Arms, MainMenuController.ItemType.Gauntlets, new[] { "Male_Peasant_Arms" }),
        ("Peasant_Trousers", 103, "Peasant Trousers", OutfitSlot.Legs, MainMenuController.ItemType.Leggings, new[] { "Male_Peasant_Legs" }),
        ("Peasant_Shoes", 104, "Peasant Shoes", OutfitSlot.Feet, MainMenuController.ItemType.Boots, new[] { "Male_Peasant_Feet" }),
        ("Ranger_Hood", 105, "Ranger Hood", OutfitSlot.Head, MainMenuController.ItemType.Helmet, new[] { "Male_Ranger_Head_Hood" }),
        ("Ranger_Tunic", 106, "Ranger Tunic", OutfitSlot.Body, MainMenuController.ItemType.Chestplate, new[] { "Male_Ranger_Body", "Male_Ranger_Acc_Pauldron" }),
        ("Ranger_Bracers", 107, "Ranger Bracers", OutfitSlot.Arms, MainMenuController.ItemType.Gauntlets, new[] { "Male_Ranger_Arms" }),
        ("Ranger_Trousers", 108, "Ranger Trousers", OutfitSlot.Legs, MainMenuController.ItemType.Leggings, new[] { "Male_Ranger_Legs" }),
        ("Ranger_Boots", 109, "Ranger Boots", OutfitSlot.Feet, MainMenuController.ItemType.Boots, new[] { "Male_Ranger_Feet_Boots" }),
    };

    /// <summary>Clothes the base prefab spawns in (OutfitPiece ids). Empty: the player starts naked.</summary>
    static readonly string[] StartingOutfit = { };

    static void BuildClothing()
    {
        Directory.CreateDirectory(OutfitDir);
        Directory.CreateDirectory(ItemsDir);
        foreach (var c in Clothing)
        {
            string piecePath = $"{OutfitDir}/{c.id}.asset";
            var piece = AssetDatabase.LoadAssetAtPath<OutfitPiece>(piecePath);
            if (piece == null)
            {
                piece = ScriptableObject.CreateInstance<OutfitPiece>();
                AssetDatabase.CreateAsset(piece, piecePath);
            }
            piece.displayName = c.display;
            piece.slot = c.slot;
            piece.hides = OutfitPiece.DefaultHides(c.slot);
            piece.models = c.models.Select(m => AssetDatabase.LoadAssetAtPath<GameObject>($"{Parts}{m}.fbx")).Where(m => m != null).ToArray();
            EditorUtility.SetDirty(piece);

            var icon = RenderIcon(piece, $"{ItemsDir}/{c.id}_Icon.png");

            string itemPath = $"{ItemsDir}/{c.id}.asset";
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(itemPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(item, itemPath);
            }
            var so = new SerializedObject(item);
            so.FindProperty("itemName").stringValue = c.display;
            so.FindProperty("itemSubType").intValue = (int)c.type;
            so.FindProperty("itemCategory").intValue = (int)MainMenuController.CategoryType.Armor;
            so.FindProperty("itemID").intValue = c.itemId;
            so.FindProperty("icon").objectReferenceValue = icon;
            so.FindProperty("objectRef").objectReferenceValue = null; // nothing to hold or drop in the world
            so.FindProperty("isStackable").boolValue = false;
            so.FindProperty("maxStackSize").intValue = 1;
            so.FindProperty("maxDurability").floatValue = 100f;
            so.FindProperty("isDroppable").boolValue = true;
            so.FindProperty("wearable").objectReferenceValue = piece;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        AssetDatabase.SaveAssets();

        // Starting outfit on the prefab
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var outfit = root.GetComponent<CharacterOutfit>();
            outfit.startingOutfit = StartingOutfit
                .Select(id => AssetDatabase.LoadAssetAtPath<OutfitPiece>($"{OutfitDir}/{id}.asset"))
                .Where(p => p != null).ToList();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Renders the clothing models (bind pose) to a small PNG for the inventory.</summary>
    static Texture2D RenderIcon(OutfitPiece piece, string path)
    {
        const int size = 256;
        var preview = new PreviewRenderUtility();
        try
        {
            var meshes = new List<(Mesh mesh, Material[] mats, Matrix4x4 matrix)>();
            var bounds = new Bounds();
            bool first = true;
            foreach (var model in piece.models)
                foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var baked = new Mesh();
                    smr.BakeMesh(baked);
                    var matrix = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    meshes.Add((baked, smr.sharedMaterials, matrix));
                    var b = GeometryUtility.CalculateBounds(baked.vertices, matrix);
                    if (first) { bounds = b; first = false; } else bounds.Encapsulate(b);
                }
            if (meshes.Count == 0) return null;

            var cam = preview.camera;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.1f;
            cam.transform.position = bounds.center + new Vector3(0f, 0f, 5f);
            cam.transform.rotation = Quaternion.LookRotation(Vector3.back);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            preview.lights[0].intensity = 1.2f;
            preview.lights[0].transform.rotation = Quaternion.Euler(30f, 150f, 0f);
            preview.ambientColor = new Color(0.45f, 0.45f, 0.45f);

            preview.BeginStaticPreview(new Rect(0, 0, size, size));
            foreach (var (mesh, mats, matrix) in meshes)
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    preview.DrawMesh(mesh, matrix, mats[Mathf.Min(sub, mats.Length - 1)], sub);
            preview.camera.Render();
            var tex = preview.EndStaticPreview();

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            foreach (var (mesh, _, _) in meshes) Object.DestroyImmediate(mesh);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Character] Icon for {piece.name} failed: {e.Message}");
            return null;
        }
        finally
        {
            preview.Cleanup();
        }
    }
}
