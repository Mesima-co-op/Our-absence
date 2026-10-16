using UnityEngine;

/// <summary>
/// The combat and movement effects, and how strong the hit feedback is.
/// One asset lives at Resources/FX/FXLibrary and is loaded on first use, so no scene needs a reference.
/// Built by Our Absence > Effects > Build Combat And Movement FX; tweak the prefabs and numbers freely.
/// </summary>
[CreateAssetMenu(menuName = "Effects/FX Library")]
public class FXLibrary : ScriptableObject
{
    public const string ResourcePath = "FX/FXLibrary";

    [Header("Combat")]
    [Tooltip("Sparks and a flash where a weapon connects")]
    public GameObject hitSparks;
    [Tooltip("Dark blood spray in the direction of the blow")]
    public GameObject bloodSpray;
    [Tooltip("Dust burst when a character dies")]
    public GameObject deathDust;
    [Tooltip("Material of the swoosh behind a swinging weapon (additive, uses vertex colour)")]
    public Material weaponTrail;
    public Gradient weaponTrailColor;

    [Header("Movement")]
    public GameObject footstepDust;
    public GameObject rollDust;
    public GameObject landDust;

    [Header("Hit stop (freeze frames)")]
    [Tooltip("Seconds of near-freeze when the player lands a hit")]
    public float hitStopDealt = 0.055f;
    [Tooltip("Seconds of near-freeze when the player is hit")]
    public float hitStopTaken = 0.09f;
    [Range(0f, 1f)] public float hitStopTimeScale = 0.03f;

    [Header("Camera shake (Cinemachine impulse)")]
    public float shakeDealt = 0.12f;
    public float shakeTaken = 0.3f;
    public float shakeLand = 0.15f;

    static FXLibrary instance;
    static bool searched;

    /// <summary>The library in Resources, or null if it hasn't been built (effects are then skipped).</summary>
    public static FXLibrary Instance
    {
        get
        {
            if (instance == null && !searched)
            {
                searched = true;
                instance = Resources.Load<FXLibrary>(ResourcePath);
                if (instance == null)
                    Debug.LogWarning("[FX] No FXLibrary in Resources/FX. Run Our Absence > Effects > Build Combat And Movement FX.");
            }
            return instance;
        }
    }
}
