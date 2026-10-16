using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;
using System.Security.Cryptography;
using System;

public class MainMenuController : MonoBehaviour
{
    [Header("Debug")]
    private DebugMenu debugMenu = new DebugMenu(false);

    [Header("Dependencies")]
    private ObjectsState globalVars;

    [Header("Navigation Buttons")]
    private Button inventoryButton, equipmentButton, settingsButton;
    private Button selectedButton;

    [Header("View Containers")]
    private VisualElement inventoryView, equipmentView;

    [Header("Inventory UI")]
    private ScrollView itemsCategories;
    private ListView itemsList;
    private VisualElement optionMenu;
    private VisualElement clickCatcher;

    [Header("Data")]
    private List<ItemsCategory> categories = new List<ItemsCategory>();
    private ItemsCategory selectedCategory;
    private ItemDataInstance selectedItem;

    [Header("Equipment Integration")]
    private EquipmentControl currentEquipmentControl;
    [Header("templets")]
    public Texture2D itemTemp;
    public Texture2D itemTempHover;
    public Font myFont;

    #region Unity Lifecycle
    void Start()
    {
        InitializeDependencies();
        InitializeUI();
        InitializeData();
        PopulateCategories();
        SetInitialView();
    }

    void Update()
    {
        HandleMenuFocus();
    }
    #endregion

    #region Initialization
    private void InitializeDependencies()
    {
        globalVars = FindObjectOfType<ObjectsState>();
        currentEquipmentControl = FindAnyObjectByType<EquipmentControl>();

        if (globalVars == null)
        {
            Debug.LogError("MainMenuController: ObjectsState not found!");
        }
    }

    private void InitializeUI()
    {
        if (globalVars?.mainMenuUI?.rootVisualElement == null)
        {
            Debug.LogError("MainMenuController: UI root element not found!");
            return;
        }

        var root = globalVars.mainMenuUI.rootVisualElement;

        // Get navigation buttons
        inventoryButton = root.Q<Button>("Inventory");
        equipmentButton = root.Q<Button>("Equipment");
        settingsButton = root.Q<Button>("Settings");

        // Get view containers
        inventoryView = root.Q<VisualElement>("InventoryView");
        equipmentView = root.Q<VisualElement>("EquipmentView");

        // Get inventory UI elements
        itemsCategories = root.Q<ScrollView>("ItemsCategories");
        itemsList = root.Q<ListView>("ItemsList");

        // Register button events
        inventoryButton.clicked += OnInventoryButtonClicked;
        equipmentButton.clicked += OnEquipmentButtonClicked;
        settingsButton.clicked += OnSettingsButtonClicked;
        AddSaveAndQuitButton(settingsButton);

        SetSelectedOption("");
    }

    /// <summary>Adds "Save & Quit" under Settings, styled like the other menu options.</summary>
    private void AddSaveAndQuitButton(Button styleSource)
    {
        if (styleSource == null || styleSource.parent == null) return;

        var saveQuit = new Button(OnSaveAndQuitClicked) { name = "SaveAndQuit", text = " Save & Quit" };
        foreach (var cls in styleSource.GetClasses()) saveQuit.AddToClassList(cls);
        saveQuit.style.unityTextAlign = TextAnchor.MiddleLeft;
        saveQuit.style.unityFont = myFont != null ? myFont : Resources.Load<Font>("Fonts/WashingtonText");
        saveQuit.style.unityFontDefinition = StyleKeyword.Initial;
        saveQuit.style.color = new Color(0.7f, 0.7f, 0.7f, 1);
        saveQuit.style.backgroundColor = new Color(0, 0, 0, 0);
        saveQuit.style.height = Length.Percent(10);
        saveQuit.style.borderLeftColor = saveQuit.style.borderRightColor =
            saveQuit.style.borderTopColor = saveQuit.style.borderBottomColor = new Color(0, 0, 0, 0);
        styleSource.parent.Insert(styleSource.parent.IndexOf(styleSource) + 1, saveQuit);
    }

    private void OnSaveAndQuitClicked()
    {
        GameSave.SaveGame();
        if (globalVars != null)
        {
            globalVars.menuOpen = false;
            globalVars.mainMenuUI.rootVisualElement.style.display = DisplayStyle.None;
        }
        SceneLoader.Load(SceneLoader.StartMenuScene);
    }

    private void SetInitialView()
    {
        selectedButton = inventoryButton;
        ShowInventoryView();
    }

