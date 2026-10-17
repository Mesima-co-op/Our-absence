using UnityEngine;

/// <summary>
/// Where a weapon is held. Lives on the weapon prefab with a "Grip" child placed at the middle of the
/// handle: its forward (+Z) points toward the tip and its up (+Y) along the edge.
/// Our Absence > Combat > Set Up Weapons And Attacks places the Grip on every weapon automatically;
/// move it in the prefab to fine-tune how a weapon sits in the hand.
/// </summary>
public class WeaponGrip : MonoBehaviour
{
    [Tooltip("Middle of the handle; +Z toward the tip, +Y along the cutting edge")]
    public Transform grip;

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
    /// The pose of a closed fist around a handle, from the hand's finger bones: at the palm,
    /// forward out of the thumb side of the fist, up toward the knuckles (where the edge faces).
    /// Returns false if the rig has no finger bones.
    /// </summary>
    public static bool HandGripPose(Animator animator, bool rightHand, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (animator == null || !animator.isHuman) return false;

        var hand = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        var index = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
        var middle = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
        var little = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
        var thumb = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
        if (hand == null || index == null || middle == null || little == null || thumb == null) return false;

        Vector3 toKnuckles = middle.position - hand.position;
        float handLength = toKnuckles.magnitude;
        Vector3 f = toKnuckles / handLength;
        // Across the fist, from the little finger to the index finger: the handle's direction
        Vector3 across = Vector3.ProjectOnPlane(index.position - little.position, f).normalized;
        // Out of the palm, toward where the thumb wraps around
        Vector3 palm = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(thumb.position - hand.position, f), across).normalized;

        position = hand.position + f * (handLength * 0.85f) + palm * (handLength * 0.32f);
        rotation = Quaternion.LookRotation(across, f);
        return true;
    }
}
