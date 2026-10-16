using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

/// <summary>
/// Builds Assets/Scenes/StartMenu.unity (title screen) and the overlay PanelSettings used by the
/// loading screen, and makes the start menu the first scene in the build.
/// </summary>
public static class StartMenuSceneBuilder
{
    const string ScenePath = "Assets/Scenes/StartMenu.unity";
    const string BasePanelSettings = "Assets/UI Toolkit/PanelSettings.asset";
    const string OverlayPanelSettings = "Assets/Resources/UI/OverlayPanelSettings.asset";
    const string StartMenuTree = "Assets/Resources/UI/StartMenu.uxml";

    [MenuItem("Our Absence/UI/Build Start Menu Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureOverlayPanelSettings();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cam = new GameObject("Main Camera").AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.gameObject.AddComponent<AudioListener>();

        var menu = new GameObject("Start Menu");
        var doc = menu.AddComponent<UIDocument>();
        doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(BasePanelSettings);
        doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(StartMenuTree);
        menu.AddComponent<StartMenuController>();

        var events = new GameObject("EventSystem");
        events.AddComponent<EventSystem>();
        events.AddComponent<InputSystemUIInputModule>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        MakeFirstBuildScene();
        Debug.Log($"[StartMenu] Built {ScenePath} and set it as the first build scene.");
    }

    static void EnsureOverlayPanelSettings()
    {
        var overlay = AssetDatabase.LoadAssetAtPath<PanelSettings>(OverlayPanelSettings);
        if (overlay == null)
        {
            AssetDatabase.CopyAsset(BasePanelSettings, OverlayPanelSettings);
            overlay = AssetDatabase.LoadAssetAtPath<PanelSettings>(OverlayPanelSettings);
        }
        overlay.sortingOrder = 1000; // above the HUD and in-game menus
        EditorUtility.SetDirty(overlay);
        AssetDatabase.SaveAssets();
    }

    static void MakeFirstBuildScene()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
