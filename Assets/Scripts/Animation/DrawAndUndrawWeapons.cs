using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shows the equipped weapons (LW1 / RW1) on the player.
/// - Equipped but sheathed: the weapon rests on the back (BackSocket_L / BackSocket_R).
/// - The first attack press plays the draw animation, moves that weapon into the hand and swings; later presses attack.
/// - U (UndrawWeapon) puts every weapon back on the back. Unequipping removes it.
/// Weapons with a WeaponGrip are placed by their handle, so they sit properly in the fist.
/// </summary>
public class DrawAndUndrawWeapons : MonoBehaviour
{
    public const string LeftSlot = "LW1";
    public const string RightSlot = "RW1";

    [Header("References")]
    [SerializeField] private PlayerInput inputSystem;
    [SerializeField] private AnimationManager animationManager;
    [SerializeField] private EquipmentControl equipmentManager;
    [SerializeField] private ObjectsState globalVariables;
    private AttackAnimationController attackAnimations;

    [Header("Weapon location objects Prefabs")]
    [Tooltip("Hand sockets: parents of the held weapons")]
    [SerializeField] private GameObject leftWeaponPrefab;
    [SerializeField] private GameObject rightWeaponPrefab;

    [Header("Sheathed weapons")]
    [Tooltip("Where a sheathed weapon's grip rests (+Z along the blade). Without one, sheathed weapons are hidden.")]
    [SerializeField] private Transform leftBackSocket;
    [SerializeField] private Transform rightBackSocket;
    [Tooltip("Seconds into the draw animation when the hand takes the weapon")]
    [SerializeField] private float drawGrabDelay = 0.3f;

    [Header("Inputs")]
    private InputAction undrawWeaponAction;
    private InputAction playerLeftAttack;
    private InputAction playerRightAttack;

    private class Carried
    {
        public ItemDataInstance item;
        public GameObject instance;
        public bool inHand;
    }

    private readonly Dictionary<string, Carried> carried = new Dictionary<string, Carried>();
    private PlayerCombat combat;

    void Awake()
    {
        combat = GetComponent<PlayerCombat>();
        undrawWeaponAction = inputSystem.actions["UndrawWeapon"];
        playerLeftAttack = inputSystem.actions["Fire"];
        playerRightAttack = inputSystem.actions["SecFire"];

        attackAnimations = new AttackAnimationController(animationManager);
    }

    void OnEnable() => EquipmentControl.OnEquipmentChanged += HandleEquipmentChanged;
    void OnDisable() => EquipmentControl.OnEquipmentChanged -= HandleEquipmentChanged;

    void Start()
    {
        equipmentManager = FindObjectOfType<EquipmentControl>();
        globalVariables = FindObjectOfType<ObjectsState>();

        // Weapons equipped before this object existed (e.g. restored from a save)
        if (equipmentManager != null && equipmentManager.equipmentSlots != null)
            foreach (var slot in new[] { LeftSlot, RightSlot })
                if (equipmentManager.equipmentSlots.TryGetValue(slot, out var s) && s?.item != null)
                    HandleEquipmentChanged(slot, s.item, true);
    }

