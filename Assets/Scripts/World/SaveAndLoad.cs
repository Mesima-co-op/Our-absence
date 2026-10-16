using UnityEngine;
using Newtonsoft.Json;
using System.IO;
using System;
using System.Collections.Generic;

[System.Serializable]
public static class SaveAndLoad
{
    public static string savePathPlayerState { get; private set; } = Application.persistentDataPath + "/player_state.babczyk";
    public static string savePathSceneState { get; private set; } = Application.persistentDataPath + "/scene_state.babczyk";



    public static T Load<T>(string path) where T : class, new()
    {
        if (!File.Exists(path))
        {
            Debug.LogWarning($"Save file not found at {path}");
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to load save: {ex}");
            return null;
        }
    }

    public static void Save<T>(string path, T data)
    {
        try
        {
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to save data: {ex}");
        }
    }


}
