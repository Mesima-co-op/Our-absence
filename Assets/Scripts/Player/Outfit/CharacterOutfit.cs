using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dresses a character built on the Quaternius base skeleton.
///
/// - Wear(piece) puts on clothing: its skinned meshes are bound to this character's bones (by
///   bone name) so they follow every animation. Anything already in that slot comes off.
/// - The naked body hides the skin that clothing lies over (limited to the BodyRegion flags on
///   each piece), so skin never pokes through and uncovered skin stays visible.
/// - Listens to the Equipment menu: equipping an armour item whose InventoryItem has a Wearable
///   puts it on, unequipping takes it off. Loading a save re-equips, so outfits persist.
/// </summary>
[DisallowMultipleComponent]
public class CharacterOutfit : MonoBehaviour
{
    [Tooltip("The naked body mesh (hidden where clothing covers it)")]
    public SkinnedMeshRenderer body;

    [Tooltip("Hair meshes, hidden by hoods and helmets")]
    public List<SkinnedMeshRenderer> hair = new List<SkinnedMeshRenderer>();

    [Tooltip("Clothes put on when the character spawns (before any save is loaded)")]
    public List<OutfitPiece> startingOutfit = new List<OutfitPiece>();

    [Tooltip("React to the Equipment menu (turn off for NPCs)")]
    public bool followEquipmentMenu = true;

    private readonly Dictionary<OutfitSlot, OutfitPiece> worn = new Dictionary<OutfitSlot, OutfitPiece>();
    private readonly Dictionary<OutfitSlot, List<GameObject>> wornObjects = new Dictionary<OutfitSlot, List<GameObject>>();
    private Dictionary<string, Transform> bonesByName;
    private Mesh originalBodyMesh;
    private Mesh maskedBodyMesh;
    private BodyRegion[] vertexRegions;


    public IReadOnlyDictionary<OutfitSlot, OutfitPiece> Worn => worn;

    private void Awake()
    {
        if (body != null) originalBodyMesh = body.sharedMesh;
        foreach (var piece in startingOutfit)
            if (piece != null) Wear(piece);
        RefreshBody();
    }

    private void OnEnable()
    {
        if (followEquipmentMenu) EquipmentControl.OnEquipmentChanged += HandleEquipmentChanged;
    }

    private void OnDisable()
    {
        EquipmentControl.OnEquipmentChanged -= HandleEquipmentChanged;
    }

    private void OnDestroy()
    {
        if (maskedBodyMesh != null) Destroy(maskedBodyMesh);
    }

    private void HandleEquipmentChanged(string slotName, ItemDataInstance item, bool equipped)
    {
        var piece = item != null && item.Definition != null ? item.Definition.Wearable : null;
        if (piece == null) return;
        if (equipped) Wear(piece);
        else if (IsWearing(piece)) TakeOff(piece.slot);
    }

    // ── Public API ────────────────────────────────────────────────────────

    public bool IsWearing(OutfitPiece piece) => piece != null && worn.TryGetValue(piece.slot, out var p) && p == piece;

    public void Wear(OutfitPiece piece)
    {
        if (piece == null) return;
        RemoveObjects(piece.slot);
        worn[piece.slot] = piece;

        var spawned = new List<GameObject>();
        foreach (var model in piece.models)
        {
            if (model == null) continue;
            foreach (var source in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                spawned.Add(BindToSkeleton(source, $"{piece.slot}_{source.name}"));
        }
        wornObjects[piece.slot] = spawned;
        RefreshBody();
    }

    public void TakeOff(OutfitSlot slot)
    {
        RemoveObjects(slot);
        worn.Remove(slot);
        RefreshBody();
    }

    public void TakeOffAll()
    {
        foreach (var slot in new List<OutfitSlot>(worn.Keys)) RemoveObjects(slot);
        worn.Clear();
        RefreshBody();
    }

    // ── Binding clothing to the skeleton ──────────────────────────────────

    /// <summary>A copy of a clothing mesh renderer that is skinned to this character's bones.</summary>
    private GameObject BindToSkeleton(SkinnedMeshRenderer source, string objectName)
    {
        CacheBones();
        var go = new GameObject(objectName);
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);

        var smr = go.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = source.sharedMesh;
        smr.sharedMaterials = source.sharedMaterials;
        smr.shadowCastingMode = source.shadowCastingMode;
        smr.updateWhenOffscreen = false;

        var bones = new Transform[source.bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            var src = source.bones[i];
            if (src != null && bonesByName.TryGetValue(src.name, out var bone)) bones[i] = bone;
            else Debug.LogWarning($"[Outfit] {source.name}: bone '{src?.name}' not found on {name}", this);
        }
        smr.bones = bones;
        if (source.rootBone != null && bonesByName.TryGetValue(source.rootBone.name, out var root))
            smr.rootBone = root;
        else if (body != null)
            smr.rootBone = body.rootBone;

        // Use the body's bounds so clothing isn't culled while its bones animate
        if (body != null) smr.localBounds = body.localBounds;
        return go;
    }

