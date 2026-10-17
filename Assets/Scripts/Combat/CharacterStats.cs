using System;
using UnityEngine;

/// <summary>The trainable attributes. Each level-up raises one of them by a point.</summary>
public enum CharacterAttribute
{
    Vigor,      // maximum health
    Endurance,  // maximum stamina and how fast it comes back
    Strength,   // damage of every blow
    Agility,    // cheaper attacks, rolls and sprinting
}

/// <summary>
/// A character's attributes, level and experience, and the values derived from them:
/// maximum health (HealthSystem), maximum stamina and regeneration (StaminaSystem), damage and stamina costs.
/// - Level = 1 + the points gained since the starting attributes.
/// - Experience is earned with <see cref="AddExperience"/>; <see cref="LevelUp"/> spends
///   <see cref="ExperienceToNextLevel"/> to raise one attribute.
/// - Gains per point are set below, so balancing needs no code. Saved by SavePlayerData.
/// Works on enemies too (without saving), for stronger variants later.
/// </summary>
public class CharacterStats : MonoBehaviour
{
    [Header("Attributes (points)")]
    [SerializeField] private int vigor = 10;
    [SerializeField] private int endurance = 10;
    [SerializeField] private int strength = 10;
    [SerializeField] private int agility = 10;
    [Tooltip("Experience available to spend on levels")]
    [SerializeField] private int experience;

    [Header("Health (Vigor)")]
    [SerializeField] private int baseHealth = 100;
    [SerializeField] private int healthPerVigor = 10;

    [Header("Stamina (Endurance)")]
    [SerializeField] private float baseStamina = 100f;
    [SerializeField] private float staminaPerEndurance = 5f;
    [Tooltip("Stamina regained per second")]
    [SerializeField] private float baseStaminaRegen = 30f;
    [SerializeField] private float regenPerEndurance = 1f;

    [Header("Damage (Strength)")]
    [Tooltip("Extra damage per Strength point above the starting value (0.03 = +3%)")]
    [SerializeField] private float damagePerStrength = 0.03f;

    [Header("Stamina costs (Agility)")]
    [Tooltip("Lower stamina costs per Agility point above the starting value (0.015 = -1.5%)")]
    [SerializeField] private float costReductionPerAgility = 0.015f;
    [SerializeField] private float maxCostReduction = 0.4f;

    [Header("Levels")]
    [Tooltip("Experience needed for the first level-up; each level costs this much more (x growth)")]
    [SerializeField] private int firstLevelCost = 100;
    [SerializeField] private float levelCostGrowth = 1.12f;

    /// <summary>Points every attribute starts with; levels count the points above it.</summary>
    public const int StartingPoints = 10;

    /// <summary>Raised whenever an attribute, the level or the experience changes.</summary>
    public event Action OnStatsChanged;

    public int Experience => experience;
    public int Level => 1 + Mathf.Max(0, vigor + endurance + strength + agility - 4 * StartingPoints);
    public int ExperienceToNextLevel => Mathf.RoundToInt(firstLevelCost * Mathf.Pow(levelCostGrowth, Level - 1));
    public bool CanLevelUp => experience >= ExperienceToNextLevel;

    public int Get(CharacterAttribute attribute) => attribute switch
    {
        CharacterAttribute.Vigor => vigor,
        CharacterAttribute.Endurance => endurance,
        CharacterAttribute.Strength => strength,
        _ => agility,
    };

    // ── Derived values ──────────────────────────────────────────────────

    public int MaxHealth => baseHealth + healthPerVigor * (vigor - StartingPoints);
    public float MaxStamina => baseStamina + staminaPerEndurance * (endurance - StartingPoints);
    public float StaminaRegen => baseStaminaRegen + regenPerEndurance * (endurance - StartingPoints);
    /// <summary>Multiplies all damage the character deals.</summary>
    public float DamageMultiplier => Mathf.Max(0.1f, 1f + damagePerStrength * (strength - StartingPoints));
    /// <summary>Multiplies all stamina costs.</summary>
    public float StaminaCostMultiplier => 1f - Mathf.Clamp(costReductionPerAgility * (agility - StartingPoints), -1f, maxCostReduction);

    private HealthSystem health;
    private StaminaSystem stamina;
    private bool applied;

    private void Start()
    {
        health = GetComponent<HealthSystem>();
        stamina = GetComponent<StaminaSystem>();
        Apply(firstTime: true);
    }

    // ── Changing ────────────────────────────────────────────────────────

    public void AddExperience(int amount)
    {
        if (amount <= 0) return;
        experience += amount;
        OnStatsChanged?.Invoke();
    }

    /// <summary>Spends the experience for one level on a point of <paramref name="attribute"/>.</summary>
    public bool LevelUp(CharacterAttribute attribute)
    {
        if (!CanLevelUp) return false;
        experience -= ExperienceToNextLevel;
        Raise(attribute, 1);
        return true;
    }

    /// <summary>Adds points without spending experience (rewards, tests).</summary>
    public void Raise(CharacterAttribute attribute, int points)
    {
        switch (attribute)
        {
            case CharacterAttribute.Vigor: vigor = Mathf.Max(1, vigor + points); break;
            case CharacterAttribute.Endurance: endurance = Mathf.Max(1, endurance + points); break;
            case CharacterAttribute.Strength: strength = Mathf.Max(1, strength + points); break;
            default: agility = Mathf.Max(1, agility + points); break;
        }
        Apply(firstTime: false);
    }

    /// <summary>Pushes the derived values to health and stamina.</summary>
    private void Apply(bool firstTime)
    {
        // The first time the character starts full; later the bars keep their fill ratio
        if (health != null) health.SetMaxHealth(MaxHealth, fill: firstTime || !applied);
        if (stamina != null) stamina.Configure(MaxStamina, StaminaRegen, StaminaCostMultiplier, fill: firstTime || !applied);
        applied = true;
        OnStatsChanged?.Invoke();
    }

    // ── Saving ──────────────────────────────────────────────────────────

    [Serializable]
    public class SaveData
    {
        public int vigor, endurance, strength, agility, experience;
    }

    public SaveData ToSave() => new SaveData
    {
        vigor = vigor, endurance = endurance, strength = strength, agility = agility, experience = experience,
    };

    public void Load(SaveData data)
    {
        if (data == null) return;
        vigor = Mathf.Max(1, data.vigor);
        endurance = Mathf.Max(1, data.endurance);
        strength = Mathf.Max(1, data.strength);
        agility = Mathf.Max(1, data.agility);
        experience = Mathf.Max(0, data.experience);
        if (applied) Apply(firstTime: true); // loaded after Start: refill to the new maximums
    }
}
