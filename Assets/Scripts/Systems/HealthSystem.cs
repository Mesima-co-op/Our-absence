using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

/// <summary>
/// Single source of truth for the health of the player or an enemy.
/// UI references are optional, so the same component works on enemies (no HUD) and on the player.
/// </summary>
public class HealthSystem : MonoBehaviour
{
    public Player_controller Player_controller;
    public UIDocument UIDoc;

    private Label m_HealthLabel;
    private VisualElement m_HealthBarMask;

    [SerializeField] private UnityEngine.UI.Slider uiSlider;
    [Tooltip("Object that gets disabled on death. Defaults to this GameObject.")]
    [FormerlySerializedAs("gameObject")]
    [SerializeField] private GameObject deathTarget;
    [SerializeField] private int health = 100;
    [SerializeField] private int maxHealth = 100;

    [Header("Player death")]
    [Tooltip("Seconds before the scene reloads after the player dies. Only used on the object tagged Player.")]
    [SerializeField] private float playerRespawnDelay = 4f;

    private Dictionary<string, int> healthModifiers = new Dictionary<string, int>();
    private Dictionary<string, int> damageEffects = new Dictionary<string, int>();
    private Dictionary<string, int> healthEffects = new Dictionary<string, int>();

    private float effectTimer = 0f;
    private bool isDead = false;

    // When this instance only displays another HealthSystem (e.g. the HUD object showing the player's health)
    private HealthSystem source;

    /// <summary>Raised with (current, max) whenever health changes.</summary>
    public event Action<int, int> OnHealthChanged;
    public event Action OnDeath;

    public int Health => source != null ? source.Health : health;
    public int MaxHealth => source != null ? source.MaxHealth : maxHealth;
    public bool IsDead => source != null ? source.IsDead : isDead;
    public Dictionary<string, int> HealthModifiers => healthModifiers;
    public Dictionary<string, int> DamageEffects => damageEffects;
    public Dictionary<string, int> HealthEffects => healthEffects;

    void Start()
    {
        if (deathTarget == null) deathTarget = gameObject;

        if (uiSlider)
            uiSlider.maxValue = maxHealth;

        if (UIDoc != null && UIDoc.rootVisualElement != null)
        {
            m_HealthLabel = UIDoc.rootVisualElement.Q<Label>("HealthLabel");
            m_HealthBarMask = UIDoc.rootVisualElement.Q<VisualElement>("HealthBarMask");
        }

        // If this sits on another object (the HUD) and the player has its own HealthSystem, mirror that one
        if (Player_controller != null && Player_controller.gameObject != gameObject)
        {
            var playerHealth = Player_controller.GetComponent<HealthSystem>();
            if (playerHealth != null && playerHealth != this)
            {
                source = playerHealth;
                source.OnHealthChanged += RefreshUI;
                RefreshUI(source.Health, source.MaxHealth);
                return;
            }
        }

        health = Mathf.Clamp(health, 0, maxHealth);
        UpdateHealthUI();
    }

    private void OnDestroy()
    {
        if (source != null) source.OnHealthChanged -= RefreshUI;
    }

    private void Update()
    {
        if (isDead || source != null) return;

        effectTimer += Time.deltaTime;
        if (effectTimer >= 1f)
        {
            ApplyEffects();
            effectTimer = 0f;
        }
    }

    private void ApplyEffects()
    {
        // Copy values first: TakeDamage/Heal can trigger death or listeners that modify the dictionaries
        foreach (var value in new List<int>(damageEffects.Values))
            TakeDamage(value);
        foreach (var value in new List<int>(healthEffects.Values))
            Heal(value);
    }

    public void TakeDamage(int damage)
    {
        if (source != null) { source.TakeDamage(damage); return; }
        if (isDead || damage <= 0) return;

        health = Mathf.Max(0, health - damage);
        UpdateHealthUI();

        if (health <= 0)
            Death();
    }

    public void Heal(int amount)
    {
        if (source != null) { source.Heal(amount); return; }
        if (isDead || amount <= 0) return;

        health = Mathf.Min(maxHealth, health + amount);
        UpdateHealthUI();
    }

    /// <summary>Positive values heal, negative values damage.</summary>
    public void ChangeHealth(float changeAmount)
    {
        int amount = Mathf.RoundToInt(changeAmount);
        if (amount >= 0) Heal(amount);
        else TakeDamage(-amount);
    }

    public void AddHealthModifier(string name, int modifier)
    {
        healthModifiers[name] = modifier;
    }

    public void ClearDamageEffects()
    {
        damageEffects.Clear();
    }

    public void ClearHealthEffects()
    {
        healthEffects.Clear();
    }

    public void AddDamageEffect(string name, int effect)
    {
        damageEffects[name] = effect;
    }

    public void AddHealthEffect(string name, int effect)
    {
        healthEffects[name] = effect;
    }

    public void RemoveHealthModifier(string name)
    {
        healthModifiers.Remove(name);
    }

    public void RemoveDamageEffect(string name)
    {
        damageEffects.Remove(name);
    }

    public void RemoveHealthEffect(string name)
    {
        healthEffects.Remove(name);
    }

    public void Death()
    {
        if (source != null) { source.Death(); return; }
        if (isDead) return;
        isDead = true;
        health = 0;

        var target = deathTarget != null ? deathTarget : gameObject;

        foreach (var component in target.GetComponents<MonoBehaviour>())
        {
            if (component != this)
                component.enabled = false;
        }

        // Disabling the animator lets ragdoll rigidbodies (if any) take over
        var animator = target.GetComponent<Animator>();
        if (animator != null) animator.enabled = false;

        var navAgent = target.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (navAgent != null) navAgent.enabled = false;

        var characterController = target.GetComponent<CharacterController>();
        if (characterController != null) characterController.enabled = false;
        else
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
        }

        target.tag = "Die";
        OnDeath?.Invoke();

        bool isPlayer = Player_controller != null || GetComponent<Player_controller>() != null;
        if (isPlayer)
            StartCoroutine(ReloadSceneAfterDelay());
    }

    private IEnumerator ReloadSceneAfterDelay()
    {
        yield return new WaitForSeconds(playerRespawnDelay);
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEngine.SceneManagement.SceneManager.LoadScene(scene.buildIndex >= 0 ? scene.buildIndex : 0);
    }

    private void UpdateHealthUI()
    {
        RefreshUI(health, maxHealth);
        OnHealthChanged?.Invoke(health, maxHealth);
    }

    private void RefreshUI(int current, int max)
    {
        if (uiSlider)
        {
            uiSlider.maxValue = max;
            uiSlider.value = current;
        }

        if (m_HealthLabel != null)
            m_HealthLabel.text = $"{current}/{max}";

        if (m_HealthBarMask != null)
        {
            float healthRatio = max > 0 ? (float)current / max : 0f;
            m_HealthBarMask.style.width = Length.Percent(Mathf.Lerp(1, 99, healthRatio));
        }
    }
}
