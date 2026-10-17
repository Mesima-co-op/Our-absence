using UnityEngine;

/// <summary>
/// Where a weapon is held. Lives on the weapon prefab with a "Grip" child placed at the middle of the
/// handle: its forward (+Z) points toward the tip and its up (+Y) along the cutting edge.
/// Our Absence > Combat > Set Up Weapons And Attacks places the Grip on every weapon automatically;
/// move it in the prefab to fine-tune how a weapon sits in the hand.
/// </summary>
public class WeaponGrip : MonoBehaviour
{
    [Tooltip("Middle of the handle; +Z toward the tip, +Y along the cutting edge")]
    public Transform grip;

    /// <summary>Default blade direction in the fist: straight out of the thumb side.</summary>
    public static readonly Vector3 StraightOut = new Vector3(1f, 0f, 0f);

    /// <summary>Moves the weapon so its grip sits exactly at <paramref name="position"/> with <paramref name="rotation"/>.</summary>
    public void AlignGrip(Vector3 position, Quaternion rotation)
    {
        if (grip == null) return;
        // The grip's rotation relative to the weapon stays fixed, so solve for the weapon's rotation
        Quaternion gripRelative = Quaternion.Inverse(transform.rotation) * grip.rotation;
        transform.rotation = rotation * Quaternion.Inverse(gripRelative);
        transform.position += position - grip.position;
    }

    /// <summary>
    /// Places this weapon in the closed fist. <paramref name="bladeInFist"/> is the blade's direction in the
    /// fist's own axes (x = out of the thumb side, y = toward the knuckles, z = out of the palm); the movesets
    /// store the direction that suits their animations.
    /// </summary>
    public bool AlignToHand(Animator animator, bool rightHand, Vector3 bladeInFist)
    {
        if (!FistFrame(animator, rightHand, out var position, out var across, out var knuckles, out var palm)) return false;
        AlignGrip(position, BladeRotation(across, knuckles, palm, bladeInFist));
        return true;
    }

    /// <summary>World rotation of a blade pointing along <paramref name="bladeInFist"/>, edge toward the knuckles.</summary>
    public static Quaternion BladeRotation(Vector3 across, Vector3 knuckles, Vector3 palm, Vector3 bladeInFist)
    {
        if (bladeInFist.sqrMagnitude < 1e-6f) bladeInFist = StraightOut;
        Vector3 blade = (across * bladeInFist.x + knuckles * bladeInFist.y + palm * bladeInFist.z).normalized;
        Vector3 edge = Vector3.ProjectOnPlane(knuckles, blade);
        if (edge.sqrMagnitude < 1e-4f) edge = Vector3.ProjectOnPlane(palm, blade);
        return Quaternion.LookRotation(blade, edge.normalized);
    }

    /// <summary>
    /// The closed fist, from the hand's finger bones: the handle's centre, and three axes:
    /// across the fist (little finger to index, where the blade leaves), toward the knuckles, and out of the palm.
    /// Returns false if the rig has no finger bones.
    /// </summary>
    public static bool FistFrame(Animator animator, bool rightHand, out Vector3 position, out Vector3 across, out Vector3 knuckles, out Vector3 palm)
    {
        position = across = knuckles = palm = Vector3.zero;
        if (animator == null || !animator.isHuman) return false;

        var hand = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        var index = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
        var middle = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
        var little = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
        var thumb = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
        if (hand == null || index == null || middle == null || little == null || thumb == null) return false;

        Vector3 toKnuckles = middle.position - hand.position;
        float handLength = toKnuckles.magnitude;
        knuckles = toKnuckles / handLength;
        across = Vector3.ProjectOnPlane(index.position - little.position, knuckles).normalized;
        palm = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(thumb.position - hand.position, knuckles), across).normalized;

        position = hand.position + knuckles * (handLength * 0.85f) + palm * (handLength * 0.32f);
        return true;
    }
}
