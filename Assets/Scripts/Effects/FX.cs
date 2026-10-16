using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays one-shot particle effects from a pool, so hits and footsteps don't allocate.
/// Effect prefabs are non-looping particle systems (children allowed); one is reused once all its particles have died.
/// </summary>
public static class FX
{
    const int MaxPerPrefab = 24;

    static readonly Dictionary<GameObject, List<ParticleSystem>> pools = new Dictionary<GameObject, List<ParticleSystem>>();
    static Transform poolRoot;

    /// <summary>Plays the effect at a point, facing <paramref name="forward"/> (+Z of the prefab), scaled uniformly.</summary>
    public static ParticleSystem Play(GameObject prefab, Vector3 position, Vector3 forward, float scale = 1f)
    {
        if (prefab == null || scale <= 0f) return null;

        var ps = Get(prefab);
        if (ps == null) return null;

        var t = ps.transform;
        t.SetPositionAndRotation(position, forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward) : Quaternion.identity);
        t.localScale = Vector3.one * scale;
        // Re-enable so helpers like FXLightFade restart
        ps.gameObject.SetActive(false);
        ps.gameObject.SetActive(true);
        ps.Clear(true);
        ps.Play(true);
        return ps;
    }

    public static ParticleSystem Play(GameObject prefab, Vector3 position, float scale = 1f) =>
        Play(prefab, position, Vector3.up, scale);

    static ParticleSystem Get(GameObject prefab)
    {
        if (poolRoot == null)
        {
            poolRoot = new GameObject("[FX Pool]").transform;
            Object.DontDestroyOnLoad(poolRoot.gameObject);
            pools.Clear(); // the old pool died with its root (scene reload in the editor)
        }

        if (!pools.TryGetValue(prefab, out var list))
            pools[prefab] = list = new List<ParticleSystem>();

        ParticleSystem oldest = null;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var ps = list[i];
            if (ps == null) { list.RemoveAt(i); continue; }
            if (!ps.gameObject.activeSelf || !ps.IsAlive(true)) return ps;
            oldest = ps;
        }

        if (list.Count >= MaxPerPrefab)
            return oldest; // busy: restart the oldest rather than pile up more

        var go = Object.Instantiate(prefab, poolRoot);
        go.name = prefab.name;
        var created = go.GetComponent<ParticleSystem>();
        if (created == null)
        {
            Debug.LogWarning($"[FX] {prefab.name} has no ParticleSystem on its root.", prefab);
            Object.Destroy(go);
            return null;
        }
        list.Add(created);
        return created;
    }
}