    private void CacheBones()
    {
        if (bonesByName != null) return;
        bonesByName = new Dictionary<string, Transform>();
        Transform skeletonRoot = transform;
        if (body != null && body.rootBone != null)
            skeletonRoot = body.rootBone.parent != null ? body.rootBone.parent : body.rootBone;
        foreach (var t in skeletonRoot.GetComponentsInChildren<Transform>(true))
            if (!bonesByName.ContainsKey(t.name)) bonesByName.Add(t.name, t);
    }

    private void RemoveObjects(OutfitSlot slot)
    {
        if (!wornObjects.TryGetValue(slot, out var objects)) return;
        foreach (var go in objects)
            if (go != null) Destroy(go);
        wornObjects.Remove(slot);
    }


    // ── Hiding the covered body parts ─────────────────────────────────────
    //
    // A body vertex is hidden when a clothing triangle lies right over it (within a few cm, measured
    // in the body's bind pose). So short sleeves or 3/4 trousers leave exactly the uncovered skin
    // visible. The piece's "hides" regions limit which body parts it may hide at all (a hood never
    // hides the face).

    const float LateralTolerance = 0.015f; // how far past a clothing edge skin still counts as covered
    const float NormalTolerance = 0.09f;   // skin this far inside, or poking out of, the cloth is hidden

    /// <summary>Per piece: which body vertices it covers (computed once per body mesh).</summary>
    private static readonly Dictionary<(int piece, int mesh), bool[]> coverageCache = new Dictionary<(int, int), bool[]>();

    private void RefreshBody()
    {
        BodyRegion hidden = BodyRegion.None;
        foreach (var piece in worn.Values) hidden |= piece.hides;

        foreach (var h in hair)
            if (h != null) h.enabled = (hidden & BodyRegion.Hair) == 0;

        if (body == null || originalBodyMesh == null) return;
        if ((hidden & ~BodyRegion.Hair) == BodyRegion.None)
        {
            body.sharedMesh = originalBodyMesh;
            return;
        }
        if (!originalBodyMesh.isReadable)
        {
            Debug.LogWarning($"[Outfit] {originalBodyMesh.name} needs Read/Write enabled to hide covered body parts.", this);
            return;
        }

        vertexRegions ??= ClassifyVertices(originalBodyMesh, body.bones);
        var covered = new bool[originalBodyMesh.vertexCount];
        foreach (var piece in worn.Values)
        {
            if ((piece.hides & ~BodyRegion.Hair) == BodyRegion.None) continue;
            var mask = Coverage(piece);
            for (int v = 0; v < covered.Length; v++)
                if (mask[v] && (vertexRegions[v] & piece.hides) != 0) covered[v] = true;
        }

        if (maskedBodyMesh == null)
        {
            maskedBodyMesh = Instantiate(originalBodyMesh);
            maskedBodyMesh.name = originalBodyMesh.name + " (dressed)";
        }
        for (int sub = 0; sub < originalBodyMesh.subMeshCount; sub++)
        {
            int[] tris = originalBodyMesh.GetTriangles(sub);
            var kept = new List<int>(tris.Length);
            for (int i = 0; i < tris.Length; i += 3)
            {
                // A triangle is hidden when at least two of its corners are covered
                int n = (covered[tris[i]] ? 1 : 0) + (covered[tris[i + 1]] ? 1 : 0) + (covered[tris[i + 2]] ? 1 : 0);
                if (n < 2)
                {
                    kept.Add(tris[i]);
                    kept.Add(tris[i + 1]);
                    kept.Add(tris[i + 2]);
                }
            }
            maskedBodyMesh.SetTriangles(kept, sub, false);
        }
        body.sharedMesh = maskedBodyMesh;
    }

    private bool[] Coverage(OutfitPiece piece)
    {
        var key = (piece.GetInstanceID(), originalBodyMesh.GetInstanceID());
        if (coverageCache.TryGetValue(key, out var cached)) return cached;

        var covered = new bool[originalBodyMesh.vertexCount];
        var bodyVerts = originalBodyMesh.vertices;
        var bodyBind = originalBodyMesh.bindposes;
        var bodyBoneIndex = new Dictionary<string, int>();
        for (int i = 0; i < body.bones.Length; i++)
            if (body.bones[i] != null && !bodyBoneIndex.ContainsKey(body.bones[i].name)) bodyBoneIndex.Add(body.bones[i].name, i);

        foreach (var model in piece.models)
        {
            if (model == null) continue;
            foreach (var cloth in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = cloth.sharedMesh;
                if (mesh == null || !mesh.isReadable)
                {
                    Debug.LogWarning($"[Outfit] {cloth.name} needs Read/Write enabled to hide the skin under it.", this);
                    continue;
                }
                var clothVerts = InBodyBindPose(mesh, cloth.bones, bodyBind, bodyBoneIndex);
                MarkCovered(clothVerts, mesh.triangles, bodyVerts, covered);
            }
        }
        coverageCache[key] = covered;
        return covered;
    }