    private void InitializeData()
    {
        categories.Clear();
        categories.Add(new ItemsCategory
        {
            categoryName = "Weapons",
            categoryType = CategoryType.Weapon,
            items = new List<ItemDataInstance>()
        });

        categories.Add(new ItemsCategory
        {
            categoryName = "Armor",
            categoryType = CategoryType.Armor,
            items = new List<ItemDataInstance>()
        });

        categories.Add(new ItemsCategory
        {
            categoryName = "Ammunition",
            categoryType = CategoryType.Ammo,
            items = new List<ItemDataInstance>()
        });

        categories.Add(new ItemsCategory
        {
            categoryName = "Magic Items",
            categoryType = CategoryType.MagicItems,
            items = new List<ItemDataInstance>()
        });

        categories.Add(new ItemsCategory
        {
            categoryName = "Quick Items",
            categoryType = CategoryType.EquipmentItems,
            items = new List<ItemDataInstance>()
        });

        categories.Add(new ItemsCategory
        {
            categoryName = "Miscellaneous",
            categoryType = CategoryType.Misc,
            items = new List<ItemDataInstance>()
        });
    }
    #endregion

    #region Navigation
    public void OnInventoryButtonClicked()
    {
        ShowInventoryView();
        currentEquipmentControl = FindAnyObjectByType<EquipmentControl>();
        SetSelectedOption("Inventory");
        debugMenu.DebugLog("Switched to Inventory view");
    }

    public void OnEquipmentButtonClicked()
    {
        ShowEquipmentView();
        SetSelectedOption("Equipment");
        debugMenu.DebugLog("Switched to Equipment view");
    }

    private void OnSettingsButtonClicked()
    {
        ShowSettingsView();
        SetSelectedOption("Setting");
        debugMenu.DebugLog("Switched to Settings view");
    }

    public void SetSelectedOption(string option)
    {
        switch (option)
        {
            case "Setting":
                inventoryButton.style.color = new Color(0.7f, 0.7f, 0.7f, 1);
                equipmentButton.style.color = new Color(0.7f, 0.7f, 0.7f, 1);
                settingsButton.style.color = Color.yellow;
                break;
            case "Equipment":
                inventoryButton.style.color = new Color(0.7f, 0.7f, 0.7f, 1);
                equipmentButton.style.color = Color.yellow;
                settingsButton.style.color = new Color(0.7f, 0.7f, 0.7f, 1);
                break;
            case "Inventory":
                inventoryButton.style.color = Color.yellow;
                equipmentButton.style.color = new Color(0.7f, 0.7f, 0.7f, 1);
                settingsButton.style.color = new Color(0.7f, 0.7f, 0.7f, 1);
                break;
            default:
                inventoryButton.style.color = Color.yellow;
                equipmentButton.style.color = new Color(0.7f, 0.7f, 0.7f, 1);
                settingsButton.style.color = new Color(0.7f, 0.7f, 0.7f, 1);
                break;

        }
    }

    private void ShowInventoryView()
    {
        inventoryView.style.visibility = Visibility.Visible;
        equipmentView.style.visibility = Visibility.Hidden;
    }

    private void ShowEquipmentView()
    {
        inventoryView.style.visibility = Visibility.Hidden;
        equipmentView.style.visibility = Visibility.Visible;
    }

    private void ShowSettingsView()
    {
        inventoryView.style.visibility = Visibility.Hidden;
        equipmentView.style.visibility = Visibility.Hidden;
        // Settings view would be implemented here
    }

    private void HandleMenuFocus()
    {
        if (globalVars != null && globalVars.menuOpen && selectedButton != null)
        {
            selectedButton.Focus();
            selectedButton = null;
        }
    }
    #endregion

