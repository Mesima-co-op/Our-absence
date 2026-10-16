using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Generates "The Descent": a one-way valley that sinks from the Shrine of the Six down to the Ember Hollow.
///
/// Regions (by progress t along the path, 0 = start, 1 = end):
///   The Threshold     0.00 - 0.12   shrine of the six gods, bonfire, player spawn
///   The Withered Wood 0.12 - 0.45   dense dead forest
///   Field of the Fallen 0.45 - 0.65 graveyard, ruined church
///   The Narrowing     0.65 - 0.85   tight gorge
///   The Ember Hollow  0.85 - 1.00   dark basin holding the last light
///
/// Two ledges along the path can be dropped down but not climbed back up.
/// Everything is deterministic from <see cref="Seed"/>; re-running regenerates the scene.
/// </summary>
public static class TheDescentGenerator
{
    // ── Output ────────────────────────────────────────────────────────────────
    const string OutputFolder = "Assets/Scenes/World";
    const string ScenePath = OutputFolder + "/TheDescent.unity";
    const string TerrainDataPath = OutputFolder + "/TheDescent_TerrainData.asset";

    // ── World dimensions ──────────────────────────────────────────────────────
    const int Seed = 1337;
    const float WorldSize = 1024f;
    const float WorldHeight = 400f;
    const int HeightRes = 1025;
    const int FieldRes = 513;       // distance-to-path field (2 m cells)
    const int SplatRes = 512;
    const int DetailRes = 512;
    const float PathHalfWidth = 4f;

    // ── Asset locations ───────────────────────────────────────────────────────
    const string Env = "Assets/Objects/environment/";
    const string Big = Env + "BIG_Environment_Pack/Environment/";
    const string Veg = Big + "Vegetation/";
    const string Layers = Env + "BIG_Environment_Pack/Terrain/_TerrainAutoUpgrade/";

    // Path: control points (x, z) and ground height (m), north to south, always descending
    static readonly Vector3[] Control =
    {
        new Vector3(512, 230, 955),   // The Threshold - Shrine of the Six
        new Vector3(440, 214, 860),
        new Vector3(570, 190, 735),   // The Withered Wood
        new Vector3(430, 160, 600),
        new Vector3(585, 128, 470),   // Field of the Fallen
        new Vector3(470, 95, 335),    // The Narrowing
        new Vector3(545, 58, 200),
        new Vector3(512, 22, 85),     // The Ember Hollow
    };

    // One-way ledges: (t, drop in metres)
    static readonly Vector2[] Ledges = { new Vector2(0.42f, 3.5f), new Vector2(0.78f, 4f) };

    // Region keys over t, interpolated smoothly
    static readonly float[] RegionT = { 0.00f, 0.12f, 0.45f, 0.65f, 0.85f, 1.00f };
    static readonly float[] ValleyWidth = { 40f, 95f, 120f, 26f, 80f, 80f };
    static readonly float[] ForestDensity = { 0.06f, 0.75f, 0.12f, 0.05f, 0.02f, 0.02f };
    static readonly float[] RidgeDensity = { 0.35f, 0.40f, 0.25f, 0.30f, 0.15f, 0.15f };
    static readonly float[] GrassDensity = { 0.90f, 0.55f, 0.85f, 0.25f, 0.05f, 0.05f };

    // Derived path data
    static Vector2[] pathXZ;
    static float[] pathT;
    static float[] fieldDist, fieldT;
    static Vector2 noiseOffset;

    struct Flatten { public Vector2 center; public float radius; public float height; }
    static readonly List<Flatten> flattenZones = new List<Flatten>();

    [MenuItem("Our Absence/World/Generate The Descent")]
    public static void Generate()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var rng = new System.Random(Seed);
        noiseOffset = new Vector2(rng.Next(0, 10000), rng.Next(0, 10000));

        EditorUtility.DisplayProgressBar("The Descent", "Tracing the path", 0.05f);
        try
        {
            BuildPath();
            BuildDistanceField();
            DefineFlattenZones();

            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(OutputFolder).Replace('\\', '/'), Path.GetFileName(OutputFolder));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            EditorUtility.DisplayProgressBar("The Descent", "Raising the land", 0.2f);
            var data = CreateTerrainData();
            var terrainGo = Terrain.CreateTerrainGameObject(data);
            terrainGo.name = "Terrain_TheDescent";
            var terrain = terrainGo.GetComponent<Terrain>();
            ConfigureTerrain(terrain);

            EditorUtility.DisplayProgressBar("The Descent", "Painting the ground", 0.45f);
            PaintSplat(data);

            EditorUtility.DisplayProgressBar("The Descent", "Planting dead trees", 0.6f);
            PlantTrees(data, new System.Random(Seed + 1));

            EditorUtility.DisplayProgressBar("The Descent", "Growing withered grass", 0.75f);
            GrowGrass(data, new System.Random(Seed + 2));

