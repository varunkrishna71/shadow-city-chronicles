// ============================================================================
// HealthSystem.cs — Player health, armor, and damage processing
// ============================================================================
// PURPOSE:
//   Manages the player's health and armor. Processes incoming damage,
//   applies armor reduction, handles death, and manages health regeneration.
//
// DAMAGE PIPELINE:
//   1. Raw damage comes in (e.g., 50 points from a shotgun)
//   2. Armor absorbs a percentage (e.g., 60% → armor takes 30, health takes 20)
//   3. Remaining damage reduces health
//   4. If health reaches 0, player dies
//
// HEALTH REGENERATION:
//   - Health does NOT auto-regenerate (realistic feel)
//   - Player must use health pickups or visit Jade's clinic
//   - Armor must be purchased or found
// ============================================================================

using UnityEngine;
using ShadowCity.Core;
using ShadowCity.Weapons;

namespace ShadowCity.Systems.Health
{
    public class HealthSystem : MonoBehaviour, IDamageable
    {
        [Header("Health")]
        [SerializeField] private float _maxHealth = 100f;
        [SerializeField] private float _currentHealth;

        [Header("Armor")]
        [SerializeField] private float _maxArmor = 100f;
        [SerializeField] private float _currentArmor;
        [SerializeField] private float _armorAbsorption = 0.6f; // 60% of damage goes to armor

        [Header("Damage Feedback")]
        [SerializeField] private float _damageFlashDuration = 0.3f;
        [SerializeField] private float _invulnerabilityDuration = 0.5f;

        [Header("Regeneration")]
        [SerializeField] private bool _enableHealthRegen = false;
        [SerializeField] private float _regenRate = 5f;
        [SerializeField] private float _regenDelay = 10f;
        [SerializeField] private float _regenThreshold = 0.25f; // Only regen up to 25% health

        // State
        private bool _isAlive = true;
        private bool _isInvulnerable;
        private float _invulnerabilityTimer;
        private float _regenTimer;
        private float _lastDamageTime;

        // Events
        public System.Action<float, float> OnHealthChanged;     // current, max
        public System.Action<float, float> OnArmorChanged;      // current, max
        public System.Action<float, Vector3> OnDamageTaken;     // damage, direction
        public System.Action OnDeath;

        public float MaxHealth => _maxHealth;
        public float CurrentHealth => _currentHealth;
        public float MaxArmor => _maxArmor;
        public float CurrentArmor => _currentArmor;
        public float HealthPercent => _currentHealth / _maxHealth;
        public float ArmorPercent => _maxArmor > 0 ? _currentArmor / _maxArmor : 0;

        private void Awake()
        {
            _currentHealth = _maxHealth;
            _currentArmor = 0f;
            _isAlive = true;
        }

        private void Update()
        {
            UpdateInvulnerability();
            UpdateRegeneration();
        }

        // ====================================================================
        // DAMAGE
        // ====================================================================

        public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
        {
            if (!_isAlive || _isInvulnerable) return;

            float actualDamage = damage;

            // Armor absorption
            if (_currentArmor > 0)
            {
                float armorDamage = actualDamage * _armorAbsorption;
                float healthDamage = actualDamage * (1f - _armorAbsorption);

                if (armorDamage > _currentArmor)
                {
                    // Armor depleted — excess goes to health
                    float excess = armorDamage - _currentArmor;
                    _currentArmor = 0;
                    healthDamage += excess;
                }
                else
                {
                    _currentArmor -= armorDamage;
                }

                actualDamage = healthDamage;
                OnArmorChanged?.Invoke(_currentArmor, _maxArmor);
            }

            _currentHealth -= actualDamage;
            _currentHealth = Mathf.Max(0, _currentHealth);

            _lastDamageTime = Time.time;
            _regenTimer = _regenDelay;

            OnHealthChanged?.Invoke(_currentHealth, _maxHealth);
            OnDamageTaken?.Invoke(actualDamage, hitDirection);

            // Publish event for other systems
            EventBus.Publish(new PlayerDamagedEvent
            {
                Damage = actualDamage,
                HitPoint = hitPoint,
                DamageSource = "Combat"
            });

            // Start invulnerability frames
            _isInvulnerable = true;
            _invulnerabilityTimer = _invulnerabilityDuration;

            if (_currentHealth <= 0)
            {
                Die();
            }
        }

        // ====================================================================
        // DEATH
        // ====================================================================

        private void Die()
        {
            if (!_isAlive) return;

            _isAlive = false;
            _currentHealth = 0;

            OnDeath?.Invoke();

            EventBus.Publish(new PlayerDeathEvent
            {
                CauseOfDeath = "Health depleted",
                DeathPosition = transform.position
            });

            if (GameManager.Instance != null)
            {
                GameManager.Instance.SetGameState(GameState.GameOver);
            }
        }

        // ====================================================================
        // HEALING
        // ====================================================================

        public void Heal(float amount)
        {
            if (!_isAlive) return;

            float oldHealth = _currentHealth;
            _currentHealth = Mathf.Min(_currentHealth + amount, _maxHealth);

            if (_currentHealth != oldHealth)
            {
                OnHealthChanged?.Invoke(_currentHealth, _maxHealth);
                EventBus.Publish(new PlayerHealedEvent { Amount = _currentHealth - oldHealth });
            }
        }

        public void AddArmor(float amount)
        {
            float oldArmor = _currentArmor;
            _currentArmor = Mathf.Min(_currentArmor + amount, _maxArmor);

            if (_currentArmor != oldArmor)
            {
                OnArmorChanged?.Invoke(_currentArmor, _maxArmor);
            }
        }

        public void FullHeal()
        {
            Heal(_maxHealth);
            AddArmor(_maxArmor);
        }

        // ====================================================================
        // REGENERATION
        // ====================================================================

        private void UpdateRegeneration()
        {
            if (!_enableHealthRegen || !_isAlive) return;
            if (_currentHealth >= _maxHealth * _regenThreshold) return;

            _regenTimer -= Time.deltaTime;
            if (_regenTimer > 0) return;

            Heal(_regenRate * Time.deltaTime);
        }

        // ====================================================================
        // INVULNERABILITY
        // ====================================================================

        private void UpdateInvulnerability()
        {
            if (!_isInvulnerable) return;

            _invulnerabilityTimer -= Time.deltaTime;
            if (_invulnerabilityTimer <= 0)
            {
                _isInvulnerable = false;
            }
        }

        // ====================================================================
        // RESPAWN
        // ====================================================================

        public void Respawn(Vector3 position, float healthPercent = 0.5f)
        {
            _isAlive = true;
            _currentHealth = _maxHealth * healthPercent;
            _currentArmor = 0;
            transform.position = position;

            OnHealthChanged?.Invoke(_currentHealth, _maxHealth);
            OnArmorChanged?.Invoke(_currentArmor, _maxArmor);
        }

        // ====================================================================
        // IDamageable
        // ====================================================================

        public float GetHealthPercent() => _currentHealth / _maxHealth;
        public bool IsAlive() => _isAlive;
    }
}
