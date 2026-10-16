using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A fading swoosh that follows the blade of a swinging weapon.
/// Added to a held weapon by WeaponDamageSystem; <see cref="Emitting"/> is on while its wielder attacks.
/// The blade is found from the weapon's meshes: from the grip (the weapon's pivot) to the far end of its longest axis.
/// </summary>
public class WeaponTrail : MonoBehaviour
{
    [Tooltip("Seconds a part of the swoosh stays visible")]
    public float lifetime = 0.14f;
    [Tooltip("Where along the blade the swoosh starts (0 = grip, 1 = tip)")]
    [Range(0f, 1f)] public float bladeStart = 0.3f;

    public bool Emitting { get; set; }

    struct Sample { public Vector3 a, b; public float time; }

    readonly List<Sample> samples = new List<Sample>();
    Vector3 baseLocal, tipLocal;
    Mesh mesh;
    GameObject trailObject;
    Gradient color;
    readonly List<Vector3> verts = new List<Vector3>();
    readonly List<Color> colors = new List<Color>();
    readonly List<Vector2> uvs = new List<Vector2>();
    readonly List<int> tris = new List<int>();

    void Start()
    {
        var lib = FXLibrary.Instance;
        if (lib == null || lib.weaponTrail == null || !FindBlade()) { enabled = false; return; }
        color = lib.weaponTrailColor;

        // The mesh lives in world space, so it isn't parented to the moving weapon
        trailObject = new GameObject($"{name} Trail");
        mesh = new Mesh { name = "Weapon Trail" };
        mesh.MarkDynamic();
        trailObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = trailObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = lib.weaponTrail;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    void OnDestroy()
    {
        if (trailObject != null) Destroy(trailObject);
        if (mesh != null) Destroy(mesh);
    }

    void OnDisable()
    {
        samples.Clear();
        if (mesh != null) mesh.Clear();
    }

    bool FindBlade()
    {
        // Longest axis of all meshes, in the weapon's own space
        Bounds bounds = default;
        bool any = false;
        foreach (var filter in GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            var m = filter.sharedMesh.bounds;
            var toLocal = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var corner = m.center + Vector3.Scale(m.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = toLocal.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        if (!any) return false;

        int axis = bounds.size.x > bounds.size.y ? (bounds.size.x > bounds.size.z ? 0 : 2) : (bounds.size.y > bounds.size.z ? 1 : 2);
        Vector3 dir = Vector3.zero; dir[axis] = 1f;
        // The tip is the end of that axis furthest from the grip
        Vector3 min = bounds.center, max = bounds.center;
        min[axis] = bounds.min[axis];
        max[axis] = bounds.max[axis];
        tipLocal = max.sqrMagnitude >= min.sqrMagnitude ? max : min;
        Vector3 gripLocal = max.sqrMagnitude >= min.sqrMagnitude ? min : max;
        baseLocal = Vector3.Lerp(gripLocal, tipLocal, bladeStart);
        return true;
    }

    void LateUpdate()
    {
        float now = Time.time;
        if (Emitting)
            samples.Add(new Sample { a = transform.TransformPoint(baseLocal), b = transform.TransformPoint(tipLocal), time = now });
        samples.RemoveAll(s => now - s.time > lifetime);
        Rebuild(now);
    }

    void Rebuild(float now)
    {
        mesh.Clear();
        if (samples.Count < 2) return;

        verts.Clear(); colors.Clear(); uvs.Clear(); tris.Clear();
        for (int i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            float age = Mathf.Clamp01((now - s.time) / lifetime);
            var c = color != null ? color.Evaluate(age) : Color.white;
            verts.Add(s.a); verts.Add(s.b);
            colors.Add(new Color(c.r, c.g, c.b, 0f)); // fades toward the grip
            colors.Add(c);
            float u = i / (float)(samples.Count - 1);
            uvs.Add(new Vector2(u, 0)); uvs.Add(new Vector2(u, 1));
            if (i > 0)
            {
                int k = i * 2;
                tris.Add(k - 2); tris.Add(k - 1); tris.Add(k);
                tris.Add(k); tris.Add(k - 1); tris.Add(k + 1);
            }
        }
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }
}