            EditorUtility.DisplayProgressBar("The Descent", "Placing ruins and light", 0.88f);
            var world = new GameObject("World").transform;
            PlaceLandmarks(terrain, world, new System.Random(Seed + 3));
            SetupLighting(terrain);

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[The Descent] Generated {ScenePath} ({data.treeInstanceCount} trees).");

            EditorUtility.ClearProgressBar();
            TheDescentPopulator.Populate();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Path
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Path data is rebuilt on demand so other editor tools can query it without regenerating.</summary>
    internal static void EnsurePath()
    {
        if (pathXZ == null) BuildPath();
    }

    static void BuildPath()
    {
        const int perSegment = 80;
        var pts = new List<Vector2>();
        var heights = new List<float>();
        for (int i = 0; i < Control.Length - 1; i++)
        {
            Vector3 p0 = Control[Mathf.Max(i - 1, 0)], p1 = Control[i], p2 = Control[i + 1], p3 = Control[Mathf.Min(i + 2, Control.Length - 1)];
            for (int s = 0; s < perSegment; s++)
            {
                float u = s / (float)perSegment;
                pts.Add(CatmullRom(XZ(p0), XZ(p1), XZ(p2), XZ(p3), u));
                heights.Add(Mathf.Lerp(p1.y, p2.y, Mathf.SmoothStep(0, 1, u)));
            }
        }
        pts.Add(XZ(Control[Control.Length - 1]));

        pathXZ = pts.ToArray();
        pathT = new float[pathXZ.Length];
        float total = 0;
        for (int i = 1; i < pathXZ.Length; i++)
        {
            total += Vector2.Distance(pathXZ[i - 1], pathXZ[i]);
            pathT[i] = total;
        }
        for (int i = 0; i < pathT.Length; i++) pathT[i] /= total;

        // Height keyed on t via control points (linear in arc length between controls)
        controlT = new float[Control.Length];
        for (int c = 0; c < Control.Length; c++)
            controlT[c] = pathT[Mathf.Min(c * perSegment, pathT.Length - 1)];
    }

    static float[] controlT;