    /// <summary>
    /// Clothing vertices moved onto the body's skeleton in its bind pose, in the body mesh's space
    /// (the clothes were modelled on a slightly different skeleton).
    /// </summary>
    private static Vector3[] InBodyBindPose(Mesh mesh, Transform[] clothBones, Matrix4x4[] bodyBind, Dictionary<string, int> bodyBoneIndex)
    {
        var verts = mesh.vertices;
        var weights = mesh.boneWeights;
        var clothBind = mesh.bindposes;
        var toBody = new Matrix4x4[clothBind.Length];
        for (int i = 0; i < clothBind.Length; i++)
        {
            string bone = i < clothBones.Length && clothBones[i] != null ? clothBones[i].name : null;
            toBody[i] = bone != null && bodyBoneIndex.TryGetValue(bone, out int j)
                ? bodyBind[j].inverse * clothBind[i]
                : Matrix4x4.identity;
        }

        var result = new Vector3[verts.Length];
        for (int v = 0; v < verts.Length; v++)
        {
            if (weights.Length <= v || toBody.Length == 0) { result[v] = verts[v]; continue; }
            var w = weights[v];
            result[v] = toBody[w.boneIndex0].MultiplyPoint3x4(verts[v]) * w.weight0
                      + toBody[w.boneIndex1].MultiplyPoint3x4(verts[v]) * w.weight1
                      + toBody[w.boneIndex2].MultiplyPoint3x4(verts[v]) * w.weight2
                      + toBody[w.boneIndex3].MultiplyPoint3x4(verts[v]) * w.weight3;
        }
        return result;
    }

    /// <summary>Marks body vertices that lie right under (or poke through) a clothing triangle.</summary>
    private static void MarkCovered(Vector3[] clothVerts, int[] clothTris, Vector3[] bodyVerts, bool[] covered)
    {
        const float cell = 0.1f;
        var grid = new Dictionary<Vector3Int, List<int>>();
        Vector3Int Cell(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));

        for (int t = 0; t < clothTris.Length; t += 3)
        {
            Vector3 a = clothVerts[clothTris[t]], b = clothVerts[clothTris[t + 1]], c = clothVerts[clothTris[t + 2]];
            var min = Cell(Vector3.Min(a, Vector3.Min(b, c)) - Vector3.one * NormalTolerance);
            var max = Cell(Vector3.Max(a, Vector3.Max(b, c)) + Vector3.one * NormalTolerance);
            for (int x = min.x; x <= max.x; x++)
                for (int y = min.y; y <= max.y; y++)
                    for (int z = min.z; z <= max.z; z++)
                    {
                        var key = new Vector3Int(x, y, z);
                        if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                        list.Add(t);
                    }
        }

        for (int v = 0; v < bodyVerts.Length; v++)
        {
            if (covered[v] || !grid.TryGetValue(Cell(bodyVerts[v]), out var tris)) continue;
            Vector3 p = bodyVerts[v];
            foreach (int t in tris)
            {
                Vector3 a = clothVerts[clothTris[t]], b = clothVerts[clothTris[t + 1]], c = clothVerts[clothTris[t + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-12f) continue;
                normal.Normalize();
                Vector3 d = p - ClosestPointOnTriangle(p, a, b, c);
                float along = Vector3.Dot(d, normal);
                if (Mathf.Abs(along) > NormalTolerance) continue;
                if ((d - normal * along).sqrMagnitude > LateralTolerance * LateralTolerance) continue;
                covered[v] = true;
                break;
            }
        }
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        // Ericson, Real-Time Collision Detection 5.1.5
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    /// <summary>Which body region each vertex belongs to, from the bone that moves it most.</summary>
    private static BodyRegion[] ClassifyVertices(Mesh mesh, Transform[] bones)
    {
        var weights = mesh.boneWeights;
        var regions = new BodyRegion[mesh.vertexCount];
        for (int v = 0; v < regions.Length; v++)
        {
            int boneIndex = weights.Length > v ? weights[v].boneIndex0 : -1;
            string bone = boneIndex >= 0 && boneIndex < bones.Length && bones[boneIndex] != null ? bones[boneIndex].name : "";
            regions[v] = RegionForBone(bone);
        }
        return regions;
    }

    private static BodyRegion RegionForBone(string bone)
    {
        bone = bone.ToLowerInvariant();
        if (bone.StartsWith("head") || bone.StartsWith("neck")) return BodyRegion.Head;
        if (bone.StartsWith("spine") || bone.StartsWith("clavicle")) return BodyRegion.Torso;
        if (bone.StartsWith("pelvis") || bone == "root") return BodyRegion.Hips;
        if (bone.StartsWith("upperarm") || bone.StartsWith("lowerarm") || bone.StartsWith("hand") ||
            bone.StartsWith("thumb") || bone.StartsWith("index") || bone.StartsWith("middle") ||
            bone.StartsWith("ring") || bone.StartsWith("pinky")) return BodyRegion.Arms;
        if (bone.StartsWith("thigh") || bone.StartsWith("calf")) return BodyRegion.Legs;
        if (bone.StartsWith("foot") || bone.StartsWith("ball")) return BodyRegion.Feet;
        return BodyRegion.None;
    }
}
