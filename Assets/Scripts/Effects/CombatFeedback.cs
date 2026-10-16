using System.Collections;
using Cinemachine;
using UnityEngine;

/// <summary>
/// What a hit feels like: sparks and blood at the contact point, a few freeze frames (hit stop)
/// and a camera kick. Called by the weapon scripts (WeaponDamageSystem, WeaponHitbox) and HealthSystem.
/// Strengths and effect prefabs come from the FXLibrary asset.
/// </summary>
public static class CombatFeedback
{
    /// <summary>A weapon connected.</summary>
    /// <param name="point">Contact point on the victim</param>
    /// <param name="direction">Direction of the blow (attacker to victim)</param>
    /// <param name="victimIsPlayer">True when the player got hit (stronger shake and freeze)</param>
    public static void Hit(Vector3 point, Vector3 direction, bool victimIsPlayer)
    {
        var lib = FXLibrary.Instance;
        if (lib == null) return;

        direction.y = Mathf.Max(direction.y, 0f);
        if (direction.sqrMagnitude < 1e-4f) direction = Vector3.up;
        direction.Normalize();

        // Sparks fly back toward the attacker, blood sprays out along the blow
        FX.Play(lib.hitSparks, point, -direction + Vector3.up * 0.3f);
        FX.Play(lib.bloodSpray, point, direction + Vector3.up * 0.25f);

        HitStop(victimIsPlayer ? lib.hitStopTaken : lib.hitStopDealt, lib.hitStopTimeScale);
        Shake(point, direction, victimIsPlayer ? lib.shakeTaken : lib.shakeDealt);
    }

    /// <summary>A character died: a burst of dust where it fell.</summary>
    public static void Death(Vector3 position)
    {
        var lib = FXLibrary.Instance;
        if (lib != null) FX.Play(lib.deathDust, position);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Hit stop
    // ─────────────────────────────────────────────────────────────────────

    static Runner runner;
    static float hitStopUntil;

    /// <summary>Slows time almost to a stop for a moment. Overlapping calls extend it; it never stacks.</summary>
    public static void HitStop(float seconds, float timeScale)
    {
        if (seconds <= 0f) return;
        // Respect pauses and slow-motion set by something else
        if (hitStopUntil <= Time.unscaledTime && !Mathf.Approximately(Time.timeScale, 1f)) return;

        hitStopUntil = Mathf.Max(hitStopUntil, Time.unscaledTime + seconds);
        Time.timeScale = timeScale;
        EnsureRunner().Restore();
    }

    static Runner EnsureRunner()
    {
        if (runner == null)
        {
            var go = new GameObject("[Combat Feedback]") { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
        }
        return runner;
    }

    class Runner : MonoBehaviour
    {
        Coroutine restore;

        public void Restore()
        {
            if (restore == null) restore = StartCoroutine(RestoreTime());
        }

        IEnumerator RestoreTime()
        {
            while (Time.unscaledTime < hitStopUntil)
                yield return null;
            Time.timeScale = 1f;
            restore = null;
        }

        void OnDisable()
        {
            // Never leave the game frozen (scene change, quitting play mode)
            if (restore != null) Time.timeScale = 1f;
            restore = null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Camera shake
    // ─────────────────────────────────────────────────────────────────────

    static CinemachineImpulseSource impulse;

    /// <summary>Kicks every Cinemachine camera in the direction of the blow.</summary>
    public static void Shake(Vector3 position, Vector3 direction, float strength)
    {
        if (strength <= 0f) return;
        if (impulse == null)
        {
            impulse = EnsureRunner().gameObject.AddComponent<CinemachineImpulseSource>();
            var def = impulse.m_ImpulseDefinition;
            def.m_ImpulseChannel = 1;
            def.m_ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            def.m_ImpulseDuration = 0.18f;
            def.m_ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
        }
        AddListeners();

        Vector3 kick = (direction.normalized + Random.insideUnitSphere * 0.4f).normalized * strength;
        impulse.transform.position = position;
        impulse.GenerateImpulseAtPositionWithVelocity(position, kick);
    }

    /// <summary>Cameras only feel impulses with a listener; add one to every virtual camera that lacks it.</summary>
    static void AddListeners()
    {
        foreach (var cam in Object.FindObjectsOfType<CinemachineVirtualCamera>())
        {
            if (cam.GetComponent<CinemachineImpulseListener>() != null) continue;
            var listener = cam.gameObject.AddComponent<CinemachineImpulseListener>();
            listener.m_ChannelMask = 1;
            listener.m_Gain = 1f;
            listener.m_Use2DDistance = false;
        }
    }

    /// <summary>A landing or heavy step: a small vertical camera bump.</summary>
    public static void Bump(Vector3 position, float strength) => Shake(position, Vector3.down, strength);
}
