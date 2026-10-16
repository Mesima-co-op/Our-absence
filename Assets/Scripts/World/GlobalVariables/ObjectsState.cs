using System.Collections;
using System.Collections.Generic;
using System.Net.Security;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class ObjectsState : MonoBehaviour
/// <summary>
/// <para>Manages the state of the MainMenu GameObject and tracks whether the menu is open.</para>
/// <para>Provides access to the MainMenu's UI elements and their states.</para>
/// </summary>
/// <remarks>
/// - Finds the MainMenu GameObject by name at startup.
/// - Updates the <see cref="menuOpen"/> flag each frame based on the MainMenu's active state.
/// </remarks>
{
    public GameObject MainMenu;
    public UIDocument mainMenuUI;
    public bool menuOpen = false;
    public PlayerInput playerInput;

    void OnEnable()
    {
        MainMenu = GameObject.Find("MainMenu");
        mainMenuUI = MainMenu.GetComponentInChildren<UIDocument>();

        mainMenuUI.rootVisualElement.style.display = DisplayStyle.None;
        UnityEngine.Cursor.visible = false;
        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        menuOpen = mainMenuUI.rootVisualElement.style.display == DisplayStyle.Flex;
    }

}
