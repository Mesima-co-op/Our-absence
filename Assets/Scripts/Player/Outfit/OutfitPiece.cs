using UnityEngine;

/// <summary>Where on the body a piece of clothing goes. One piece per slot.</summary>
public enum OutfitSlot { Head, Body, Arms, Legs, Feet }

/// <summary>Parts of the naked body that clothing can hide (so skin doesn't poke through).</summary>
[System.Flags]
public enum BodyRegion
{
    None = 0,
    Head = 1 << 0,
    Torso = 1 << 1,
    Hips = 1 << 2,
    Arms = 1 << 3,
    Legs = 1 << 4,
    Feet = 1 << 5,
    Hair = 1 << 6,
}

/// <summary>
/// A wearable piece of clothing: one or more skinned models made for the player's skeleton
/// (Quaternius "Universal Base Characters" rig). Put it on with CharacterOutfit.Wear, or link it
/// to an InventoryItem (Wearable field) so equipping the item in the Equipment menu puts it on.
/// Create: Assets > Create > Item > Outfit Piece.
/// </summary>
[CreateAssetMenu(fileName = "New Outfit Piece", menuName = "Item/Outfit Piece")]
public class OutfitPiece : ScriptableObject
{
    public string displayName;
    public OutfitSlot slot;

    [Tooltip("Models whose skinned meshes are worn (FBX files rigged to the player skeleton)")]
    public GameObject[] models;

    [Tooltip("Parts of the naked body hidden while this is worn")]
    public BodyRegion hides;

    /// <summary>
    /// Body parts a slot may hide. Only the skin actually under the clothing is hidden
    /// (see CharacterOutfit), so these can be generous: short sleeves still show the forearms.
    /// </summary>
    public static BodyRegion DefaultHides(OutfitSlot slot) => slot switch
    {
        OutfitSlot.Head => BodyRegion.Hair,
        OutfitSlot.Body => BodyRegion.Torso | BodyRegion.Hips | BodyRegion.Arms,
        OutfitSlot.Arms => BodyRegion.Arms,
        OutfitSlot.Legs => BodyRegion.Hips | BodyRegion.Legs | BodyRegion.Feet,
        OutfitSlot.Feet => BodyRegion.Feet | BodyRegion.Legs,
        _ => BodyRegion.None,
    };

    /// <summary>The OutfitSlot for one of the Equipment menu's armour slot names, or null.</summary>
    public static OutfitSlot? SlotForEquipmentSlot(string equipmentSlot) => equipmentSlot switch
    {
        "Helmet" => OutfitSlot.Head,
        "Chestplate" => OutfitSlot.Body,
        "GauntletsL" or "GauntletsR" => OutfitSlot.Arms,
        "Leggings" => OutfitSlot.Legs,
        "Boots" => OutfitSlot.Feet,
        _ => null,
    };
}