    #region Category Management
    private void PopulateCategories()
    {
        if (itemsCategories == null) return;

        itemsCategories.Clear();

        foreach (var category in categories)
        {
            var button = new Button(() => SelectCategory(category))
            {
                text = $"{category.categoryName} ({category.items.Count})"
            };

            // ⭐ Apply custom background image
            button.style.backgroundImage = new StyleBackground(itemTemp);

            // ⭐ Background color tint (optional)
            button.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.7f);

            // ⭐ Custom font
            button.style.unityFontDefinition = new StyleFontDefinition(myFont);
            button.style.fontSize = 18;
            button.style.color = Color.white;

            // ⭐ Optional: padding, border, radius
            button.style.paddingLeft = 8;
            button.style.paddingRight = 8;
            button.style.borderTopLeftRadius = 6;
            button.style.borderTopRightRadius = 6;
            button.style.borderBottomLeftRadius = 6;
            button.style.borderBottomRightRadius = 6;


            itemsCategories.Add(button);
        }
    }

    private void SelectCategory(ItemsCategory category)
    {
        selectedCategory = category;
        debugMenu.DebugLog($"Selected category: {category.categoryName} with {category.items.Count} items");
        PopulateItems(category.items);
    }

    public ItemsCategory GetCategoryByType(CategoryType categoryType)
    {
        return categories.FirstOrDefault(c => c.categoryType == categoryType);
    }
    #endregion

    #region Item List Management
    private void PopulateItems(List<ItemDataInstance> items, bool isEquipmentMode = false)
    {
        if (itemsList == null) return;

        selectedItem = null;
        itemsList.itemsSource = new List<ItemDataInstance>(items);
        if (isEquipmentMode && currentEquipmentControl != null)
        {
            var allowedTypes = currentEquipmentControl.GetAllowedItemTypes();
            itemsList.itemsSource = items.Where(item => !ReferenceEquals(item, null) &&
                   ((IEnumerable<ItemType>)allowedTypes).Contains(item._definition.ItemSubType))
            .ToList();
        }

        itemsList.fixedItemHeight = 85;
        itemsList.makeItem = () =>
        {
            var button = new Button();
            button.style.flexDirection = FlexDirection.Row;
            button.style.alignItems = Align.Center;
            button.style.width = Length.Percent(100);
            button.style.marginBottom = 6;
            button.style.borderTopWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            button.style.borderRightWidth = 0;


            // Icon
            var icon = new VisualElement();
            icon.name = "icon";
            icon.style.width = 74;
            icon.style.height = 74;
            icon.style.marginTop = 5;
            icon.style.backgroundPositionX = BackgroundPropertyHelper.ConvertScaleModeToBackgroundPosition(ScaleMode.StretchToFill);
            icon.style.backgroundPositionY = BackgroundPropertyHelper.ConvertScaleModeToBackgroundPosition(ScaleMode.StretchToFill);
            icon.style.backgroundRepeat = BackgroundPropertyHelper.ConvertScaleModeToBackgroundRepeat(ScaleMode.StretchToFill);
            icon.style.backgroundSize = BackgroundPropertyHelper.ConvertScaleModeToBackgroundSize(ScaleMode.StretchToFill);

            // Text
            var label = new Label();
            label.name = "label";
            label.style.flexGrow = 1;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.height = Length.Percent(100);
            //label.style.unityTextOutlineColor = new Color(1f, 0.35f, 0f, 1f);
            //label.style.unityTextOutlineWidth = 0.1f;
            label.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);

            // Hover start
            button.RegisterCallback<PointerEnterEvent>(evt =>
            {
                button.style.backgroundImage = itemTempHover;
            });

            // Hover end
            button.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                button.style.backgroundImage = itemTemp;
            });


            button.Add(icon);
            button.Add(label);

            return button;
        };

        itemsList.bindItem = (element, index) =>
        {
            if (index >= itemsList.itemsSource.Count) return;

            var item = (ItemDataInstance)itemsList.itemsSource[index];

            var button = element as Button;
            var icon = button.Q<VisualElement>("icon");
            var label = button.Q<Label>("label");

            label.text = item._definition.ItemName;
            label.style.unityFontDefinition = new StyleFontDefinition(myFont);

            icon.style.backgroundImage = item._definition.Icon;

            button.style.backgroundImage = itemTemp;
            button.style.backgroundColor = new Color(0f, 0f, 0f, 0f);


            // Clear previous event handlers
            button.clicked -= null;

            if (isEquipmentMode)
            {
                button.clicked += () => HandleEquipmentModeItemClick(item);
            }
            else
            {
                button.clicked += () => HandleNormalItemClick(item, button);
            }
        };

        itemsList.Rebuild();
    }

    private void HandleNormalItemClick(ItemDataInstance item, Button button)
    {
        selectedItem = item;
        debugMenu.DebugLog($"Selected item: {item._definition.ItemName}");

        var worldPos = button.worldBound.position;
        var menuPos = worldPos + new Vector2(0, button.resolvedStyle.height);

        ShowItemOptionMenu(menuPos, item, button);
    }

    private void HandleEquipmentModeItemClick(ItemDataInstance item)
    {
        if (currentEquipmentControl != null)
        {
            currentEquipmentControl.EquipItem(item, currentEquipmentControl.selectedSlot);
            OnEquipmentButtonClicked(); // Return to equipment view
        }
    }
    #endregion

    #region Equipment Integration
    public void QuickEquipment(ItemDataInstance item)
    {
        switch (item._definition.ItemSubType)
        {
            case ItemType.QuickItem:
                currentEquipmentControl?.EquipToQuickItem(item);
                break;
            case ItemType.LightWeapon:
            case ItemType.HeavyWeapon:
            case ItemType.Bow:
            case ItemType.Crossbow:
                currentEquipmentControl?.EquipToWeapon(item);
                break;
            case ItemType.Helmet:
            case ItemType.Chestplate:
            case ItemType.Leggings:
            case ItemType.Gauntlets:
                currentEquipmentControl?.EquipToArmor(item);
                break;
            case ItemType.Arrows:
            case ItemType.Bolts:
                currentEquipmentControl?.EquipToAmmo(item);
                break;
            case ItemType.MagicItem:
                currentEquipmentControl?.EquipToMagicItem(item);
                break;
            default:
                debugMenu.DebugLog($"No quick equip slot for item type: {item._definition.ItemSubType}");
                break;
        }
        OnEquipmentButtonClicked();
    }
    public void OpenInventoryForEquipment(EquipmentControl equipmentControl, CategoryType categoryType)
    {
        currentEquipmentControl = equipmentControl;
        ShowInventoryView();

        var category = GetCategoryByType(categoryType);
        if (category != null)
        {
            SelectCategory(category);
            PopulateItems(category.items, true); // Equipment mode
        }

        debugMenu.DebugLog($"Opened inventory for equipment: {categoryType}");
    }

    public void RemoveItemFromInventory(ItemDataInstance item)
    {
        foreach (var category in categories)
        {
            if (category.items.Remove(item))
            {
                debugMenu.DebugLog($"Removed {item._definition.ItemName} from inventory");
                RefreshCurrentView();
                return;
            }
        }
    }

    public void ReturnItemToInventory(ItemDataInstance item)
    {
        var itemCategory = GetItemCategory(item._definition.ItemSubType);
        var category = GetCategoryByType(itemCategory);

        if (category != null)
        {
            category.items.Add(item);
            debugMenu.DebugLog($"Returned {item._definition.ItemName} to inventory");
            RefreshCurrentView();
        }
    }

    private CategoryType GetItemCategory(ItemType itemType)
    {
        return itemType switch
        {
            ItemType.LightWeapon or ItemType.HeavyWeapon or ItemType.Bow or ItemType.Crossbow => CategoryType.Weapon,
            ItemType.Helmet or ItemType.Chestplate or ItemType.Leggings or ItemType.Gauntlets => CategoryType.Armor,
            ItemType.Arrows or ItemType.Bolts => CategoryType.Ammo,
            ItemType.MagicItem => CategoryType.MagicItems,
            ItemType.QuickItem => CategoryType.EquipmentItems,
            _ => CategoryType.Misc
        };
    }

    private void RefreshCurrentView()
    {
        PopulateCategories();
        if (selectedCategory != null)
        {
            PopulateItems(selectedCategory.items, currentEquipmentControl != null);
        }
    }
    #endregion

    #region Item Option Menu
    private void ShowItemOptionMenu(Vector2 position, ItemDataInstance item, VisualElement ownerButton)
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
                left = position.x,
                top = position.y,
                backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f),
                paddingLeft = 8, paddingRight = 8,
                paddingTop = 6, paddingBottom = 6,
                borderTopLeftRadius = 4, borderTopRightRadius = 4,
                borderBottomLeftRadius = 4, borderBottomRightRadius = 4,
                borderLeftWidth = 1, borderRightWidth = 1,
                borderTopWidth = 1, borderBottomWidth = 1,
                borderLeftColor = Color.gray, borderRightColor = Color.gray,
                borderTopColor = Color.gray, borderBottomColor = Color.gray
            }
        };

        // Add menu options
        var equipButton = new Button(() =>
        {

            debugMenu.DebugLog($"Quick equip {item._definition.ItemName}");
            QuickEquipment(item);

            ClearOptionMenu();
        })
        { text = "Quick Equip" };

        var unequipButton = new Button(() =>
        {
            if (currentEquipmentControl != null)
            {
                currentEquipmentControl.UnequipItem(currentEquipmentControl.GetSlot(item.equippedSlotName));
                debugMenu.DebugLog($"Unequipped {item._definition.ItemName}");
            }
            ClearOptionMenu();
        })
        { text = "Unequip" };


        var removeButton = new Button(() =>
        {
            var player = FindObjectOfType<Player_controller>();
            if (player != null)
            {

                if (item._definition.ObjectRef != null)
                {
                    var spawnPosition = player.transform.position + player.transform.forward + Vector3.up * 0.5f;
                    var droppedItem = Instantiate(item._definition.ObjectRef, spawnPosition, Quaternion.identity);
                    debugMenu.DebugLog($"Dropped {item._definition.ItemName} on the ground at {spawnPosition}");
                }
                else
                {
                    debugMenu.DebugLog($"Prefab not found for item: {item._definition.ItemName}");
                }
            }
            else
            {
                debugMenu.DebugLog("Player not found in scene.");
            }
            RemoveItemFromInventory(item);
            if (item.equippedSlotName != null)
                currentEquipmentControl.UnequipItem(currentEquipmentControl.GetSlot(item.equippedSlotName));
            ClearOptionMenu();
        })
        { text = "Drop" };

        var infoButton = new Button(() =>
        {
            ShowItemInfo(item);
            ClearOptionMenu();
        })
        { text = "Info" };

        if (item.equippedSlotName == null || item.equippedSlotName == "") optionMenu.Add(equipButton); else optionMenu.Add(unequipButton);
        optionMenu.Add(removeButton);
        optionMenu.Add(infoButton);

        root.Add(optionMenu);

        // Handle outside clicks
        clickCatcher.RegisterCallback<ClickEvent>(evt =>
        {
            if (!optionMenu.worldBound.Contains(evt.position) &&
                !ownerButton.worldBound.Contains(evt.position))
            {
                selectedItem = null;
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

    private void ShowItemInfo(ItemDataInstance item)
    {
        debugMenu.DebugLog($"Item Info - Name: {item._definition.ItemName}, Type: {item._definition.ItemSubType}, ID: {item._definition.ItemID}");
        // Could open a detailed info panel here
    }
    #endregion

    #region Public API
    public ItemDataInstance GetSelectedItem()
    {
        return selectedItem;
    }

    public void AddItemToInventory(ItemDataInstance item)
    {
        var itemCategory = GetItemCategory(item._definition.ItemSubType);
        var category = GetCategoryByType(itemCategory);

        if (category != null)
        {
            category.items.Add(item);
            RefreshCurrentView();
        }
    }

    public void InitializeSavedItemsInInventory(List<ItemDataInstance> items)
    {
        debugMenu.DebugLog($"Initializing inventory with {items.Count} saved items");
        foreach (var item in items)
        {
            AddItemToInventory(item);
        }
    }

    public List<ItemsCategory> GetAllInventory()
    {
        return categories;
    }

    public EquipmentControl GetEquipmentControl()
    {
        return currentEquipmentControl;
    }
    public bool HasItem(int itemID)
    {
        return categories.Any(category => category.items.Any(item => item._definition.ItemID == itemID));
    }

    public ItemDataInstance GetItemByID(int itemID)
    {
        foreach (var category in categories)
        {
            var item = category.items.FirstOrDefault(i => i._definition.ItemID == itemID);
            if (item != null) return item;
        }
        return null;
    }
    #endregion

    #region Data Classes
    [System.Serializable]
    public class ItemsCategory
    {
        public string categoryName;
        public CategoryType categoryType;
        public List<ItemDataInstance> items = new List<ItemDataInstance>();
    }

    [System.Serializable]
    public class DebugMenu
    {
        public bool toDebug = true;

        public DebugMenu(bool debug = true)
        {
            toDebug = debug;
        }

        public void DebugLog(string message)
        {
            if (toDebug)
            {
                Debug.Log($"[MainMenuController] {message}");
            }
        }
    }
    #endregion

    #region Enums
    public enum CategoryType
    {
        Weapon,
        Armor,
        MagicItems,
        Ammo,
        Misc,
        QuestItems,
        EquipmentItems
    }

    public enum ItemType
    {
        None,
        LightWeapon,
        HeavyWeapon,
        Bow,
        Crossbow,
        Helmet,
        Chestplate,
        Leggings,
        Gauntlets,
        Arrows,
        Bolts,
        MagicItem,
        QuickItem,
        Potion
    }
    #endregion
}