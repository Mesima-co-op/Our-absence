#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility that creates a set of ready-to-use EnemyPersonality assets
/// in Assets/HTN_EnemyAI/Personalities/.
/// Run via  Tools > HTN Enemy AI > Create Personality Presets
/// </summary>
public static class PersonalityPresetGenerator
{
    private const string OutputPath = "Assets/HTN_EnemyAI/Personalities";

    [MenuItem("Tools/HTN Enemy AI/Create Personality Presets")]
    public static void CreateAll()
    {
        EnsureFolder();

        CreateAsset(Aggressive(), "Personality_Aggressive");
        CreateAsset(Cautious(), "Personality_Cautious");
        CreateAsset(Balanced(), "Personality_Balanced");
        CreateAsset(Berserker(), "Personality_Berserker");
        CreateAsset(Skirmisher(), "Personality_Skirmisher");
        CreateAsset(Coward(), "Personality_Coward");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[HTN] Personality presets created in " + OutputPath);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Preset definitions
    //  Tweak these tables to tune the probability feel of each archetype.
    // ─────────────────────────────────────────────────────────────────────────

    /// High aggression, attacks freely, barely retreats.
    static EnemyPersonality Aggressive() => new EnemyPersonality
    {
        personalityName = "Aggressive",
        aggression = 0.80f,
        caution = 0.10f,
        unpredictability = 0.15f,
        // Action weights
        weightAttackMelee = 9f,
        weightReposition = 2f,
        weightRetreat = 1f,
        weightCircle = 3f,
        weightPrepare = 3f,
        weightDodge = 2f,
        weightChase = 8f,
        // Combat timing
        attackCooldown = 1.0f,
        maxConsecutiveAttacks = 3,
        minCombatTime = 1.0f,
        maxCombatTime = 3.0f,
        // Movement
        combatSpeed = 4.5f,
        chaseSpeed = 6.5f,
        footworkSpeed = 2.0f,
        // Ranges
        meleeAttackRange = 2.2f,
        optimalCombatRange = 3.5f,
        stopChasingRange = 14f,
    };

    /// Hangs back, circles, attacks only when safe.
    static EnemyPersonality Cautious() => new EnemyPersonality
    {
        personalityName = "Cautious",
        aggression = 0.30f,
        caution = 0.70f,
        unpredictability = 0.10f,
        weightAttackMelee = 3f,
        weightReposition = 5f,
        weightRetreat = 5f,
        weightCircle = 7f,
        weightPrepare = 4f,
        weightDodge = 5f,
        weightChase = 2f,
        attackCooldown = 2.0f,
        maxConsecutiveAttacks = 1,
        minCombatTime = 2.0f,
        maxCombatTime = 5.0f,
        combatSpeed = 3.0f,
        chaseSpeed = 4.5f,
        footworkSpeed = 1.6f,
        meleeAttackRange = 2.0f,
        optimalCombatRange = 5.0f,
        maxRetreatDistance = 7f,
        rollChance = 0.5f,
    };

    /// Middle-of-the-road; matches original EnemyAI defaults.
    static EnemyPersonality Balanced() => new EnemyPersonality
    {
        personalityName = "Balanced",
        aggression = 0.55f,
        caution = 0.30f,
        unpredictability = 0.20f,
        weightAttackMelee = 5f,
        weightReposition = 3f,
        weightRetreat = 2f,
        weightCircle = 4f,
        weightPrepare = 2f,
        weightDodge = 3f,
        weightChase = 4f,
        attackCooldown = 1.5f,
        maxConsecutiveAttacks = 2,
        minCombatTime = 1.5f,
        maxCombatTime = 4.0f,
        combatSpeed = 3.5f,
        chaseSpeed = 5.5f,
        footworkSpeed = 1.8f,
    };

    /// Relentless; ignores consecutive-attack cap, barely dodges.
    static EnemyPersonality Berserker() => new EnemyPersonality
    {
        personalityName = "Berserker",
        aggression = 1.00f,
        caution = 0.00f,
        unpredictability = 0.40f,
        weightAttackMelee = 15f,
        weightReposition = 1f,
        weightRetreat = 0f,
        weightCircle = 1f,
        weightPrepare = 2f,
        weightDodge = 1f,
        weightChase = 12f,
        attackCooldown = 0.7f,
        maxConsecutiveAttacks = 5,
        minCombatTime = 0.5f,
        maxCombatTime = 2.0f,
        combatSpeed = 5.0f,
        chaseSpeed = 7.5f,
        rollChance = 0.05f,
        stopChasingRange = 20f,
    };

    /// Hit-and-run: lots of circling, dodging, repositioning, occasional burst.
    static EnemyPersonality Skirmisher() => new EnemyPersonality
    {
        personalityName = "Skirmisher",
        aggression = 0.50f,
        caution = 0.45f,
        unpredictability = 0.40f,
        weightAttackMelee = 4f,
        weightReposition = 6f,
        weightRetreat = 3f,
        weightCircle = 8f,
        weightPrepare = 1f,
        weightDodge = 7f,
        weightChase = 5f,
        attackCooldown = 1.2f,
        maxConsecutiveAttacks = 1,
        minCombatTime = 1.0f,
        maxCombatTime = 2.5f,
        combatSpeed = 4.0f,
        chaseSpeed = 6.0f,
        orbitRadius = 5f,
        orbitSpeed = 2.5f,
        rollChance = 0.6f,
    };

    /// Flees frequently, only attacks as a last resort.
    static EnemyPersonality Coward() => new EnemyPersonality
    {
        personalityName = "Coward",
        aggression = 0.10f,
        caution = 0.90f,
        unpredictability = 0.10f,
        weightAttackMelee = 1f,
        weightReposition = 4f,
        weightRetreat = 10f,
        weightCircle = 3f,
        weightPrepare = 1f,
        weightDodge = 8f,
        weightChase = 1f,
        attackCooldown = 3.0f,
        maxConsecutiveAttacks = 1,
        minCombatTime = 2.5f,
        maxCombatTime = 6.0f,
        combatSpeed = 2.5f,
        chaseSpeed = 4.0f,
        maxRetreatDistance = 10f,
        rollChance = 0.7f,
        stopChasingRange = 6f,
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/HTN_EnemyAI"))
            AssetDatabase.CreateFolder("Assets", "HTN_EnemyAI");
        if (!AssetDatabase.IsValidFolder(OutputPath))
            AssetDatabase.CreateFolder("Assets/HTN_EnemyAI", "Personalities");
    }

    static void CreateAsset(EnemyPersonality data, string assetName)
    {
        // In-memory object → ScriptableObject asset
        EnemyPersonality so = ScriptableObject.CreateInstance<EnemyPersonality>();

        // Copy all fields via JSON round-trip (handles new fields automatically)
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(data), so);

        string path = $"{OutputPath}/{assetName}.asset";
        AssetDatabase.CreateAsset(so, path);
    }
}
#endif