    static float PathHeight(float t)
    {
        float h = Control[Control.Length - 1].y;
        for (int c = 0; c < Control.Length - 1; c++)
        {
            if (t <= controlT[c + 1])
            {
                float u = Mathf.InverseLerp(controlT[c], controlT[c + 1], t);
                h = Mathf.Lerp(Control[c].y, Control[c + 1].y, Mathf.SmoothStep(0, 1, u));
                break;
            }
        }
        // One-way ledges: a sharp drop that the CharacterController cannot climb back up
        foreach (var ledge in Ledges)
            h -= ledge.y * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(ledge.x - 0.0008f, ledge.x + 0.0008f, t));
        return h;
    }

    internal static Vector3 PathPoint(float t, out Vector2 tangent)
    {
        int i = 1;
        while (i < pathT.Length - 1 && pathT[i] < t) i++;
        float u = Mathf.InverseLerp(pathT[i - 1], pathT[i], t);
        Vector2 xz = Vector2.Lerp(pathXZ[i - 1], pathXZ[i], u);
        tangent = (pathXZ[i] - pathXZ[i - 1]).normalized;
        return new Vector3(xz.x, PathHeight(t), xz.y);
    }

    static void BuildDistanceField()
    {
        fieldDist = new float[FieldRes * FieldRes];
        fieldT = new float[FieldRes * FieldRes];
        float cell = WorldSize / (FieldRes - 1);
        for (int z = 0; z < FieldRes; z++)
        {
            for (int x = 0; x < FieldRes; x++)
            {
                var p = new Vector2(x * cell, z * cell);
                float best = float.MaxValue, bestT = 0;
                for (int i = 0; i < pathXZ.Length - 1; i++)
                {
                    Vector2 a = pathXZ[i], ab = pathXZ[i + 1] - a;
                    float u = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                    float d = (a + ab * u - p).sqrMagnitude;
                    if (d < best) { best = d; bestT = Mathf.Lerp(pathT[i], pathT[i + 1], u); }
                }
                fieldDist[z * FieldRes + x] = Mathf.Sqrt(best);
                fieldT[z * FieldRes + x] = bestT;
            }
        }
    }

    /// <summary>Distance to the path and progress t at world position (x, z).</summary>
    static void SampleField(float wx, float wz, out float dist, out float t)
    {
        float fx = Mathf.Clamp(wx / WorldSize * (FieldRes - 1), 0, FieldRes - 1.001f);
        float fz = Mathf.Clamp(wz / WorldSize * (FieldRes - 1), 0, FieldRes - 1.001f);
        int x0 = (int)fx, z0 = (int)fz;
        float ux = fx - x0, uz = fz - z0;
        int i00 = z0 * FieldRes + x0, i10 = i00 + 1, i01 = i00 + FieldRes, i11 = i01 + 1;
        dist = Mathf.Lerp(Mathf.Lerp(fieldDist[i00], fieldDist[i10], ux), Mathf.Lerp(fieldDist[i01], fieldDist[i11], ux), uz);
        // t is taken from the nearest cell: interpolating it across the medial axis would blend unrelated parts of the path
        int nx = Mathf.RoundToInt(fx), nz = Mathf.RoundToInt(fz);
        t = fieldT[nz * FieldRes + nx];
    }

    static float Keyed(float[] values, float t)
    {
        for (int i = 0; i < RegionT.Length - 1; i++)
        {
            if (t <= RegionT[i + 1])
                return Mathf.Lerp(values[i], values[i + 1], Mathf.SmoothStep(0, 1, Mathf.InverseLerp(RegionT[i], RegionT[i + 1], t)));
        }
        return values[values.Length - 1];
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Heights
    // ─────────────────────────────────────────────────────────────────────────

    static void DefineFlattenZones()
    {
        flattenZones.Clear();
        AddFlatten(0.0f, Vector2.zero, 20f);                 // Shrine of the Six
        AddFlatten(0.55f, SideOffset(0.55f, 34f), 26f);       // graveyard
        AddFlatten(0.55f, SideOffset(0.55f, -58f), 22f);      // ruined church
        AddFlatten(1.0f, Vector2.zero, 24f);                 // The Ember Hollow
    }

    internal static Vector2 SideOffset(float t, float side)
    {
        PathPoint(t, out var tan);
        return new Vector2(-tan.y, tan.x) * side;
    }

    static void AddFlatten(float t, Vector2 offset, float radius)
    {
        var p = PathPoint(t, out _);
        flattenZones.Add(new Flatten { center = new Vector2(p.x, p.z) + offset, radius = radius, height = p.y });
    }

    static TerrainData CreateTerrainData()
    {
        var data = new TerrainData { heightmapResolution = HeightRes };
        data.size = new Vector3(WorldSize, WorldHeight, WorldSize);

        var heights = new float[HeightRes, HeightRes];
        float cell = WorldSize / (HeightRes - 1);
        for (int z = 0; z < HeightRes; z++)
        {
            for (int x = 0; x < HeightRes; x++)
            {
                float wx = x * cell, wz = z * cell;
                heights[z, x] = Mathf.Clamp01(GroundHeight(wx, wz) / WorldHeight);
            }
        }
        SmoothHeights(heights, 2);
        data.SetHeights(0, 0, heights);

        if (AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath) != null)
            AssetDatabase.DeleteAsset(TerrainDataPath);
        AssetDatabase.CreateAsset(data, TerrainDataPath);
        return data;
    }

    /// <summary>Box-blur passes that remove distance-field banding; one-way ledges stay sharp.</summary>
    static void SmoothHeights(float[,] h, int passes)
    {
        float cell = WorldSize / (HeightRes - 1);
        var keep = new bool[HeightRes, HeightRes];
        for (int z = 0; z < HeightRes; z++)
            for (int x = 0; x < HeightRes; x++)
            {
                SampleField(x * cell, z * cell, out _, out float t);
                foreach (var ledge in Ledges)
                    if (Mathf.Abs(t - ledge.x) < 0.0035f) keep[z, x] = true;
            }

        var tmp = new float[HeightRes, HeightRes];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int z = 0; z < HeightRes; z++)
                for (int x = 0; x < HeightRes; x++)
                {
                    if (keep[z, x]) { tmp[z, x] = h[z, x]; continue; }
                    float sum = 0; int n = 0;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int sx = x + dx, sz = z + dz;
                            if (sx < 0 || sz < 0 || sx >= HeightRes || sz >= HeightRes) continue;
                            sum += h[sz, sx]; n++;
                        }
                    tmp[z, x] = sum / n;
                }
            System.Array.Copy(tmp, h, tmp.Length);
        }
    }

    static float GroundHeight(float wx, float wz)
    {
        SampleField(wx, wz, out float d, out float t);
        float h = PathHeight(t);
        float width = Keyed(ValleyWidth, t);

        float detail = Fbm(wx, wz, 0.03f, 3) - 0.5f;   // small bumps
        float broad = Fbm(wx, wz, 0.006f, 4) - 0.5f;   // ridges and hollows

        float rise;
        if (d <= PathHalfWidth)
        {
            rise = 0f;
        }
        else if (d <= width)
        {
            float s = (d - PathHalfWidth) / Mathf.Max(width - PathHalfWidth, 1f);
            rise = 6f * s * s + detail * 3f * s + broad * 6f * s;
        }
        else
        {
            // Valley walls: the world closes in on every side
            float e = d - width;
            rise = 6f + broad * 6f + e * 1.05f + e * e * 0.004f;
            rise += broad * 50f * Mathf.Clamp01(e / 50f) + detail * 6f * Mathf.Clamp01(e / 20f);
            rise = Mathf.Min(rise, 175f + broad * 40f);
        }

        float ground = h + rise;

        foreach (var f in flattenZones)
        {
            float fd = Vector2.Distance(new Vector2(wx, wz), f.center);
            if (fd < f.radius * 1.6f)
            {
                float k = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(f.radius, f.radius * 1.6f, fd));
                ground = Mathf.Lerp(ground, f.height + detail * 0.4f, k);
            }
        }
        return ground;
    }

    static float Fbm(float x, float z, float freq, int octaves)
    {
        float sum = 0, amp = 0.5f, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            sum += Mathf.PerlinNoise(x * freq + noiseOffset.x, z * freq + noiseOffset.y) * amp;
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return sum / norm;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Terrain configuration and texturing
    // ─────────────────────────────────────────────────────────────────────────

    static void ConfigureTerrain(Terrain terrain)
    {
        terrain.heightmapPixelError = 6;
        terrain.basemapDistance = 220;
        terrain.treeDistance = 500;
        terrain.treeBillboardDistance = 300;
        terrain.treeCrossFadeLength = 20;
        terrain.treeMaximumFullLODCount = 120;
        terrain.detailObjectDistance = 90;
        terrain.detailObjectDensity = 0.85f;
        terrain.drawInstanced = true;
    }

    enum L { Grass, Moss, Leaves, Dirt, Path, Rock }

    static void PaintSplat(TerrainData data)
    {
        data.terrainLayers = new[]
        {
            LoadLayer("layer_Gras_012023054508611406"),
            LoadLayer("layer_Moos_012023054508611406"),
            LoadLayer("layer_Leaves2023054508611406"),
            LoadLayer("layer_Dirt_012023054508611406"),
            LoadLayer("layer_Raw_Dirt2023054508611406"),
            LoadLayer("layer_Rock_013d1cca0d6d0e9938"),
        };
        data.alphamapResolution = SplatRes;

        int n = data.terrainLayers.Length;
        var maps = new float[SplatRes, SplatRes, n];
        var w = new float[n];
        for (int z = 0; z < SplatRes; z++)
        {
            for (int x = 0; x < SplatRes; x++)
            {
                float nx = (x + 0.5f) / SplatRes, nz = (z + 0.5f) / SplatRes;
                float wx = nx * WorldSize, wz = nz * WorldSize;
                SampleField(wx, wz, out float d, out float t);

                float steep = data.GetSteepness(nx, nz);
                float rock = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(26f, 40f, steep));
                float edgeNoise = Fbm(wx, wz, 0.15f, 2) * 2.5f;
                float path = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(PathHalfWidth - 1f, PathHalfWidth + 1.5f + edgeNoise, d));
                float nearPath = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(PathHalfWidth, PathHalfWidth + 12f, d));

                float decay = t; // the deeper you go, the less life remains
                float patch = Fbm(wx, wz, 0.02f, 3);
                float patch2 = Fbm(wx + 300, wz - 200, 0.035f, 2);

                w[(int)L.Grass] = Mathf.Max(0.05f, (1f - decay * 0.85f) * patch);
                w[(int)L.Moss] = patch2 * (0.25f + 0.5f * decay);
                w[(int)L.Leaves] = Keyed(ForestDensity, t) * 1.6f * Mathf.SmoothStep(0, 1, patch2 + 0.2f);
                w[(int)L.Dirt] = nearPath * 0.7f + decay * 0.6f * (1f - patch);
                for (int l = 0; l < (int)L.Path; l++) w[l] *= (1f - rock) * (1f - path);
                w[(int)L.Path] = path * (1f - rock);
                w[(int)L.Rock] = rock;

                float sum = 0;
                for (int l = 0; l < n; l++) sum += w[l];
                for (int l = 0; l < n; l++) maps[z, x, l] = w[l] / Mathf.Max(sum, 1e-4f);
            }
        }
        data.SetAlphamaps(0, 0, maps);
    }

    static TerrainLayer LoadLayer(string name)
    {
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(Layers + name + ".terrainlayer");
        if (layer == null) Debug.LogError($"[The Descent] Missing terrain layer {name}");
        return layer;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Trees
    // ─────────────────────────────────────────────────────────────────────────

    static void PlantTrees(TerrainData data, System.Random rng)
    {
        string deadT = Veg + "Trees/Dead_Trees/DeadTrees/Dead_Tree_";
        string deadOak = Veg + "Trees/Dead_Trees/DeadOak/DO_Trees/Dead_Oak_";
        string dry = Veg + "Trees/Autumn_Trees/Dry_Trees/Dry_Trees/Dry_Tree_";
        string conifer = Veg + "Trees/Conifer/Conifer_Trees/Conifer_";

        var protos = new List<TreePrototype>();
        var deadIdx = AddTrees(protos, deadT, 1, 10);
        var oakIdx = AddTrees(protos, deadOak, 1, 8);
        var dryIdx = AddTrees(protos, dry, 1, 6);
        var conIdx = AddTrees(protos, conifer, 2, 12);
        data.treePrototypes = protos.ToArray();

        var trees = new List<TreeInstance>();
        const float cell = 5f;
        int cells = (int)(WorldSize / cell);
        for (int cz = 0; cz < cells; cz++)
        {
            for (int cx = 0; cx < cells; cx++)
            {
                float wx = (cx + (float)rng.NextDouble()) * cell;
                float wz = (cz + (float)rng.NextDouble()) * cell;
                float roll = (float)rng.NextDouble();
                float pick = (float)rng.NextDouble();
                float scale = 0.5f + (float)rng.NextDouble() * 0.35f;
                float rot = (float)rng.NextDouble() * Mathf.PI * 2f;

                SampleField(wx, wz, out float d, out float t);
                if (d < PathHalfWidth + 7f || InFlattenZone(wx, wz, 1.2f)) continue;

                float nx = wx / WorldSize, nz = wz / WorldSize;
                if (data.GetSteepness(nx, nz) > 33f) continue;

                float width = Keyed(ValleyWidth, t);
                bool onRidge = d > width;
                float density = onRidge
                    ? Keyed(RidgeDensity, t) * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(40f, 130f, d - width)))
                    : Keyed(ForestDensity, t) * Mathf.Lerp(0.4f, 1.3f, Fbm(wx, wz, 0.012f, 2));
                if (roll > density) continue;

                List<int> group;
                if (onRidge && (t < 0.2f || (t > 0.62f && t < 0.88f)) && pick < 0.6f) group = conIdx;
                else if (t > 0.12f && t < 0.45f) group = pick < 0.55f ? oakIdx : (pick < 0.85f ? deadIdx : dryIdx);
                else group = pick < 0.7f ? deadIdx : dryIdx;

                int proto = group[rng.Next(group.Count)];
                byte shade = (byte)(150 + rng.Next(60)); // darker, ashen tint
                trees.Add(new TreeInstance
                {
                    prototypeIndex = proto,
                    position = new Vector3(nx, 0, nz),
                    widthScale = scale,
                    heightScale = scale * (0.9f + (float)rng.NextDouble() * 0.25f),
                    rotation = rot,
                    color = new Color32(shade, shade, (byte)(shade - 10), 255),
                    lightmapColor = new Color32(255, 255, 255, 255),
                });
            }
        }
        data.SetTreeInstances(trees.ToArray(), true);
    }

    static List<int> AddTrees(List<TreePrototype> protos, string prefix, int from, int to)
    {
        var indices = new List<int>();
        for (int i = from; i <= to; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{prefix}{i:00}.prefab");
            if (prefab == null) continue;
            if (HasMissingTextures(prefab))
            {
                Debug.LogWarning($"[The Descent] Skipping {prefab.name}: its materials are missing textures (renders white).");
                continue;
            }
            indices.Add(protos.Count);
            protos.Add(new TreePrototype { prefab = prefab, bendFactor = 0.2f });
        }
        if (indices.Count == 0) Debug.LogError($"[The Descent] No tree prefabs found at {prefix}*");
        return indices;
    }

    static bool HasMissingTextures(GameObject prefab)
    {
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m == null || (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") == null)) return true;
        return false;
    }

    static bool InFlattenZone(float wx, float wz, float scale)
    {
        foreach (var f in flattenZones)
            if (Vector2.Distance(new Vector2(wx, wz), f.center) < f.radius * scale) return true;
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Grass
    // ─────────────────────────────────────────────────────────────────────────

    static void GrowGrass(TerrainData data, System.Random rng)
    {
        string tex = Veg + "Grass/Textures/";
        // Values below are blade counts per cell, not 0-255 coverage (the 2022.2+ default)
        data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        data.SetDetailResolution(DetailRes, 16);
        data.detailPrototypes = new[]
        {
            GrassProto(tex + "DryGrass_02.PSD", 0.7f, 1.3f, 0.6f, 1.2f, new Color(0.52f, 0.5f, 0.38f), new Color(0.42f, 0.36f, 0.26f)),
            GrassProto(tex + "Old_Grass.PSD", 0.6f, 1.1f, 0.5f, 1.0f, new Color(0.4f, 0.42f, 0.32f), new Color(0.35f, 0.3f, 0.22f)),
            GrassProto(tex + "DGrass_01.psd", 0.8f, 1.6f, 0.8f, 1.7f, new Color(0.36f, 0.34f, 0.28f), new Color(0.3f, 0.26f, 0.2f)),
        };

        int layers = data.detailPrototypes.Length;
        var maps = new int[layers][,];
        for (int l = 0; l < layers; l++) maps[l] = new int[DetailRes, DetailRes];

        for (int z = 0; z < DetailRes; z++)
        {
            for (int x = 0; x < DetailRes; x++)
            {
                float nx = (x + 0.5f) / DetailRes, nz = (z + 0.5f) / DetailRes;
                float wx = nx * WorldSize, wz = nz * WorldSize;
                SampleField(wx, wz, out float d, out float t);

                float steep = data.GetSteepness(nx, nz);
                float baseDensity = Keyed(GrassDensity, t)
                    * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(24f, 34f, steep)))
                    * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(PathHalfWidth + 0.5f, PathHalfWidth + 4f, d))
                    * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(40f, 90f, d - Keyed(ValleyWidth, t))));
                if (InFlattenZone(wx, wz, 0.45f)) baseDensity *= 0.2f;
                if (baseDensity <= 0.01f) continue;

                float clump = Fbm(wx, wz, 0.05f, 2);
                float clump2 = Fbm(wx - 500, wz + 250, 0.08f, 2);
                maps[0][z, x] = Mathf.RoundToInt(baseDensity * clump * 7f + (float)rng.NextDouble() * 0.5f);
                maps[1][z, x] = Mathf.RoundToInt(baseDensity * clump2 * 5f);
                maps[2][z, x] = Mathf.RoundToInt(baseDensity * (1f - t) * Mathf.Clamp01(clump * clump2 * 2.5f) * 4f);
            }
        }
        for (int l = 0; l < layers; l++) data.SetDetailLayer(0, 0, l, maps[l]);
    }

    static DetailPrototype GrassProto(string path, float minW, float maxW, float minH, float maxH, Color healthy, Color dry)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null) Debug.LogError($"[The Descent] Missing grass texture {path}");
        return new DetailPrototype
        {
            prototypeTexture = tex,
            usePrototypeMesh = false,
            renderMode = DetailRenderMode.GrassBillboard,
            minWidth = minW, maxWidth = maxW,
            minHeight = minH, maxHeight = maxH,
            healthyColor = healthy, dryColor = dry,
            noiseSpread = 0.3f,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Landmarks, props and light
    // ─────────────────────────────────────────────────────────────────────────

    static void PlaceLandmarks(Terrain terrain, Transform world, System.Random rng)
    {
        string bld = Big + "Buildings/Building Elements/";
        string rocks = Veg + "Rocks_Stones/";
        var statue1 = Load(Big + "Props/Statues/Statue_01/Prefabs/Statue_01.prefab");
        var statue2 = Load(Big + "Props/Statues/Satue_02/Prefabs/Statue_02.prefab");
        var fire = Load("Assets/Effects/Light/Enviorment/Fire.prefab");
        var campfire = Load(Env + "Campfire.prefab");
        var ruinStones = new[] { Load(bld + "Ruin_01/Prefabs/RStone_01.prefab"), Load(bld + "Ruin_01/Prefabs/RStone_02.prefab"), Load(bld + "Ruin_01/Prefabs/RStone_03.prefab") };
        var graves = new[] { Load(bld + "Graveyard/Graves_01/Prefabs/Grave_01.prefab"), Load(bld + "Graveyard/Graves_01/Prefabs/Grave_02.prefab"), Load(bld + "Graveyard/Graves_01/Prefabs/Grave_03.prefab") };
        var fence = Load(bld + "Graveyard/Grave_Fence/Prefabs/Fence.prefab");
        var church = Load(bld + "Church/Prefabs/Church_Lod_01.prefab");
        var wall = Load(bld + "C_Wall/Prefabs/C_Wall_Lod_01.prefab");
        var mossRocks = new[] { Load(rocks + "Rock_01/Prefabs/MoosRock_01.prefab"), Load(rocks + "Rock_02/Prefabs/MossRock_02.prefab"), Load(rocks + "Rock_03/Prefabs/MoosRock_03.prefab"), Load(rocks + "Rock_05/Prefabs/MossRock_05.prefab") };
        var bareRocks = new[] { Load(rocks + "Rock_03/Prefabs/Rock_03_Group_01.prefab"), Load(rocks + "Rock_04/Prefabs/Rock_04_Grp.prefab"), Load(rocks + "Rock_06/Prefabs/Rock_06.prefab"), Load(rocks + "Rock_01/Prefabs/Rock_01.prefab") };

        // ── The Threshold: Shrine of the Six ─────────────────────────────────
        var threshold = Group(world, "01_The Threshold");
        var shrineCenter = PathPoint(0f, out var startTan);
        var shrine = Group(threshold, "Shrine of the Six");
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * Mathf.PI * 2f;
            var pos = shrineCenter + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 11f;
            var statue = Spawn(i % 2 == 0 ? statue1 : statue2, terrain, pos, shrine, $"God {i + 1}");
            if (statue) statue.transform.LookAt(new Vector3(shrineCenter.x, statue.transform.position.y, shrineCenter.z));
        }
        for (int i = 0; i < 7; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            Spawn(ruinStones[rng.Next(ruinStones.Length)], terrain, shrineCenter + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (14f + (float)rng.NextDouble() * 4f), shrine, null, rng);
        }
        Spawn(campfire, terrain, shrineCenter, shrine, "Bonfire");
        Spawn(fire, terrain, shrineCenter, shrine, "Bonfire Flame");
        AddLight(shrine, "Bonfire Light", terrain, shrineCenter + Vector3.up * 1.5f, new Color(1f, 0.55f, 0.25f), 18f, 1.8f);

        var spawnPoint = PathPoint(0.025f, out var spawnTan);
        var spawn = new GameObject("PlayerSpawn").transform;
        spawn.SetParent(threshold);
        spawn.position = OnGround(terrain, spawnPoint) + Vector3.up * 0.1f;
        spawn.rotation = Quaternion.LookRotation(new Vector3(spawnTan.x, 0, spawnTan.y));

        // ── The Withered Wood ───────────────────────────────────────────────
        var wood = Group(world, "02_The Withered Wood");
        ScatterAlongPath(terrain, wood, mossRocks, rng, 0.12f, 0.45f, 45, 7f, 45f);
        var fallenGod = PathPoint(0.3f, out _);
        var fallen = Spawn(statue2, terrain, fallenGod + (Vector3)ToXZ(SideOffset(0.3f, 14f)), wood, "Fallen God");
        if (fallen) fallen.transform.rotation = Quaternion.Euler(78f, 35f, 0f);

        // ── Field of the Fallen ─────────────────────────────────────────────
        var field = Group(world, "03_Field of the Fallen");
        var yard = PathPoint(0.55f, out var yardTan);
        var yardCenter = yard + (Vector3)ToXZ(SideOffset(0.55f, 34f));
        var yardRot = Quaternion.LookRotation(new Vector3(yardTan.x, 0, yardTan.y));
        var graveyard = Group(field, "Graveyard");
        for (int row = 0; row < 6; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                if (rng.NextDouble() < 0.18) continue; // gaps where the dead were never buried
                var local = new Vector3((col - 3.5f) * 3.2f + (float)rng.NextDouble() * 0.6f, 0, (row - 2.5f) * 3.8f);
                var g = Spawn(graves[rng.Next(graves.Length)], terrain, yardCenter + yardRot * local, graveyard, null);
                if (g) g.transform.rotation = yardRot * Quaternion.Euler(Rand(rng, -6, 6), Rand(rng, -12, 12), Rand(rng, -8, 8));
            }
        }
        for (int i = -3; i <= 3; i++)
        {
            if (i == 0) continue; // gate gap facing the path
            var f = Spawn(fence, terrain, yardCenter + yardRot * new Vector3(i * 4f, 0, -13f), graveyard, null);
            if (f) f.transform.rotation = yardRot * Quaternion.Euler(0, 0, Rand(rng, -5, 5));
        }
        var churchPos = yard + (Vector3)ToXZ(SideOffset(0.55f, -58f));
        var ruinedChurch = Spawn(church, terrain, churchPos, field, "Ruined Church");
        if (ruinedChurch) ruinedChurch.transform.rotation = Quaternion.LookRotation(yard - churchPos, Vector3.up) * Quaternion.Euler(0, 90, 0);
        for (int i = 0; i < 6; i++)
        {
            var wp = churchPos + new Vector3(Rand(rng, -26, 26), 0, Rand(rng, -26, 26));
            var w = Spawn(wall, terrain, wp, field, null);
            if (w) w.transform.rotation = Quaternion.Euler(0, Rand(rng, 0, 360), Rand(rng, -4, 4));
        }
        ScatterAlongPath(terrain, field, ruinStones, rng, 0.47f, 0.64f, 25, 6f, 40f);

        // ── The Narrowing ───────────────────────────────────────────────────
        var narrowing = Group(world, "04_The Narrowing");
        ScatterAlongPath(terrain, narrowing, bareRocks, rng, 0.65f, 0.85f, 40, 5f, 18f);

        // ── The Ember Hollow ────────────────────────────────────────────────
        var hollow = Group(world, "05_The Ember Hollow");
        var ember = PathPoint(1f, out _);
        Spawn(statue1, terrain, ember + Vector3.right * 3f, hollow, "Keeper of the Last Light");
        Spawn(fire, terrain, ember, hollow, "The Last Ember");
        AddLight(hollow, "Last Ember Light", terrain, ember + Vector3.up * 2.5f, new Color(1f, 0.62f, 0.3f), 40f, 3f);
        ScatterAlongPath(terrain, hollow, bareRocks, rng, 0.88f, 1f, 12, 8f, 30f);

        // ── Fragments of light marking the way ──────────────────────────────
        var fragments = Group(world, "Light Fragments");
        int index = 1;
        for (float t = 0.08f; t < 0.95f; t += 0.075f, index++)
        {
            var p = PathPoint(t, out _);
            var side = (Vector3)ToXZ(SideOffset(t, (index % 2 == 0 ? 1 : -1) * (PathHalfWidth + 2f)));
            var stone = Spawn(ruinStones[index % ruinStones.Length], terrain, p + side, fragments, $"Fragment {index:00}");
            // light fades as the descent deepens
            float strength = Mathf.Lerp(1.4f, 0.6f, t);
            AddLight(stone ? stone.transform : fragments, "Spark", terrain, p + side + Vector3.up * 1.2f, new Color(0.95f, 0.8f, 0.55f), 7f, strength);
        }
    }

    static void ScatterAlongPath(Terrain terrain, Transform parent, GameObject[] prefabs, System.Random rng,
                                 float t0, float t1, int count, float minSide, float maxSide)
    {
        for (int i = 0; i < count; i++)
        {
            float t = Mathf.Lerp(t0, t1, (float)rng.NextDouble());
            float side = Mathf.Lerp(minSide, maxSide, (float)rng.NextDouble()) * (rng.NextDouble() < 0.5 ? -1 : 1);
            var p = PathPoint(t, out _) + (Vector3)ToXZ(SideOffset(t, side));
            if (InFlattenZone(p.x, p.z, 1f)) continue;
            Spawn(prefabs[rng.Next(prefabs.Length)], terrain, p, parent, null, rng);
        }
    }

    static GameObject Spawn(GameObject prefab, Terrain terrain, Vector3 pos, Transform parent, string name, System.Random randomize = null)
    {
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
        go.transform.SetParent(parent, false);
        go.transform.position = OnGround(terrain, pos) - Vector3.up * 0.05f;
        if (randomize != null)
        {
            go.transform.rotation = Quaternion.Euler(0, Rand(randomize, 0, 360), 0);
            go.transform.localScale *= Rand(randomize, 0.8f, 1.3f);
        }
        if (name != null) go.name = name;
        return go;
    }

    static void AddLight(Transform parent, string name, Terrain terrain, Vector3 pos, Color color, float range, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        float ground = OnGround(terrain, pos).y;
        go.transform.position = new Vector3(pos.x, Mathf.Max(pos.y, ground + 1f), pos.z);
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
    }

    static void SetupLighting(Terrain terrain)
    {
        var fogColor = new Color(0.075f, 0.08f, 0.095f);

        var moon = new GameObject("Moonlight").AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.55f, 0.6f, 0.75f);
        moon.intensity = 0.55f;
        moon.shadows = LightShadows.Soft;
        moon.shadowStrength = 0.85f;
        moon.transform.rotation = Quaternion.Euler(28f, -35f, 0f);
        RenderSettings.sun = moon;

        RenderSettings.skybox = CreateNightSky();
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.17f, 0.18f, 0.22f);
        RenderSettings.ambientEquatorColor = new Color(0.1f, 0.1f, 0.11f);
        RenderSettings.ambientGroundColor = new Color(0.04f, 0.04f, 0.04f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = 0.007f;

        // Preview camera at the spawn, looking down the path (replace with the player rig when integrating)
        var spawnPos = PathPoint(0.02f, out _);
        var lookAt = PathPoint(0.09f, out _);
        var cam = new GameObject("Main Camera").AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.farClipPlane = 1200f;
        cam.transform.position = OnGround(terrain, spawnPos) + Vector3.up * 6f;
        cam.transform.LookAt(OnGround(terrain, lookAt) + Vector3.up * 2f);
        cam.gameObject.AddComponent<AudioListener>();
    }

    const string SkyPath = OutputFolder + "/TheDescent_NightSky.mat";

    /// <summary>A starless, overcast night: no sun disc, low exposure, tinted to match the fog.</summary>
    static Material CreateNightSky()
    {
        var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
        if (sky == null)
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            AssetDatabase.CreateAsset(sky, SkyPath);
        }
        sky.SetFloat("_SunDisk", 0f);
        sky.SetFloat("_SunSize", 0f);
        sky.SetFloat("_AtmosphereThickness", 0.4f);
        sky.SetColor("_SkyTint", new Color(0.3f, 0.32f, 0.38f));
        sky.SetColor("_GroundColor", new Color(0.05f, 0.05f, 0.06f));
        sky.SetFloat("_Exposure", 0.22f);
        EditorUtility.SetDirty(sky);
        return sky;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    static Transform Group(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static GameObject Load(string path)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) Debug.LogWarning($"[The Descent] Missing prefab {path}");
        return go;
    }

    static Vector3 OnGround(Terrain terrain, Vector3 p) => new Vector3(p.x, terrain.SampleHeight(p) + terrain.transform.position.y, p.z);
    static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
    static Vector3 ToXZ(Vector2 v) => new Vector3(v.x, 0, v.y);
    static float Rand(System.Random r, float min, float max) => min + (float)r.NextDouble() * (max - min);
    static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }
}
