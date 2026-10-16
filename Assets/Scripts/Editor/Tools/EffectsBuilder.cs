using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the combat and movement effects: textures, materials, particle prefabs and the FXLibrary
/// in Resources, then adds MovementFX to the player and enemy prefabs. Safe to re-run; it overwrites
/// what it made, so tweak the prefabs by hand only if you won't re-run it (or change the numbers here).
/// </summary>
public static class EffectsBuilder
{
    const string Root = "Assets/Effects/Combat";
    const string TexFolder = Root + "/Textures";
    const string MatFolder = Root + "/Materials";
    const string LibraryPath = "Assets/Resources/FX/FXLibrary.asset";

    static readonly string[] CharacterPrefabs =
    {
        PlayerCharacterBuilder.PrefabPath,
        "Assets/Objects/Characters/Examples/Enemy.prefab",
    };

    [MenuItem("Our Absence/Effects/Build Combat And Movement FX")]
    public static void Build()
    {
        Folder("Assets/Effects"); Folder(Root); Folder(TexFolder); Folder(MatFolder);
        Folder("Assets/Resources"); Folder("Assets/Resources/FX");

        var softDot = Texture("FX_SoftDot", 64, 64, (u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float a = Mathf.Clamp01(1f - d);
            return new Color(1, 1, 1, a * a);
        });
        var spark = Texture("FX_Spark", 32, 64, (u, v) =>
        {
            // A thin bright streak (stretched further by the renderer)
            float across = 1f - Mathf.Abs(u - 0.5f) * 2f;
            float along = 1f - Mathf.Abs(v - 0.5f) * 2f;
            return new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(across), 3f) * Mathf.Clamp01(along * 1.5f));
        });
        var smoke = Texture("FX_Smoke", 128, 128, (u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float n = Mathf.PerlinNoise(u * 5f + 3.1f, v * 5f + 7.7f) * 0.6f + Mathf.PerlinNoise(u * 11f, v * 11f) * 0.4f;
            float a = Mathf.Clamp01(1f - d) * Mathf.Lerp(0.5f, 1.2f, n);
            return new Color(1, 1, 1, Mathf.Clamp01(a * a * 1.4f));
        });
        var trailTex = Texture("FX_Trail", 8, 64, (u, v) =>
        {
            // Bright along the blade's edge (v = 1), fading toward the base
            float a = Mathf.Pow(v, 1.6f) * (1f - Mathf.SmoothStep(0.94f, 1f, v) * 0.6f);
            return new Color(1, 1, 1, a);
        });

        var sparkMat = Material("FX_Spark", "Legacy Shaders/Particles/Additive", spark);
        var flashMat = Material("FX_Flash", "Legacy Shaders/Particles/Additive", softDot);
        var bloodMat = Material("FX_Blood", "Legacy Shaders/Particles/Alpha Blended", softDot);
        var dustMat = Material("FX_Dust", "Legacy Shaders/Particles/Alpha Blended", smoke);
        var trailMat = Material("FX_WeaponTrail", "Legacy Shaders/Particles/Additive", trailTex);

        var lib = AssetDatabase.LoadAssetAtPath<FXLibrary>(LibraryPath);
        if (lib == null)
        {
            lib = ScriptableObject.CreateInstance<FXLibrary>();
            AssetDatabase.CreateAsset(lib, LibraryPath);
        }
        lib.hitSparks = HitSparks(sparkMat, flashMat);
        lib.bloodSpray = Blood(bloodMat);
        lib.footstepDust = Dust("FX_FootstepDust", dustMat, burst: 5, size: new Vector2(0.22f, 0.4f), speed: new Vector2(0.3f, 0.8f), life: new Vector2(0.6f, 1f), radius: 0.08f, ring: false, alpha: 0.5f);
        lib.rollDust = Dust("FX_RollDust", dustMat, burst: 14, size: new Vector2(0.3f, 0.55f), speed: new Vector2(0.6f, 1.6f), life: new Vector2(0.8f, 1.3f), radius: 0.25f, ring: false, alpha: 0.55f);
        lib.landDust = Dust("FX_LandDust", dustMat, burst: 18, size: new Vector2(0.25f, 0.45f), speed: new Vector2(1.2f, 2.6f), life: new Vector2(0.7f, 1.1f), radius: 0.2f, ring: true, alpha: 0.6f);
        lib.deathDust = Dust("FX_DeathDust", dustMat, burst: 26, size: new Vector2(0.4f, 0.8f), speed: new Vector2(0.8f, 2f), life: new Vector2(1.2f, 1.8f), radius: 0.5f, ring: true, alpha: 0.6f);
        lib.weaponTrail = trailMat;
        lib.weaponTrailColor = Gradient(
            (0f, new Color(1f, 0.92f, 0.78f, 0.85f)),
            (0.45f, new Color(1f, 0.62f, 0.3f, 0.4f)),
            (1f, new Color(0.8f, 0.3f, 0.1f, 0f)));
        EditorUtility.SetDirty(lib);

        foreach (var path in CharacterPrefabs)
            AddMovementFX(path);

        AssetDatabase.SaveAssets();
        Debug.Log($"[FX] Built the combat and movement effects into {Root} and {LibraryPath}.");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Effects
    // ─────────────────────────────────────────────────────────────────────

    static GameObject HitSparks(Material sparkMat, Material flashMat)
    {
        // Root: hot sparks flying back from the blade (unscaled time, so they burst out during the hit stop)
        var sparks = NewSystem("FX_HitSparks", null, sparkMat, unscaled: true);
        var main = sparks.main;
        main.duration = 0.3f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.6f), new Color(1f, 0.55f, 0.15f));
        main.gravityModifier = 1.2f;
        main.maxParticles = 40;
        Burst(sparks, 18, 28);
        var shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 40f;
        shape.radius = 0.02f;
        FadeOut(sparks, new Color(1f, 0.95f, 0.8f), new Color(1f, 0.35f, 0.05f));
        var renderer = sparks.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.035f;
        renderer.lengthScale = 1.5f;

        // A few slow embers drifting off
        var embers = NewSystem("Embers", sparks.transform, sparkMat, unscaled: true);
        main = embers.main;
        main.duration = 0.3f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.035f);
        main.startColor = new Color(1f, 0.6f, 0.2f);
        main.gravityModifier = 0.15f;
        Burst(embers, 3, 6);
        shape = embers.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;
        FadeOut(embers, new Color(1f, 0.7f, 0.3f), new Color(0.9f, 0.2f, 0.02f));

        // The flash at the contact point
        var flash = NewSystem("Flash", sparks.transform, flashMat, unscaled: true);
        main = flash.main;
        main.duration = 0.1f;
        main.startLifetime = 0.08f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
        main.startColor = new Color(1f, 0.82f, 0.55f, 0.9f);
        Burst(flash, 1, 1);
        var flashShape = flash.shape;
        flashShape.enabled = false;
        FadeOut(flash, Color.white, new Color(1f, 0.6f, 0.2f));
        var size = flash.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.3f));

        var lightGo = new GameObject("Light");
        lightGo.transform.SetParent(sparks.transform, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 3f;
        light.intensity = 2.5f;
        light.color = new Color(1f, 0.6f, 0.25f);
        light.shadows = LightShadows.None;
        lightGo.AddComponent<FXLightFade>().duration = 0.12f;

        return SavePrefab(sparks.gameObject);
    }

    static GameObject Blood(Material bloodMat)
    {
        // Root: dark droplets thrown along the blow
        var drops = NewSystem("FX_BloodSpray", null, bloodMat, unscaled: false);
        var main = drops.main;
        main.duration = 0.3f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.42f, 0.03f, 0.03f), new Color(0.25f, 0.01f, 0.01f));
        main.gravityModifier = 1.6f;
        main.maxParticles = 30;
        Burst(drops, 12, 20);
        var shape = drops.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 25f;
        shape.radius = 0.03f;
        FadeOut(drops, Color.white, new Color(0.6f, 0.6f, 0.6f), fadeStart: 0.6f);
        var renderer = drops.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.02f;
        renderer.lengthScale = 1.2f;

        // A short red mist
        var mist = NewSystem("Mist", drops.transform, bloodMat, unscaled: false);
        main = mist.main;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
        main.startColor = new Color(0.32f, 0.02f, 0.02f, 0.5f);
        Burst(mist, 3, 5);
        shape = mist.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 30f;
        shape.radius = 0.05f;
        FadeOut(mist, Color.white, Color.white);
        var size = mist.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.4f));

        return SavePrefab(drops.gameObject);
    }

    static GameObject Dust(string name, Material dustMat, int burst, Vector2 size, Vector2 speed, Vector2 life, float radius, bool ring, float alpha)
    {
        var dust = NewSystem(name, null, dustMat, unscaled: false);
        var main = dust.main;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.74f, 0.64f, alpha), new Color(0.64f, 0.59f, 0.51f, alpha));
        main.gravityModifier = -0.02f; // dust hangs and rises a little
        main.maxParticles = burst * 4;
        Burst(dust, Mathf.Max(1, burst - 2), burst + 2);

        var shape = dust.shape;
        if (ring)
        {
            // Effect is played facing up, so the circle lies flat on the ground and pushes outward
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0.2f;
        }
        else
        {
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 50f;
            shape.radius = radius;
        }

        var limit = dust.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 0f;
        limit.dampen = ring ? 0.12f : 0.08f;

        var rotation = dust.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

        var grow = dust.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.4f), new Keyframe(0.4f, 1.1f), new Keyframe(1, 1.6f)));

        // Soft in, slow out
        var col = dust.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.12f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0, 1) });
        col.color = g;

        dust.GetComponent<ParticleSystemRenderer>().sortingFudge = 10f; // behind the sparks and blood

        return SavePrefab(dust.gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────

    static ParticleSystem NewSystem(string name, Transform parent, Material material, bool unscaled)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.useUnscaledTime = unscaled;
        main.stopAction = ParticleSystemStopAction.None;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    static void Burst(ParticleSystem ps, int min, int max)
    {
        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)min, (short)max) });
    }

    static void FadeOut(ParticleSystem ps, Color from, Color to, float fadeStart = 0.3f)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(from, 0), new GradientColorKey(to, 1) },
                  new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, fadeStart), new GradientAlphaKey(0, 1) });
        col.color = g;
    }

    static Gradient Gradient(params (float t, Color c)[] keys)
    {
        var g = new Gradient();
        var colors = new GradientColorKey[keys.Length];
        var alphas = new GradientAlphaKey[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            colors[i] = new GradientColorKey(keys[i].c, keys[i].t);
            alphas[i] = new GradientAlphaKey(keys[i].c.a, keys[i].t);
        }
        g.SetKeys(colors, alphas);
        return g;
    }

    static GameObject SavePrefab(GameObject go)
    {
        string path = $"{Root}/{go.name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static Texture2D Texture(string name, int width, int height, System.Func<float, float, Color> pixel)
    {
        string path = $"{TexFolder}/{name}.png";
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                tex.SetPixel(x, y, pixel((x + 0.5f) / width, (y + 0.5f) / height));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Material(string name, string shader, Texture texture)
    {
        string path = $"{MatFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find(shader);
        mat.mainTexture = texture;
        mat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f)); // neutral: the particle colours are used as they are
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void AddMovementFX(string prefabPath)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            if (root.GetComponent<Animator>() != null && root.GetComponent<MovementFX>() == null)
                root.AddComponent<MovementFX>();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
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
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
