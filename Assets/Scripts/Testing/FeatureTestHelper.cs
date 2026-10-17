using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Feature Test Lab helper: isolates saves in the hidden test slot and adds debug hotkeys
/// plus an on-screen panel. Only meant for test scenes.
///
///   F1  show/hide panel     H  heal to full        K  kill the player (tests death + reload)
///   G   give all items      P  back to spawn       M  complete the test mission (NPC dialogue)
///   O   cycle outfits on the player (naked → peasant → ranger)
///   L   level up: earns one level's experience and spends it on the next attribute in turn
///   F9  wipe the test save and reload the lab
/// </summary>
[DefaultExecutionOrder(-1000)] // must pick the test slot before SavePlayerData loads anything
public class FeatureTestHelper : MonoBehaviour
{
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private string testMissionId = "test_mission";
    [TextArea(4, 12)]
    [SerializeField] private string stationNotes = "";

    private bool showPanel = true;
    private string lastAction = "";
    private float lastActionTime;
    private GUIStyle boxStyle, labelStyle;

    private void Awake()
    {
        // Keep testing away from the player's real save slots
        SaveSlots.BeginSession(SaveSlots.TestSlot);
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.f1Key.wasPressedThisFrame) showPanel = !showPanel;
        if (kb.hKey.wasPressedThisFrame) Heal();
        if (kb.kKey.wasPressedThisFrame) Kill();
        if (kb.gKey.wasPressedThisFrame) GiveAllItems();
        if (kb.pKey.wasPressedThisFrame) BackToSpawn();
        if (kb.mKey.wasPressedThisFrame) CompleteTestMission();
        if (kb.f9Key.wasPressedThisFrame) ResetTestSave();
        if (kb.oKey.wasPressedThisFrame) CycleOutfit();
        if (kb.lKey.wasPressedThisFrame) LevelUp();
    }

    // ── Actions ─────────────────────────────────────────────────────────

    private static HealthSystem PlayerHealth()
    {
        var player = FindObjectOfType<Player_controller>();
        return player != null ? player.GetComponent<HealthSystem>() : null;
    }

    private void Heal()
    {
        var health = PlayerHealth();
        if (health == null || health.IsDead) return;
        health.Heal(health.MaxHealth);
        Report("Healed to full");
    }

    private void Kill()
    {
        var health = PlayerHealth();
        if (health == null || health.IsDead) return;
        health.TakeDamage(health.Health);
        Report("Player killed: the lab reloads in a few seconds");
    }

    private void GiveAllItems()
    {
        var menu = FindObjectOfType<MainMenuController>();
        if (menu == null || !ItemsManager.isLoaded)
        {
            Report("Items not ready yet");
            return;
        }

        foreach (var definition in ItemsManager.allItems)
        {
            if (definition == null) continue;
            var go = new GameObject("Item_" + definition.ItemID) { hideFlags = HideFlags.HideInHierarchy };
            var item = go.AddComponent<ItemDataInstance>();
            item._definition = definition;
            item.itemID = definition.ItemID;
            item.currentDurability = definition.MaxDurability;
            menu.AddItemToInventory(item);
        }
        Report($"Added {ItemsManager.allItems.Count} item(s) to the inventory (open it with I)");
    }

    private int nextAttribute;

    /// <summary>Tests leveling: one level's worth of experience, spent on Vigor, Endurance, Strength, Agility in turn.</summary>
    private void LevelUp()
    {
        var player = FindObjectOfType<Player_controller>();
        var stats = player != null ? player.GetComponent<CharacterStats>() : null;
        if (stats == null)
        {
            Report("The player has no CharacterStats");
            return;
        }
        var attribute = (CharacterAttribute)(nextAttribute++ % 4);
        stats.AddExperience(stats.ExperienceToNextLevel);
        stats.LevelUp(attribute);
        Report($"Level {stats.Level}: +1 {attribute}  (HP {stats.MaxHealth}, stamina {stats.MaxStamina:0}, damage x{stats.DamageMultiplier:0.00}, costs x{stats.StaminaCostMultiplier:0.00})");
    }

    private int outfitIndex;

    /// <summary>Quick look at the clothing without the menus: naked → peasant set → ranger set.</summary>
    private void CycleOutfit()
    {
        var player = FindObjectOfType<Player_controller>();
        var outfit = player != null ? player.GetComponent<CharacterOutfit>() : null;
        if (outfit == null)
        {
            Report("The player has no CharacterOutfit (not Player_Base)");
            return;
        }
        if (!ItemsManager.isLoaded)
        {
            Report("Items not ready yet");
            return;
        }

        string[] sets = { "", "Peasant", "Ranger" };
        outfitIndex = (outfitIndex + 1) % sets.Length;
        outfit.TakeOffAll();
        foreach (var definition in ItemsManager.allItems)
        {
            var piece = definition != null ? definition.Wearable : null;
            if (piece != null && sets[outfitIndex] != "" && piece.name.StartsWith(sets[outfitIndex]))
                outfit.Wear(piece);
        }
        Report(outfitIndex == 0 ? "Outfit: naked" : $"Outfit: {sets[outfitIndex]} set (preview only, not equipped in the menu)");
    }

    private void BackToSpawn()
    {
        var player = FindObjectOfType<Player_controller>();
        if (player == null || spawnPoint == null) return;

        var controller = player.GetComponent<CharacterController>();
        if (controller) controller.enabled = false;
        player.transform.SetPositionAndRotation(spawnPoint.position + Vector3.up * 0.2f, spawnPoint.rotation);
        if (controller) controller.enabled = true;
        Report("Back at spawn");
    }

    private void CompleteTestMission()
    {
        NPCSystem.CompleteMission(testMissionId);
        Report($"Mission '{testMissionId}' completed: talk to the NPC again");
    }

    private void ResetTestSave()
    {
        SaveSlots.Delete(SaveSlots.TestSlot);
        SceneLoader.Load(gameObject.scene.name);
        Report("Test save wiped");
    }

    private void Report(string message)
    {
        lastAction = message;
        lastActionTime = Time.unscaledTime;
        Debug.Log($"[Test Lab] {message}");
    }

    // ── Panel ───────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (boxStyle == null)
        {
            boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8) };
            labelStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13, wordWrap = true };
        }

        if (!showPanel)
        {
            GUI.Label(new Rect(Screen.width - 170, 8, 160, 24), "<b>F1</b>  Test Lab panel", labelStyle);
            return;
        }

        var health = PlayerHealth();
        string hp = health != null ? $"{health.Health}/{health.MaxHealth}" : "n/a";
        var stamina = health != null ? health.GetComponent<StaminaSystem>() : null;
        string sp = stamina != null ? $"   ·   stamina {Mathf.CeilToInt(stamina.Stamina)}/{stamina.MaxStamina:0}" : "";
        var stats = health != null ? health.GetComponent<CharacterStats>() : null;
        string lv = stats != null
            ? $"Lv {stats.Level}  VIG {stats.Get(CharacterAttribute.Vigor)}  END {stats.Get(CharacterAttribute.Endurance)}  " +
              $"STR {stats.Get(CharacterAttribute.Strength)}  AGI {stats.Get(CharacterAttribute.Agility)}  XP {stats.Experience}/{stats.ExperienceToNextLevel}\n"
            : "";
        string text =
            "<b>FEATURE TEST LAB</b>   (F1 hide)\n" +
            $"HP {hp}{sp}   ·   save slot: <b>test</b> (real saves untouched)\n" + lv + "\n" +
            "<b>H</b> heal   <b>K</b> kill   <b>G</b> give all items   <b>P</b> to spawn\n" +
            "<b>M</b> complete test mission   <b>F9</b> wipe test save   <b>O</b> outfits   <b>L</b> level up\n" +
            "<b>F5</b> save   <b>I/Esc</b> menu (Save & Quit)   <b>E</b> pick up / talk\n" +
            (string.IsNullOrEmpty(stationNotes) ? "" : "\n" + stationNotes) +
            (Time.unscaledTime - lastActionTime < 5f && lastAction != "" ? $"\n\n<color=#f0c070>{lastAction}</color>" : "");

        const float width = 380f;
        float height = labelStyle.CalcHeight(new GUIContent(text), width - 20) + 18;
        var rect = new Rect(Screen.width - width - 10, 10, width, height);
        GUI.Box(rect, GUIContent.none, boxStyle);
        GUI.Label(new Rect(rect.x + 10, rect.y + 8, width - 20, height - 10), text, labelStyle);
    }
}
