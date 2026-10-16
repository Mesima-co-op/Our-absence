using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Title screen: Continue (most recent save), New Game (pick a slot), Load Game (pick or delete a slot), Quit.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class StartMenuController : MonoBehaviour
{
    [Tooltip("Scene a new game starts in.")]
    [SerializeField] private string newGameScene = "TheDescent";

    enum SlotMode { NewGame, LoadGame }

    VisualElement mainPanel, slotsPanel, confirmPanel, slotList;
    Button continueButton, newGameButton, loadGameButton, quitButton, backButton, confirmYes, confirmNo;
    Label continueDetail, slotsTitle, confirmText;
    SlotMode slotMode;
    Action pendingConfirm;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        mainPanel = root.Q("MainPanel");
        slotsPanel = root.Q("SlotsPanel");
        confirmPanel = root.Q("ConfirmPanel");
        slotList = root.Q("SlotList");
        continueButton = root.Q<Button>("ContinueButton");
        newGameButton = root.Q<Button>("NewGameButton");
        loadGameButton = root.Q<Button>("LoadGameButton");
        quitButton = root.Q<Button>("QuitButton");
        backButton = root.Q<Button>("BackButton");
        confirmYes = root.Q<Button>("ConfirmYes");
        confirmNo = root.Q<Button>("ConfirmNo");
        continueDetail = root.Q<Label>("ContinueDetail");
        slotsTitle = root.Q<Label>("SlotsTitle");
        confirmText = root.Q<Label>("ConfirmText");
        root.Q<Label>("Version").text = $"v{Application.version}";

        continueButton.clicked += OnContinue;
        newGameButton.clicked += OnNewGame;
        loadGameButton.clicked += () => ShowSlots(SlotMode.LoadGame);
        quitButton.clicked += Quit;
        backButton.clicked += ShowMain;
        confirmYes.clicked += () => { var action = pendingConfirm; HideConfirm(); action?.Invoke(); };
        confirmNo.clicked += HideConfirm;
        root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

        Time.timeScale = 1f;
        UnityEngine.Cursor.visible = true;
        UnityEngine.Cursor.lockState = CursorLockMode.None;

        RefreshMain();
        ShowMain();
    }

    // ── Main ─────────────────────────────────────────────────────────────

    void RefreshMain()
    {
        int recent = SaveSlots.MostRecentSlot();
        continueButton.SetEnabled(recent >= 0);
        loadGameButton.SetEnabled(recent >= 0);

        var meta = recent >= 0 ? SaveSlots.GetMeta(recent) : null;
        continueDetail.text = meta != null
            ? $"{SceneLoader.DisplayName(meta.sceneName)}  ·  {FormatPlayTime(meta.playTimeSeconds)}"
            : "";
    }

    void ShowMain()
    {
        slotsPanel.AddToClassList("oa-hidden");
        HideConfirm();
        RefreshMain();
        (continueButton.enabledSelf ? continueButton : newGameButton).Focus();
    }

    void OnContinue()
    {
        int slot = SaveSlots.MostRecentSlot();
        if (slot >= 0) LoadSlot(slot);
    }

    void OnNewGame()
    {
        // First time playing: no need to pick a slot
        if (SaveSlots.MostRecentSlot() < 0) StartNewGame(0);
        else ShowSlots(SlotMode.NewGame);
    }

    // ── Slots ────────────────────────────────────────────────────────────

    void ShowSlots(SlotMode mode)
    {
        slotMode = mode;
        slotsTitle.text = mode == SlotMode.NewGame ? "Choose a slot for the new journey" : "Load Game";
        BuildSlotCards();
        slotsPanel.RemoveFromClassList("oa-hidden");
        slotList.Q<Button>()?.Focus();
    }

    void BuildSlotCards()
    {
        slotList.Clear();
        for (int slot = 0; slot < SaveSlots.SlotCount; slot++)
        {
            int index = slot;
            var meta = SaveSlots.GetMeta(slot);

            var card = new Button { focusable = true };
            card.AddToClassList("oa-slot");

            var text = new VisualElement();
            text.AddToClassList("oa-slot__text");
            text.pickingMode = PickingMode.Ignore;
            var name = new Label(meta != null
                ? $"Slot {slot + 1}  —  {SceneLoader.DisplayName(meta.sceneName)}"
                : $"Slot {slot + 1}  —  Empty");
            name.AddToClassList("oa-slot__name");
            var info = new Label(meta != null
                ? $"Played {FormatPlayTime(meta.playTimeSeconds)}   ·   Saved {meta.SavedAtLocal:yyyy-MM-dd HH:mm}"
                : (slotMode == SlotMode.NewGame ? "Begin a new journey here" : "No journey yet"));
            info.AddToClassList("oa-slot__info");
            text.Add(name);
            text.Add(info);
            card.Add(text);

            if (slotMode == SlotMode.LoadGame)
            {
                card.SetEnabled(meta != null);
                card.clicked += () => LoadSlot(index);
                if (meta != null)
                {
                    var delete = new Button(() => Confirm($"Delete the journey in slot {index + 1}?\nThis cannot be undone.", () =>
                    {
                        SaveSlots.Delete(index);
                        if (SaveSlots.MostRecentSlot() < 0) ShowMain();
                        else BuildSlotCards();
                    })) { text = "Delete" };
                    delete.AddToClassList("oa-slot__delete");
                    card.Add(delete);
                }
            }
            else
            {
                card.clicked += () =>
                {
                    if (meta == null) StartNewGame(index);
                    else Confirm($"Slot {index + 1} already holds a journey.\nAbandon it and begin anew?", () => StartNewGame(index));
                };
            }
            slotList.Add(card);
        }
    }

    void StartNewGame(int slot)
    {
        if (SceneLoader.IsLoading) return;
        SaveSlots.CreateNew(slot, newGameScene);
        SceneLoader.Load(newGameScene);
    }

    void LoadSlot(int slot)
    {
        if (SceneLoader.IsLoading) return;
        var meta = SaveSlots.GetMeta(slot);
        if (meta == null) return;

        string scene = meta.sceneName;
        if (string.IsNullOrEmpty(scene) || !Application.CanStreamedLevelBeLoaded(scene))
        {
            Debug.LogWarning($"[StartMenu] Saved scene '{scene}' is not in the build; starting at {newGameScene}.");
            scene = newGameScene;
        }
        SaveSlots.BeginSession(slot);
        SceneLoader.Load(scene);
    }

    // ── Confirm dialog ───────────────────────────────────────────────────

    void Confirm(string message, Action onYes)
    {
        confirmText.text = message;
        pendingConfirm = onYes;
        confirmPanel.RemoveFromClassList("oa-hidden");
        confirmNo.Focus();
    }

    void HideConfirm()
    {
        pendingConfirm = null;
        confirmPanel.AddToClassList("oa-hidden");
    }

    // ── Misc ─────────────────────────────────────────────────────────────

    void OnKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Escape) return;
        if (!confirmPanel.ClassListContains("oa-hidden")) HideConfirm();
        else if (!slotsPanel.ClassListContains("oa-hidden")) ShowMain();
        evt.StopPropagation();
    }

    static string FormatPlayTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{t.Minutes}m {t.Seconds:00}s";
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
