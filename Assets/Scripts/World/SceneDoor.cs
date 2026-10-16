using UnityEngine;

/// <summary>
/// Walk-through doorway to another scene: when the player enters the trigger, the game is saved
/// and the target scene loads behind the loading screen.
/// Setup: put it on an object with a Collider (set to Is Trigger) and type the scene name.
/// The target scene must be in File > Build Settings.
/// </summary>
[RequireComponent(typeof(Collider))]
public class SceneDoor : MonoBehaviour
{
    [Tooltip("Scene to load, by name (must be in the build settings).")]
    [SerializeField] private string targetScene = "TheDescent";
    [SerializeField] private bool saveBeforeLeaving = true;

    private bool used;

    public string TargetScene => targetScene;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (used || SceneLoader.IsLoading) return;
        if (other.GetComponent<Player_controller>() == null) return;

        used = true;
        if (saveBeforeLeaving) GameSave.SaveGame();
        SceneLoader.Load(targetScene);
    }
}
