using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class EquipmentControl : MonoBehaviour
{
    [Header("Dependencies")]
    private ObjectsState globalVars;
    private MainMenuController mainMenuController;

    [Header("UI Elements")]
    private VisualElement optionMenu;
    private VisualElement clickCatcher;

    [Header("Equipment State")]
    public EquipmentSlot selectedSlot;
    public Dictionary<string, EquipmentSlot> equipmentSlots;

    #region Unity Lifecycle
    void Start()
    {
        InitializeDependencies();
        InitializeEquipmentSlots();
        RegisterSlotEvents();
    }
    #endregion

    #region Initialization
    private void InitializeDependencies()
    {
        globalVars = FindObjectOfType<ObjectsState>();
        mainMenuController = FindObjectOfType<MainMenuController>();

        if (globalVars == null || mainMenuController == null)
        {
            Debug.LogError("EquipmentControl: Missing required dependencies!");
        }
    }

    private void InitializeEquipmentSlots()
    {
        if (globalVars?.mainMenuUI?.rootVisualElement == null)
        {
            Debug.LogError("EquipmentControl: UI root element not found!");
            return;
        }

        var root = globalVars.mainMenuUI.rootVisualElement;
        equipmentSlots = new Dictionary<string, EquipmentSlot>();

        // Define slot configurations
        var slotConfigs = new[]
        {
            // Weapons
            ("LW1", MainMenuController.CategoryType.Weapon),
            ("RW1", MainMenuController.CategoryType.Weapon),
            
            // Armor
            ("Helmet", MainMenuController.CategoryType.Armor),
            ("Chestplate", MainMenuController.CategoryType.Armor),
            ("Leggings", MainMenuController.CategoryType.Armor),
            ("GauntletsL", MainMenuController.CategoryType.Armor),
            ("GauntletsR", MainMenuController.CategoryType.Armor),
            ("Boots", MainMenuController.CategoryType.Armor),
            
            // Magic Items
            ("MagicItem1", MainMenuController.CategoryType.MagicItems),
            ("MagicItem2", MainMenuController.CategoryType.MagicItems),

        };

        // Create equipment slots
        foreach (var (slotName, category) in slotConfigs)
        {
            var button = root.Q<Button>(slotName);
            if (button != null)
            {
                var slot = new EquipmentSlot(button, category, null, slotName);
                equipmentSlots[slotName] = slot;
                UpdateSlotDisplay(slot);
            }
            else
            {
                Debug.LogWarning($"EquipmentControl: Button '{slotName}' not found in UI!");
            }

        }
    }

    private void RegisterSlotEvents()
    {
        foreach (var slot in equipmentSlots.Values)
        {
            if (slot.button != null)
            {
                slot.button.clicked += () => OnSlotClicked(slot);
            }
        }
    }
    #endregion

    #region Event Handlers
    private void OnSlotClicked(EquipmentSlot slot)
    {
        selectedSlot = slot;

        if (slot.HasItem())
        {
            // Show equipped item info or options
            ShowEquippedItemOptions(slot);
        }
        else
        {
            // Show equip options
            ShowEquipOptions(slot);
        }
    }

    private void ShowEquippedItemOptions(EquipmentSlot slot)
    {
        var position = slot.button.worldBound.position;
        ShowOptionMenu(position, new (string, System.Action)[]
        {
            ("Unequip", () => UnequipItem(slot)),
            ("Replace", () => ReplaceItem(slot)),
            ("Info", () => ShowItemInfo(slot.item))
        });
    }

    private void ShowEquipOptions(EquipmentSlot slot)
    {
        var position = slot.button.worldBound.position;
        ShowOptionMenu(position, new (string, System.Action)[]
        {
            ("Equip", () => OpenInventoryForEquip(slot))
        });
    }
    #endregion

    #region Equipment Actions
    public void EquipItem(ItemDataInstance item, EquipmentSlot slot)
    {
        if (slot == null)
        {
            Debug.LogWarning("EquipmentControl: No slot selected for equipping!");
            return;
        }

        if (!CanEquipToSlot(item, slot))
        {
            Debug.LogWarning($"EquipmentControl: Cannot equip {item._definition.ItemName} to {slot.button.name}!");
            return;
        }

        // Store previous item for potential return to inventory
        var previousItem = slot.item;

        // Equip new item
        slot.SetItem(item);
        UpdateSlotDisplay(slot);

        // Change previous item slot to null if existed
        if (previousItem != null)
        {
            previousItem.equippedSlotName = null;
        }

        // Change new item slot name
        slot.item.equippedSlotName = slot.button.name;

        Debug.Log($"Equipped {item._definition.ItemName} to {slot.button.name}");
        selectedSlot = null;
    }

    public void UnequipItem(EquipmentSlot slot)
    {
        if (slot == null)
        {
            Debug.LogWarning("EquipmentControl: No slot selected for unequipping!");
            return;
        }
        if (!slot.HasItem()) return;

        var item = slot.item;
        slot.SetItem(null);
        UpdateSlotDisplay(slot);
        DrawAndUndrawWeapons player = FindObjectOfType<DrawAndUndrawWeapons>();
        if (player != null)
        {
            player.UndrawWeapon(item, item); // Pass the item to undraw
        }

        // Return item to inventory
        item.equippedSlotName = null;

        Debug.Log($"Unequipped {item._definition.ItemName} from {slot.button.name}");
        ClearOptionMenu();
    }

    private void ReplaceItem(EquipmentSlot slot)
    {
        selectedSlot = slot;
        OpenInventoryForEquip(slot);
    }

    private void OpenInventoryForEquip(EquipmentSlot slot)
    {
        selectedSlot = slot;
        mainMenuController.OpenInventoryForEquipment(this, slot.category);
        ClearOptionMenu();
    }

    private bool CanEquipToSlot(ItemDataInstance item, EquipmentSlot slot)
    {
        // Check if item category matches slot category
        var itemCategory = GetItemCategory(item._definition.ItemSubType);
        return itemCategory == slot.category;
    }

    private MainMenuController.CategoryType GetItemCategory(MainMenuController.ItemType itemType)
    {
        return itemType switch
        {
            MainMenuController.ItemType.LightWeapon => MainMenuController.CategoryType.Weapon,
            MainMenuController.ItemType.HeavyWeapon => MainMenuController.CategoryType.Weapon,
            MainMenuController.ItemType.Bow => MainMenuController.CategoryType.Weapon,
            MainMenuController.ItemType.Crossbow => MainMenuController.CategoryType.Weapon,
            MainMenuController.ItemType.Helmet => MainMenuController.CategoryType.Armor,
            MainMenuController.ItemType.Chestplate => MainMenuController.CategoryType.Armor,
            MainMenuController.ItemType.Leggings => MainMenuController.CategoryType.Armor,
            MainMenuController.ItemType.Gauntlets => MainMenuController.CategoryType.Armor,
            MainMenuController.ItemType.Arrows => MainMenuController.CategoryType.Ammo,
            MainMenuController.ItemType.Bolts => MainMenuController.CategoryType.Ammo,
            MainMenuController.ItemType.MagicItem => MainMenuController.CategoryType.MagicItems,
            MainMenuController.ItemType.QuickItem => MainMenuController.CategoryType.EquipmentItems,
            _ => MainMenuController.CategoryType.Misc
        };
    }

    private void UpdateSlotDisplay(EquipmentSlot slot)
    {
        if (slot.HasItem())
        {
            // Get the child visual element named "Display"
            var display = slot.button.Q<VisualElement>("Display");

            if (display != null)
            {
                // Assign the icon to the background
                display.style.backgroundImage = new StyleBackground(slot.item._definition.Icon);
            }
            else
            {
                Debug.LogWarning("Display element not found inside button.");
            }
        }
        else
        {
            // Clear display back to default
            var display = slot.button.Q<VisualElement>("Display");
            if (display != null)
                display.style.backgroundImage = GetDefaultIconForSlot(slot.button.name);
        }
    }

    private Texture2D GetDefaultIconForSlot(string type)
    {
        switch (type)
        {
            case "LW1":
                return Resources.Load<Texture2D>("Equipment/Icons/sword-temp");

            case "RW1":
                return Resources.Load<Texture2D>("Equipment/Icons/sword-temp");

            case "Helmet":
                return Resources.Load<Texture2D>("Equipment/Icons/Helment-temp");

            case "Chestplate":
                return Resources.Load<Texture2D>("Equipment/Icons/Chestplate-temp");

            case "Leggings":
                return Resources.Load<Texture2D>("Equipment/Icons/Leggings-temp");

            case "GauntletsL":
                return Resources.Load<Texture2D>("Equipment/Icons/Gauntlets-temp");

            case "GauntletsR":
                return Resources.Load<Texture2D>("Equipment/Icons/Gauntlets-temp");

            case "Boots":
                return Resources.Load<Texture2D>("Equipment/Icons/Leggings-temp");

            case "MagicItem1":
                return Resources.Load<Texture2D>("Equipment/Icons/ring-temp");

            case "MagicItem2":
                return Resources.Load<Texture2D>("Equipment/Icons/ring-temp");

            default:
                return Resources.Load<Texture2D>("Icons/Defaults/empty");
        }
    }

    private string GetEmptySlotText(string slotName)
    {
        return slotName switch
        {
            "LW1" or "LW2" or "LW3" => "Left Weapon",
            "RW1" or "RW2" or "RW3" => "Right Weapon",
            "Helmet" => "Helmet",
            "Chestplate" => "Chestplate",
            "Leggings" => "Leggings",
            "GauntletsL" => "GauntletsL",
            "GauntletsR" => "GauntletsR",
            "Arrows1" or "Arrows2" => "Arrows",
            "Bolts1" or "Bolts2" => "Bolts",
            var name when name.StartsWith("MagicItem") => "Magic Item",
            var name when name.StartsWith("QuickItem") => "Quick Item",
            _ => "Empty"
        };
    }

    private void ShowItemInfo(ItemDataInstance item)
    {
        if (item != null)
        {
            Debug.Log($"Item Info: {item._definition.ItemName} (Type: {item._definition.ItemSubType}, ID: {item._definition.ItemID})");
            // Could open a detailed info panel here
        }
        ClearOptionMenu();
    }
    #endregion

    #region Option Menu
    private void ShowOptionMenu(Vector2 position, (string text, System.Action action)[] options)
    {
        ClearOptionMenu();

        if (globalVars?.mainMenuUI?.rootVisualElement == null) return;

        var root = globalVars.mainMenuUI.rootVisualElement;

        // Create click catcher
        clickCatcher = new VisualElement
        {
            style = {
                position = Position.Absolute,
                left = 0, top = 0, right = 0, bottom = 0,
                backgroundColor = new Color(0, 0, 0, 0)
            }
        };
        root.Add(clickCatcher);

        // Create option menu
        optionMenu = new VisualElement
        {
            style = {
                position = Position.Absolute,
                left = position.x + 50,
                top = position.y + 15,
                backgroundImage = Resources.Load<Texture2D>("Sprites&images/Hub/Button-005"),
                paddingLeft = 8, paddingRight = 8,
                paddingTop = 6, paddingBottom = 6,
                borderTopLeftRadius = 4, borderTopRightRadius = 4,
                borderBottomLeftRadius = 4, borderBottomRightRadius = 4,
                borderLeftWidth = 0, borderRightWidth = 0,
                borderTopWidth = 0, borderBottomWidth = 0,
                borderLeftColor = Color.gray, borderRightColor = Color.gray,
                borderTopColor = Color.gray, borderBottomColor = Color.gray
            }
        };

        // Add option buttons
        foreach (var (text, action) in options)
        {
            var button = new Button(action)
            {
                text = text
            };

            // Load assets
            var bg = Resources.Load<Texture2D>("Sprites&images/Hub/item_frame-001");
            var bg2 = Resources.Load<Texture2D>("Sprites&images/Hub/item_frame-002");
            var font = Resources.Load<Font>("Fonts/WashingtonText");

            // Base style
            button.style.marginBottom = 2;
            button.style.marginTop = 2;
            button.style.backgroundImage = new StyleBackground(bg);
            button.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            button.style.unityFontDefinition = FontDefinition.FromFont(font);
            button.style.fontSize = 14;
            button.style.borderTopWidth = 0;
            button.style.borderLeftWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.color = new Color(178, 178, 178, 255);

            // ------------------------
            // HOVER EFFECT
            // ------------------------
            button.RegisterCallback<PointerEnterEvent>(evt =>
            {
                button.style.backgroundImage = new StyleBackground(bg2);
            });

            // ------------------------
            // UNHOVER EFFECT
            // ------------------------
            button.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                button.style.backgroundImage = new StyleBackground(bg);
            });

            // ------------------------
            // PRESS EFFECT
            // ------------------------
            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                button.style.scale = new Vector2(0.95f, 0.95f); // slight shrink effect
            });

            // ------------------------
            // RELEASE EFFECT
            // ------------------------
            button.RegisterCallback<PointerUpEvent>(evt =>
            {
                button.style.scale = new Vector2(1f, 1f);
            });
            optionMenu.Add(button);
        }

        root.Add(optionMenu);

        // Handle outside clicks
        clickCatcher.RegisterCallback<ClickEvent>(evt =>
        {
            if (!optionMenu.worldBound.Contains(evt.position))
            {
                ClearOptionMenu();
            }
        });
    }

    private void ClearOptionMenu()
    {
        optionMenu?.RemoveFromHierarchy();
        optionMenu = null;
        clickCatcher?.RemoveFromHierarchy();
        clickCatcher = null;
    }
    #endregion

    #region Public API
    public EquipmentSlot GetSlot(string slotName)
    {
        return equipmentSlots.TryGetValue(slotName, out var slot) ? slot : null;
    }

    public List<ItemDataInstance> GetAllEquippedItems()
    {
        var equippedItems = new List<ItemDataInstance>();
        foreach (var slot in equipmentSlots.Values)
        {
            if (slot.HasItem())
            {
                equippedItems.Add(slot.item);
            }
        }
        return equippedItems;
    }

    public string HasItemEquipped(int itemID)
    {
        foreach (var (slotKey, slotValue) in equipmentSlots)
        {
            Debug.Log($"Checking slot {slotKey} for item ID {itemID}");
            if (slotValue.HasItem() && slotValue.item._definition.ItemID == itemID)
            {
                return (string)slotKey;
            }
        }
        return null;
    }

    public void PickUpItem(GameObject origin, float range)
    {
        Collider[] hitColliders = Physics.OverlapSphere(origin.transform.position, range);
        foreach (var hitCollider in hitColliders)
        {
            if (hitCollider.GetComponent<PickUpItem>()?.IsPickUpItemInRange() != null)
            {
                var item = hitCollider.GetComponent<PickUpItem>().Item;
                if (item != null)
                {
                    if (origin.GetComponent<Player_controller>()?.isPickingUp != null) origin.GetComponent<Player_controller>().isPickingUp = true;
                    mainMenuController.AddItemToInventory(item);
                    hitCollider.gameObject.SetActive(false);
                    Debug.Log($"Picked up item: {item._definition.ItemName}");
                    break;
                }
            }
        }
    }

    internal object GetAllowedItemTypes()
    {
        if (selectedSlot == null) return new[] { MainMenuController.ItemType.LightWeapon,
        MainMenuController.ItemType.HeavyWeapon,
        MainMenuController.ItemType.Bow,
        MainMenuController.ItemType.Crossbow,
        MainMenuController.ItemType.Helmet,
        MainMenuController.ItemType.Chestplate,
        MainMenuController.ItemType.Leggings,
        MainMenuController.ItemType.Gauntlets,
        MainMenuController.ItemType.MagicItem,
        MainMenuController.ItemType.Bolts,
        MainMenuController.ItemType.Arrows,
        MainMenuController.ItemType.QuickItem};

        switch (selectedSlot.button.name)
        {
            case "LW1":
            case "LW2":
            case "LW3":
            case "RW1":
            case "RW2":
            case "RW3":
                return new[] { MainMenuController.ItemType.LightWeapon, MainMenuController.ItemType.HeavyWeapon, MainMenuController.ItemType.Bow, MainMenuController.ItemType.Crossbow };
            case "Helmet":
                return new[] { MainMenuController.ItemType.Helmet };
            case "Chestplate":
                return new[] { MainMenuController.ItemType.Chestplate };
            case "Leggings":
                return new[] { MainMenuController.ItemType.Leggings };
            case "GauntletsL":
            case "GauntletsR":
                return new[] { MainMenuController.ItemType.Gauntlets };
            case "Arrows1":
            case "Arrows2":
                return new[] { MainMenuController.ItemType.Arrows };
            case "Bolts1":
            case "Bolts2":
                return new[] { MainMenuController.ItemType.Bolts };
            case var name when name.StartsWith("MagicItem"):
                return new[] { MainMenuController.ItemType.MagicItem };
            case var name when name.StartsWith("QuickItem"):
                return new[] { MainMenuController.ItemType.QuickItem };
            default:
                return null;
        }
    }

    internal void EquipToQuickItem(ItemDataInstance item)
    {
        selectedSlot = FindEmptySlot(MainMenuController.CategoryType.EquipmentItems);
        EquipItem(item, selectedSlot);
    }

    internal void EquipToWeapon(ItemDataInstance item)
    {
        selectedSlot = FindEmptySlot(MainMenuController.CategoryType.Weapon);
        EquipItem(item, selectedSlot);
    }

    internal void EquipToArmor(ItemDataInstance item)
    {
        selectedSlot = FindEmptySlot(MainMenuController.CategoryType.Armor, item._definition.ItemSubType);
        EquipItem(item, selectedSlot);
    }

    internal void EquipToAmmo(ItemDataInstance item)
    {
        selectedSlot = FindEmptySlot(MainMenuController.CategoryType.Ammo, item._definition.ItemSubType);
        EquipItem(item, selectedSlot);
    }

    internal void EquipToMagicItem(ItemDataInstance item)
    {
        selectedSlot = FindEmptySlot(MainMenuController.CategoryType.MagicItems);
        EquipItem(item, selectedSlot);
    }
    #endregion

    #region Utils
    private EquipmentSlot FindEmptySlot(MainMenuController.CategoryType categoryType,
    MainMenuController.ItemType itemType = MainMenuController.ItemType.None)
    {
        foreach (var slot in equipmentSlots.Values)
        {

            if (itemType == MainMenuController.ItemType.None &&
            slot.category == categoryType &&
            slot.IsEmpty())
            {
                return slot;
            }
            else
            {
                switch (itemType)
                {
                    case MainMenuController.ItemType.Helmet when slot.button.name == "Helmet":
                    case MainMenuController.ItemType.Chestplate when slot.button.name == "Chestplate":
                    case MainMenuController.ItemType.Leggings when slot.button.name == "Leggings":
                    case MainMenuController.ItemType.Gauntlets when slot.button.name.StartsWith("Gauntlets") && (slot.IsEmpty() || slot.button.name == "GauntletsR"):
                    case MainMenuController.ItemType.Arrows when (slot.button.name == "Arrows1" || slot.button.name == "Arrows2"):
                    case MainMenuController.ItemType.Bolts when (slot.button.name == "Bolts1" || slot.button.name == "Bolts2"):
                        return slot;
                }
            }

        }
        // If no empty slot found, return the first slot in this category
        foreach (var slot in equipmentSlots.Values)
        {
            if (slot.category == categoryType)
            {
                return slot;
            }
        }
        return null;
    }
    #endregion

    #region Equipment Slot Class
    public class EquipmentSlot
    {
        public Button button;
        public MainMenuController.CategoryType category;
        public ItemDataInstance item;
        private string name;

        public EquipmentSlot(Button button, MainMenuController.CategoryType category, ItemDataInstance item, string name)
        {
            this.button = button;
            this.category = category;
            this.item = item;
            this.name = name;
        }

        public void SetItem(ItemDataInstance newItem)
        {
            item = newItem;
        }

        public bool HasItem()
        {
            return item?._definition != null;
        }

        public bool IsEmpty()
        {
            return item == null;
        }

        public string GetName()
        {
            return name;
        }
    }
    #endregion
}