using UnityEngine;
using UnityEditor;

namespace LurkingNinja.Utils.Editor
{
    [InitializeOnLoad]
    public static class GameViewExtensions
    {
        private const string PREF_FORCE_GAME_VIEW_FOCUS = "LurkingNinja.ForceGameViewFocus";
        private const string MENU_ITEM = "Tools/LurkingNinja/Force GameView focus";

        static GameViewExtensions()
        {
            EditorApplication.playModeStateChanged += PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += GameViewExtensionsDestructor;
            Menu.SetChecked(MENU_ITEM, GetForceGameView());
        }

        private static void GameViewExtensionsDestructor()
        {
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= GameViewExtensionsDestructor;
        }

        private static void PlayModeChanged(PlayModeStateChange playMode)
        {
            // We only execute anything when we just entered play mode.
            if (playMode != PlayModeStateChange.EnteredPlayMode || !GetForceGameView()) return;

            var gameWindow = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            // We only do the force focus if Focused or Maximized play mode selected.
            // We do not force focus when Unfocused selected.
            if (!gameWindow.maximized && !PlayModeWindow.GetPlayModeFocused()) return;

            gameWindow.Focus();
            gameWindow.SendEvent(new Event
            {
                button = 0,
                clickCount = 1,
                type = EventType.MouseDown,
                mousePosition = gameWindow.rootVisualElement.contentRect.center
            });
        }

        private static bool GetForceGameView() => EditorPrefs.GetBool(PREF_FORCE_GAME_VIEW_FOCUS);

        [MenuItem(MENU_ITEM, false)]
        private static void SetForceGameViewMenu()
        {
            var setting = !GetForceGameView();
            EditorPrefs.SetBool(PREF_FORCE_GAME_VIEW_FOCUS, setting);
            Menu.SetChecked(MENU_ITEM, setting);
        }
    }
}