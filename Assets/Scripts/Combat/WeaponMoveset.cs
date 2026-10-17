using UnityEngine;

/// <summary>
/// How a kind of weapon fights: the combo of attacks played on repeated presses.
/// Each step names its animation, when in the swing it can hit, and when the next press may chain.
/// Weapons point to a moveset from their item (InventoryItem > Moveset); bare hands use the player's
/// unarmed moveset. Built by Our Absence > Combat > Build Weapons And Movesets; safe to tweak by hand.
/// </summary>
[CreateAssetMenu(menuName = "Item/Weapon Moveset")]
public class WeaponMoveset : ScriptableObject
{
    [System.Serializable]
    public class Step
    {
        [Tooltip("The attack animation (right-handed; it is mirrored for the left hand)")]
        public AnimationClip clip;
        [Tooltip("Played when the combo ends after this step (optional)")]
        public AnimationClip recovery;
        [Tooltip("Playback speed of the attack")]
        public float speed = 1f;
        [Tooltip("Damage multiplier for this step")]
        public float damage = 1f;
        [Tooltip("Part of the clip (0-1) in which the weapon deals damage")]
        [Range(0f, 1f)] public float hitStart = 0.3f;
        [Range(0f, 1f)] public float hitEnd = 0.6f;
        [Tooltip("From this point (0-1) the next step can start, if the attack button was pressed")]
        [Range(0f, 1f)] public float chainFrom = 0.65f;
        [Tooltip("Metres the player steps forward during the swing (spread until the end of the hit window)")]
        public float lunge = 0.3f;
        [Tooltip("The clip strikes with the left hand (like a jab). It is mirrored, so the blow comes from the hand " +
                 "that attacks; otherwise that hand's layer would mask the punch out.")]
        public bool mirror;
    }

    public string displayName = "Moveset";
    [Tooltip("How the weapon points out of the fist for these animations (x = thumb side, y = knuckles, " +
             "z = palm). Found by the builder: the direction whose blade best sweeps the space in front of the " +
             "player during the hit windows.")]
    public Vector3 bladeInFist = new Vector3(1f, 0f, 0f);
    [Tooltip("How far in front of the player a swing reaches (m). During a hit window, enemies inside this " +
             "reach and in front of the player are hit, as well as anything the blade itself touches.")]
    public float reach = 1.5f;
    [Tooltip("Half-angle of the swing's arc in front of the player (degrees)")]
    public float arc = 70f;
    public Step[] combo = new Step[0];
    [Tooltip("Seconds a press is remembered while the current attack is still playing")]
    public float inputBuffer = 0.45f;
}