    void Update()
    {
        if (globalVariables == null || globalVariables.menuOpen) return;

        if (undrawWeaponAction.triggered)
            SheatheAll();

        if (playerLeftAttack.triggered)
            HandleLeftAttackAttempt();
        else if (playerRightAttack.triggered)
            HandleRightAttackAttempt();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Attacks
    // ─────────────────────────────────────────────────────────────────────

    public void HandleRightAttackAttempt() => AttackAttempt(RightSlot);
    public void HandleLeftAttackAttempt() => AttackAttempt(LeftSlot);

    /// <summary>Draws the slot's weapon if it is on the back, otherwise attacks with that hand.</summary>
    private void AttackAttempt(string slot)
    {
        carried.TryGetValue(slot, out var weapon);
        bool hasWeapon = weapon != null && weapon.instance != null;
        bool drawn = animationManager.animator.GetBool("isWeaponDrawn");

        if (hasWeapon && !weapon.inHand)
        {
            if (drawn)
            {
                // Already in combat stance with the other hand: just take it
                MoveToHand(slot);
                Attack(slot, weapon.item);
            }
            else
            {
                animationManager.TriggerAnimation("drawWeapon");
                StartCoroutine(GrabAfterDelay(slot));
            }
            return;
        }

        Attack(slot, hasWeapon ? weapon.item : null);
    }

    private void Attack(string slot, ItemDataInstance item)
    {
        if (combat != null && combat.enabled)
        {
            // Combos matched to the weapon (PlayerCombat); a weapon not yet in hand attacks bare-handed
            carried.TryGetValue(slot, out var weapon);
            bool held = weapon != null && weapon.inHand && weapon.instance != null;
            combat.Attack(slot == RightSlot,
                held ? item?._definition?.Moveset : null,
                held ? weapon.instance.GetComponent<WeaponDamageSystem>() : null);
            return;
        }

        float type = item?._definition != null ? item._definition.ItemID : 0f;
        if (slot == RightSlot) attackAnimations.TriggerRightWeaponAttack(type);
        else attackAnimations.TriggerLeftWeaponAttack(type);
    }

    private IEnumerator GrabAfterDelay(string slot)
    {
        yield return new WaitForSeconds(drawGrabDelay);
        MoveToHand(slot);
        // One press draws and swings: the first attack starts as soon as the weapon is in hand
        if (combat != null && combat.enabled && carried.TryGetValue(slot, out var weapon) && weapon.inHand)
            Attack(slot, weapon.item);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Equip / draw / sheathe
    // ─────────────────────────────────────────────────────────────────────

    private void HandleEquipmentChanged(string slot, ItemDataInstance item, bool equipped)
    {
        if (slot != LeftSlot && slot != RightSlot) return;

        if (carried.TryGetValue(slot, out var current))
        {
            if (equipped && current.item == item && current.instance != null) return; // already showing it
            if (current.instance != null) Destroy(current.instance);
            carried.Remove(slot);
        }

        // The hand's combo follows the weapon equipped in it (swapping clips here, not mid-attack)
        if (combat != null) combat.SetMoveset(slot == RightSlot, equipped ? item?._definition?.Moveset : null);

        if (!equipped || item?._definition == null || item._definition.ObjectRef == null) return;

        var instance = Instantiate(item._definition.ObjectRef);
        instance.name = item._definition.ObjectRef.name;
        PrepareCarriedWeapon(instance, item._definition.ObjectRef.transform.localScale);
        carried[slot] = new Carried { item = item, instance = instance };
        Sheathe(slot);

        // Equipped while fighting: straight into the hand
        if (animationManager.animator.GetBool("isWeaponDrawn")) MoveToHand(slot);
    }

    /// <summary>Puts both weapons on the back (U, or when unequipping from the menu).</summary>
    public void SheatheAll()
    {
        animationManager.SetBoolParam("isWeaponDrawn", false);
        Sheathe(LeftSlot);
        Sheathe(RightSlot);
    }

    /// <summary>Kept for EquipmentControl: unequipping sheathes; the event then removes the weapon.</summary>
    public void UndrawWeapon(ItemDataInstance item1, ItemDataInstance item2) => SheatheAll();

    private void MoveToHand(string slot)
    {
        if (!carried.TryGetValue(slot, out var weapon) || weapon.instance == null) return;
        bool right = slot == RightSlot;
        var socket = (right ? rightWeaponPrefab : leftWeaponPrefab).transform;
        var t = weapon.instance.transform;

        t.SetParent(socket, true);
        weapon.instance.SetActive(true);
        var grip = weapon.instance.GetComponent<WeaponGrip>();
        var moveset = weapon.item?._definition?.Moveset;
        var bladeInFist = moveset != null ? moveset.bladeInFist : WeaponGrip.StraightOut;
        if (grip == null || !grip.AlignToHand(animationManager.animator, right, bladeInFist))
        {
            // Old fixed offsets for weapons without a grip point
            t.localRotation = right ? Quaternion.Euler(90f, 0, 90f) : Quaternion.Euler(90f, 90f, 0f);
            t.localPosition = new Vector3(0f, 0.05f, 0f);
        }
        SetHeld(weapon, true);
    }

    private void Sheathe(string slot)
    {
        if (!carried.TryGetValue(slot, out var weapon) || weapon.instance == null) return;
        var back = slot == RightSlot ? rightBackSocket : leftBackSocket;
        var t = weapon.instance.transform;
        var grip = weapon.instance.GetComponent<WeaponGrip>();

        if (back == null)
        {
            // Nowhere to put it: hide it until it's drawn
            t.SetParent(transform, false);
            weapon.instance.SetActive(false);
        }
        else
        {
            t.SetParent(back, true);
            weapon.instance.SetActive(true);
            if (grip != null) grip.AlignGrip(back.position, back.rotation);
            else t.SetPositionAndRotation(back.position, back.rotation);
        }
        SetHeld(weapon, false);
    }

    private static void SetHeld(Carried weapon, bool inHand)
    {
        weapon.inHand = inHand;
        // A sheathed weapon must not hit anything or leave a trail
        foreach (var col in weapon.instance.GetComponentsInChildren<Collider>(true))
            col.enabled = inHand;
        var damage = weapon.instance.GetComponent<WeaponDamageSystem>();
        if (damage != null) damage.Sheathed = !inHand;
    }

    /// <summary>
    /// A carried weapon must not be picked up again or collide physically,
    /// but its colliders must still report triggers so WeaponDamageSystem can hit enemies.
    /// </summary>
    private void PrepareCarriedWeapon(GameObject weapon, Vector3 worldScale)
    {
        var pickUp = weapon.GetComponent<PickUpItem>();
        if (pickUp != null) pickUp.enabled = false;
        foreach (var body in weapon.GetComponentsInChildren<Rigidbody>())
            body.isKinematic = true;
        foreach (var col in weapon.GetComponentsInChildren<Collider>())
            col.isTrigger = true;

        weapon.transform.localScale = worldScale; // same size as on the ground, whatever it's parented to
        weapon.tag = "Weapon";
        weapon.layer = rightWeaponPrefab.layer;
    }
}
