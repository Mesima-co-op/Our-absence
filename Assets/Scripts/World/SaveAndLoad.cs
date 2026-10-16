using UnityEngine;
using Newtonsoft.Json;
using System.IO;
using System;
using System.Collections.Generic;

public static class SaveAndLoad
{
    /// <summary>Player inventory and position for the active save slot.</summary>
    public static string savePathPlayerState => SaveSlots.PlayerStatePath(SaveSlots.CurrentSlot);

    /// <summary>Pickable-item state of one scene for the active save slot.</summary>
    public static string SceneStatePath(string sceneName) => SaveSlots.SceneStatePath(SaveSlots.CurrentSlot, sceneName);

    public static T Load<T>(string path, bool quietIfMissing = false) where T : class, new()
    {
        if (!File.Exists(path))
        {
            if (!quietIfMissing) Debug.Log($"No save file at {path} (new game or new area).");
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to load save {path}: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Writes to a temporary file first and then swaps it in, so a crash or power loss
    /// mid-write never leaves a half-written (corrupt) save behind.
    /// </summary>
    public static void Save<T>(string path, T data)
    {
        string tmp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(tmp, json);

            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to save data to {path}: {ex}");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort cleanup */ }
        }
    }
}
