using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

/// <summary>
/// Loads scenes behind a loading screen (lore line, area name, progress bar).
/// Usage: SceneLoader.Load("TheDescent"). Survives scene changes; created on first use.
/// </summary>
public class SceneLoader : MonoBehaviour
{
    public const string StartMenuScene = "StartMenu";

    const float FadeSeconds = 0.4f;
    const float MinimumShowSeconds = 1.6f;   // long enough to read a line of lore
    const float SaveLoadTimeout = 6f;

    static readonly string[] LoreLines =
    {
        "The path cannot be turned back; the only way is through the darkness.",
        "From the void emerge faint fragments of light, fragile sparks of creation.",
        "With creation comes war, with war comes death, and with death, the light begins to wither.",
        "Six gods gave their favor to this land. Not every man saw it as a gift.",
        "With all good comes evil. This is the balance of the favor.",
        "Every step forward is a struggle against despair.",
        "What once gave more hope than despair has faded away. The scales are tilted.",
        "Uncover what little light remains, before it is forever consumed.",
    };

    static SceneLoader instance;
    public static bool IsLoading { get; private set; }

    UIDocument document;
    VisualElement root, barFill, ember;
    Label lore, area, hint;

    public static void Load(string sceneName)
    {
        if (IsLoading) return;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[SceneLoader] Scene '{sceneName}' is not in the build settings.");
            return;
        }
        Ensure().StartCoroutine(instance.LoadRoutine(sceneName));
    }

    /// <summary>Friendly area name for menus and the loading screen.</summary>
    public static string DisplayName(string sceneName)
    {
        switch (sceneName)
        {
            case "TheDescent": return "The Descent";
            case StartMenuScene: return "";
            default: return sceneName;
        }
    }

    static SceneLoader Ensure()
    {
        if (instance != null) return instance;

        var go = new GameObject("[SceneLoader]");
        go.SetActive(false); // configure the UIDocument before it enables
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SceneLoader>();
        instance.document = go.AddComponent<UIDocument>();
        instance.document.panelSettings = Resources.Load<PanelSettings>("UI/OverlayPanelSettings");
        instance.document.visualTreeAsset = Resources.Load<VisualTreeAsset>("UI/LoadingScreen");
        go.SetActive(true);
        instance.Bind();
        return instance;
    }

    void Bind()
    {
        root = document.rootVisualElement.Q("LoadingRoot");
        barFill = root.Q("BarFill");
        ember = root.Q("Ember");
        lore = root.Q<Label>("LoreLine");
        area = root.Q<Label>("AreaName");
        hint = root.Q<Label>("Hint");
        root.style.display = DisplayStyle.None;
    }

    IEnumerator LoadRoutine(string sceneName)
    {
        IsLoading = true;
        Time.timeScale = 1f;
        float shownAt = Time.unscaledTime;

        lore.text = LoreLines[Random.Range(0, LoreLines.Length)];
        area.text = DisplayName(sceneName);
        hint.text = "Loading";
        SetProgress(0f);
        root.style.display = DisplayStyle.Flex;
        yield return Fade(0f, 1f);

        var op = USceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;
        while (op.progress < 0.9f)
        {
            SetProgress(op.progress / 0.9f * 0.8f);
            yield return null;
        }
        SetProgress(0.8f);
        op.allowSceneActivation = true;
        while (!op.isDone) yield return null;

        // Let the new scene's Start() run and its save data (inventory, items) come in
        yield return null;
        hint.text = "Restoring";
        float waited = 0f;
        while (!ItemsManager.isLoaded && waited < SaveLoadTimeout && sceneName != StartMenuScene)
        {
            waited += Time.unscaledDeltaTime;
            SetProgress(Mathf.Lerp(0.8f, 0.95f, waited / SaveLoadTimeout));
            yield return null;
        }

        SetProgress(1f);
        while (Time.unscaledTime - shownAt < MinimumShowSeconds) yield return null;

        yield return Fade(1f, 0f);
        root.style.display = DisplayStyle.None;
        IsLoading = false;
    }

    IEnumerator Fade(float from, float to)
    {
        for (float t = 0; t < FadeSeconds; t += Time.unscaledDeltaTime)
        {
            root.style.opacity = Mathf.Lerp(from, to, t / FadeSeconds);
            yield return null;
        }
        root.style.opacity = to;
    }

    void SetProgress(float value)
    {
        barFill.style.width = Length.Percent(Mathf.Clamp01(value) * 100f);
    }

    void Update()
    {
        if (!IsLoading) return;
        // A single ember breathing in the dark
        float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 2.4f);
        ember.style.opacity = pulse;
        float size = 0.85f + 0.3f * pulse;
        ember.style.scale = new Scale(new Vector3(size, size, 1f)); // UI Toolkit only supports z = 1
    }
}
