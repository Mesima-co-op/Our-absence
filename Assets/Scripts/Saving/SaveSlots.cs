using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Save slots on disk:
///   persistentDataPath/saves/slot_N/meta.json             - what the start menu shows
///   persistentDataPath/saves/slot_N/player_state.babczyk  - inventory and position
///   persistentDataPath/saves/slot_N/scene_{Scene}.babczyk - pickable items per scene
/// The active slot is chosen in the start menu. Playing a scene directly from the editor uses slot 0.
/// </summary>
public static class SaveSlots
{
    public const int SlotCount = 3;

    /// <summary>Hidden slot used by the Feature Test Lab; never listed in the start menu.</summary>
    public const int TestSlot = 99;

    public static int CurrentSlot { get; private set; } = 0;

    static string Root => Path.Combine(Application.persistentDataPath, "saves");
    public static string SlotDirectory(int slot) => Path.Combine(Root, $"slot_{slot}");
    public static string PlayerStatePath(int slot) => Path.Combine(SlotDirectory(slot), "player_state.babczyk");
    public static string SceneStatePath(int slot, string sceneName) => Path.Combine(SlotDirectory(slot), $"scene_{sceneName}.babczyk");
    static string MetaPath(int slot) => Path.Combine(SlotDirectory(slot), "meta.json");

    // Play time = time stored in the slot + time spent in this session
    static double playTimeAtSessionStart;
    static float sessionStartRealtime;
    static bool sessionStarted;

    public static double CurrentPlayTime
    {
        get
        {
            EnsureSession();
            return playTimeAtSessionStart + (Time.realtimeSinceStartup - sessionStartRealtime);
        }
    }

    /// <summary>Scenes started straight from the editor never went through the menu: use slot 0.</summary>
    static void EnsureSession()
    {
        if (!sessionStarted) BeginSession(CurrentSlot);
    }

    [Serializable]
    public class SaveMeta
    {
        public int slot;
        public string sceneName;
        public string savedAtUtc;      // ISO 8601
        public double playTimeSeconds;

        public DateTime SavedAtLocal => DateTime.TryParse(savedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
            ? t.ToLocalTime() : DateTime.MinValue;
    }

    /// <summary>Makes <paramref name="slot"/> the slot every save and load goes to, and starts the play-time clock.</summary>
    public static void BeginSession(int slot)
    {
        CurrentSlot = slot == TestSlot ? TestSlot : Mathf.Clamp(slot, 0, SlotCount - 1);
        sessionStarted = true;
        playTimeAtSessionStart = GetMeta(CurrentSlot)?.playTimeSeconds ?? 0;
        sessionStartRealtime = Time.realtimeSinceStartup;
    }

    /// <summary>Wipes the slot and starts a fresh game in it.</summary>
    public static void CreateNew(int slot, string startScene)
    {
        Delete(slot);
        Directory.CreateDirectory(SlotDirectory(slot));
        BeginSession(slot);
        WriteMeta(startScene);
    }

    public static bool HasSave(int slot) => File.Exists(MetaPath(slot));

    public static SaveMeta GetMeta(int slot) => SaveAndLoad.Load<SaveMeta>(MetaPath(slot), quietIfMissing: true);

    /// <summary>The slot that was saved most recently, or -1 when there are no saves.</summary>
    public static int MostRecentSlot()
    {
        int best = -1;
        DateTime bestTime = DateTime.MinValue;
        for (int i = 0; i < SlotCount; i++)
        {
            var meta = GetMeta(i);
            if (meta != null && meta.SavedAtLocal >= bestTime)
            {
                best = i;
                bestTime = meta.SavedAtLocal;
            }
        }
        return best;
    }

    public static void Delete(int slot)
    {
        try
        {
            var dir = SlotDirectory(slot);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Save] Could not delete slot {slot}: {ex.Message}");
        }
    }

    public static void WriteMeta(string sceneName)
    {
        SaveAndLoad.Save(MetaPath(CurrentSlot), new SaveMeta
        {
            slot = CurrentSlot,
            sceneName = sceneName,
            savedAtUtc = DateTime.UtcNow.ToString("o"),
            playTimeSeconds = CurrentPlayTime,
        });
    }

    /// <summary>
    /// Saves from before slots existed lived directly in persistentDataPath.
    /// Moves them into slot 0 once, so nobody loses progress.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void MigrateLegacySaves()
    {
        try
        {
            string legacyPlayer = Path.Combine(Application.persistentDataPath, "player_state.babczyk");
            string legacyScene = Path.Combine(Application.persistentDataPath, "scene_state.babczyk");
            if (HasSave(0) || (!File.Exists(legacyPlayer) && !File.Exists(legacyScene))) return;

            Directory.CreateDirectory(SlotDirectory(0));
            if (File.Exists(legacyPlayer)) File.Move(legacyPlayer, PlayerStatePath(0));
            if (File.Exists(legacyScene)) File.Move(legacyScene, SceneStatePath(0, "Game"));
            foreach (var file in Directory.GetFiles(Application.persistentDataPath, "scene_state_*.babczyk"))
            {
                string scene = Path.GetFileNameWithoutExtension(file).Substring("scene_state_".Length);
                File.Move(file, SceneStatePath(0, scene));
            }

            var player = SaveAndLoad.Load<SavePlayerData.PlayerSaveData.Data>(PlayerStatePath(0), quietIfMissing: true);
            CurrentSlot = 0;
            WriteMeta(string.IsNullOrEmpty(player?.scene) ? "Game" : player.scene);
            Debug.Log("[Save] Moved existing save files into slot 1.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Save] Legacy save migration failed: {ex.Message}");
        }
    }
}

/// <summary>One call that saves everything in the loaded scenes into the current slot.</summary>
public static class GameSave
{
    public static void SaveGame()
    {
        var player = UnityEngine.Object.FindObjectOfType<SavePlayerData>();
        if (player == null) return; // nothing to save outside gameplay scenes (e.g. the start menu)

        if (!player.Save()) return; // not ready yet, or the player is dead
        foreach (var sceneState in UnityEngine.Object.FindObjectsOfType<SceneManager>())
            sceneState.Save();

        SaveSlots.WriteMeta(player.gameObject.scene.name);
        Debug.Log($"[Save] Saved to slot {SaveSlots.CurrentSlot + 1}.");
    }
}
