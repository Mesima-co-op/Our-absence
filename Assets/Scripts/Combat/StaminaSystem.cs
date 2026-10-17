using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Stamina: attacks, rolls and sprinting spend it, and it comes back after a short pause.
/// - An action needs some stamina left (more than 0); its full cost is then taken, down to 0.
/// - Emptying the bar exhausts the character: a longer pause before regeneration, and no attacks, rolls or
///   sprinting until a quarter of the bar is back. Endless combos are impossible; the player has to back
///   off and breathe.
/// Maximum, regeneration and the cost multiplier come from CharacterStats (Endurance, Agility).
/// On the player it also drives the HUD stamina bar (GameUI.uxml: StaminaBarMask / StaminaLabel).
/// </summary>
public class StaminaSystem : MonoBehaviour
{
    [SerializeField] private float maxStamina = 100f;
    [Tooltip("Stamina regained per second")]
    [SerializeField] private float regenPerSecond = 30f;
    [Tooltip("Seconds after spending before stamina starts coming back")]
    [SerializeField] private float regenDelay = 0.7f;
    [Tooltip("The pause when the bar was emptied")]
    [SerializeField] private float exhaustedDelay = 1.6f;

    [Header("Costs")]
    [SerializeField] private float rollCost = 20f;
    [Tooltip("Per second of sprinting")]
    [SerializeField] private float sprintCostPerSecond = 12f;
    [Tooltip("After running dry, actions are possible again at this part of the bar")]
    [UnityEngine.Serialization.FormerlySerializedAs("sprintResumeAt")]
    [Range(0f, 1f)] [SerializeField] private float recoverAt = 0.25f;

    private float stamina;
    private float costMultiplier = 1f;
    private float regenBlockedUntil;
    private bool exhausted; // ran dry: no actions until recoverAt
    private float flashUntil;

    /// <summary>Raised with (current, max) whenever stamina changes noticeably.</summary>
    public event Action<float, float> OnStaminaChanged;
    /// <summary>Raised when an action was refused for lack of stamina.</summary>
    public event Action OnTooTired;

    public float Stamina => stamina;
    public float MaxStamina => maxStamina;
    public float RollCost => rollCost;
    /// <summary>True while there is stamina to act with (not exhausted).</summary>
    public bool CanAct => !exhausted && stamina > 0f;
    public bool CanSprint => CanAct;
    /// <summary>The bar was emptied and hasn't recovered to <c>recoverAt</c> yet.</summary>
    public bool Exhausted => exhausted;

    private void Awake()
    {
        stamina = maxStamina;
    }

    /// <summary>Sets the maximum, regeneration and cost multiplier (from CharacterStats).</summary>
    public void Configure(float max, float regen, float costs, bool fill)
    {
        float ratio = maxStamina > 0f ? stamina / maxStamina : 1f;
        maxStamina = Mathf.Max(1f, max);
        regenPerSecond = Mathf.Max(0f, regen);
        costMultiplier = Mathf.Max(0.05f, costs);
        stamina = fill ? maxStamina : ratio * maxStamina;
        Changed();
    }

    /// <summary>
    /// Takes <paramref name="cost"/> (before the Agility multiplier) if there is any stamina left.
    /// Returns false, and flashes the bar, when the character is too tired to act.
    /// </summary>
    public bool TrySpend(float cost)
    {
        if (!CanAct)
        {
            flashUntil = Time.time + 0.4f;
            OnTooTired?.Invoke();
            return false;
        }
        Spend(cost * costMultiplier);
        return true;
    }

    /// <summary>Continuous costs like sprinting: <paramref name="perSecond"/> scaled by the frame time.</summary>
    public void Drain(float perSecond)
    {
        Spend(perSecond * costMultiplier * Time.deltaTime);
    }

    /// <summary>Sprinting for this frame, if there is stamina for it.</summary>
    public bool Sprint()
    {
        if (!CanSprint) return false;
        Drain(sprintCostPerSecond);
        return true;
    }

    /// <summary>Refills the bar (rest at a campfire, tests).</summary>
    public void Refill()
    {
        stamina = maxStamina;
        exhausted = false;
        Changed();
    }

    private void Spend(float amount)
    {
        if (amount <= 0f) return;
        stamina = Mathf.Max(0f, stamina - amount);
        if (stamina <= 0f) exhausted = true;
        float delay = stamina <= 0f ? exhaustedDelay : regenDelay;
        regenBlockedUntil = Mathf.Max(regenBlockedUntil, Time.time + delay);
        Changed();
    }

    private void Update()
    {
        if (stamina < maxStamina && Time.time >= regenBlockedUntil)
        {
            stamina = Mathf.Min(maxStamina, stamina + regenPerSecond * Time.deltaTime);
            if (exhausted && stamina >= maxStamina * recoverAt) exhausted = false;
            Changed();
        }
        UpdateHud();
    }

    private void Changed() => OnStaminaChanged?.Invoke(stamina, maxStamina);

    // ── HUD ─────────────────────────────────────────────────────────────

    private VisualElement hudMask, hudFill;
    private Label hudLabel;
    private bool hudSearched;

    /// <summary>Finds the stamina bar in the scene's HUD (only for the player).</summary>
    private void FindHud()
    {
        hudSearched = true;
        if (GetComponent<Player_controller>() == null) return;
        foreach (var doc in FindObjectsOfType<UIDocument>())
        {
            var root = doc.rootVisualElement;
            var mask = root?.Q<VisualElement>("StaminaBarMask");
            if (mask == null) continue;
            hudMask = mask;
            hudFill = root.Q<VisualElement>("StaminaBarFill");
            hudLabel = root.Q<Label>("StaminaLabel");
            return;
        }
    }

    private static readonly Color FillColor = new Color(0.55f, 0.85f, 0.35f);
    private static readonly Color TiredColor = new Color(0.95f, 0.75f, 0.25f);
    private static readonly Color FlashColor = new Color(1f, 0.25f, 0.2f);

    private void UpdateHud()
    {
        if (!hudSearched) FindHud();
        if (hudMask == null) return;
        float ratio = maxStamina > 0f ? stamina / maxStamina : 0f;
        hudMask.style.width = Length.Percent(Mathf.Lerp(1f, 99f, ratio));
        if (hudFill != null)
            hudFill.style.backgroundColor = Time.time < flashUntil ? FlashColor : exhausted ? TiredColor : FillColor;
        if (hudLabel != null) hudLabel.text = $"{Mathf.CeilToInt(stamina)}/{Mathf.RoundToInt(maxStamina)}";
    }
}
