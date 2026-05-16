// ============================================================================
// WeaponSystem.cs — Complete weapon management and shooting system
// ============================================================================
// PURPOSE:
//   Manages all weapon-related functionality:
//   - Weapon inventory (what the player carries)
//   - Weapon switching
//   - Shooting (raycasting for hitscan, projectiles for rockets/grenades)
//   - Reloading
//   - Recoil and spread
//   - Muzzle flash, shell ejection, impact effects
//
// ARCHITECTURE:
//   Weapons are defined as ScriptableObjects (data-driven design).
//   This means you can create new weapons by just creating new asset files
//   in Unity — no code changes needed.
//
// MOBILE OPTIMIZATION:
//   - Bullet impacts use Object Pooling (no Instantiate/Destroy)
//   - Raycasts use layer masks to skip unnecessary collision checks
//   - Muzzle flash uses particle system pooling
//   - Auto-aim assist for mobile (adjustable intensity)
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.Weapons
{
    /// <summary>
    /// ScriptableObject defining weapon properties. Create new weapons
    /// by right-clicking in Unity: Create > ShadowCity > Weapon Data.
    /// </summary>
    [CreateAssetMenu(fileName = "NewWeapon", menuName = "ShadowCity/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        [Header("Identity")]
        public string WeaponId;
        public string DisplayName;
        public WeaponType Type;
        public Sprite Icon;
        public GameObject ModelPrefab;

        [Header("Damage")]
        public float BaseDamage = 25f;
        public float HeadshotMultiplier = 2.5f;
        public float Range = 100f;
        public float ArmorPenetration = 0f; // 0 = none, 1 = full penetration

        [Header("Fire Rate")]
        public FireMode FireMode = FireMode.SemiAuto;
        public float FireRate = 5f;              // Rounds per second
        public int BurstCount = 3;               // For burst mode

        [Header("Ammo")]
        public int MagazineSize = 12;
        public int MaxReserveAmmo = 120;
        public float ReloadTime = 1.5f;
        public AmmoType AmmoType = AmmoType.Pistol;

        [Header("Accuracy")]
        public float BaseSpread = 1f;            // Degrees of spread
        public float AimSpread = 0.3f;           // Spread when aiming down sights
        public float MoveSpreadMultiplier = 1.5f; // Extra spread when moving
        public float SpreadRecoveryRate = 5f;

        [Header("Recoil")]
        public float RecoilVertical = 2f;
        public float RecoilHorizontal = 0.5f;
        public float RecoilRecoverySpeed = 8f;

        [Header("Audio")]
        public AudioClip FireSound;
        public AudioClip ReloadSound;
        public AudioClip EmptySound;
        public AudioClip EquipSound;
    }

    public enum WeaponType
    {
        Pistol,
        SMG,
        AssaultRifle,
        Shotgun,
        SniperRifle,
        RPG,
        Melee,
        Throwable
    }

    public enum FireMode
    {
        SemiAuto,     // One shot per press
        FullAuto,     // Continuous fire while held
        Burst,        // Fixed burst per press
        Pump          // Pump action (shotgun)
    }

    public enum AmmoType
    {
        Pistol,
        Rifle,
        Shotgun,
        Sniper,
        Explosive
    }

    /// <summary>
    /// Runtime instance of a weapon in the player's inventory.
    /// Tracks current ammo, modifications, etc.
    /// </summary>
    public class WeaponInstance
    {
        public WeaponData Data;
        public int CurrentMagazine;
        public int ReserveAmmo;
        public GameObject ModelInstance;

        public WeaponInstance(WeaponData data)
        {
            Data = data;
            CurrentMagazine = data.MagazineSize;
            ReserveAmmo = data.MaxReserveAmmo;
        }

        public bool HasAmmo => CurrentMagazine > 0;
        public bool CanReload => CurrentMagazine < Data.MagazineSize && ReserveAmmo > 0;
    }

    public class WeaponSystem : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private Transform _weaponHolder;      // Bone/transform where weapon model attaches
        [SerializeField] private Transform _muzzlePoint;       // Where bullets spawn from
        [SerializeField] private Transform _aimPoint;          // Where the crosshair aims at

        [Header("Auto-Aim (Mobile)")]
        [SerializeField] private float _autoAimRadius = 2f;     // How much aim assist
        [SerializeField] private float _autoAimRange = 50f;     // Max range for aim assist
        [SerializeField] private float _autoAimStrength = 0.5f;  // 0 = no assist, 1 = full snap
        [SerializeField] private LayerMask _enemyLayer;

        [Header("Impact")]
        [SerializeField] private LayerMask _hitLayers;
        [SerializeField] private string _bulletImpactPoolId = "BulletImpact";
        [SerializeField] private string _muzzleFlashPoolId = "MuzzleFlash";

        // Weapon inventory
        private List<WeaponInstance> _weapons = new List<WeaponInstance>();
        private int _currentWeaponIndex = -1;
        private WeaponInstance _currentWeapon;

        // Shooting state
        private float _nextFireTime;
        private float _currentSpread;
        private float _currentRecoil;
        private bool _isReloading;
        private int _burstShotsRemaining;

        // References
        private Animator _animator;
        private AudioSource _audioSource;

        // Animation hashes
        private static readonly int AnimShoot = Animator.StringToHash("Shoot");
        private static readonly int AnimReload = Animator.StringToHash("Reload");
        private static readonly int AnimWeaponType = Animator.StringToHash("WeaponType");

        public WeaponInstance CurrentWeapon => _currentWeapon;
        public bool IsReloading => _isReloading;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _audioSource = GetComponent<AudioSource>();
        }

        private void Update()
        {
            DecaySpread();
            DecayRecoil();
        }

        // ====================================================================
        // WEAPON INVENTORY
        // ====================================================================

        /// <summary>
        /// Add a weapon to the player's inventory. If they already have this type,
        /// just add ammo.
        /// </summary>
        public void AddWeapon(WeaponData weaponData)
        {
            // Check if player already has this weapon type
            WeaponInstance existing = _weapons.Find(w => w.Data.WeaponId == weaponData.WeaponId);

            if (existing != null)
            {
                existing.ReserveAmmo = Mathf.Min(
                    existing.ReserveAmmo + weaponData.MagazineSize,
                    weaponData.MaxReserveAmmo
                );
                return;
            }

            WeaponInstance newWeapon = new WeaponInstance(weaponData);

            // Instantiate the weapon model and attach to holder
            if (weaponData.ModelPrefab != null && _weaponHolder != null)
            {
                newWeapon.ModelInstance = Instantiate(weaponData.ModelPrefab, _weaponHolder);
                newWeapon.ModelInstance.SetActive(false);
            }

            _weapons.Add(newWeapon);

            // Auto-equip if this is the first weapon
            if (_weapons.Count == 1)
            {
                EquipWeapon(0);
            }
        }

        /// <summary>
        /// Switch to a specific weapon by index.
        /// </summary>
        public void EquipWeapon(int index)
        {
            if (index < 0 || index >= _weapons.Count) return;
            if (_isReloading) return;

            // Hide current weapon
            if (_currentWeapon?.ModelInstance != null)
            {
                _currentWeapon.ModelInstance.SetActive(false);
            }

            _currentWeaponIndex = index;
            _currentWeapon = _weapons[index];

            // Show new weapon
            if (_currentWeapon.ModelInstance != null)
            {
                _currentWeapon.ModelInstance.SetActive(true);
            }

            _animator.SetInteger(AnimWeaponType, (int)_currentWeapon.Data.Type);

            if (_currentWeapon.Data.EquipSound != null)
            {
                _audioSource.PlayOneShot(_currentWeapon.Data.EquipSound);
            }

            EventBus.Publish(new WeaponEquippedEvent
            {
                WeaponId = _currentWeapon.Data.WeaponId,
                CurrentAmmo = _currentWeapon.CurrentMagazine,
                MaxAmmo = _currentWeapon.ReserveAmmo
            });
        }

        /// <summary>
        /// Cycle to the next weapon in inventory.
        /// </summary>
        public void CycleWeapon(int direction)
        {
            if (_weapons.Count <= 1) return;

            int newIndex = (_currentWeaponIndex + direction + _weapons.Count) % _weapons.Count;
            EquipWeapon(newIndex);
        }

        // ====================================================================
        // SHOOTING
        // ====================================================================

        /// <summary>
        /// Attempt to fire the current weapon.
        /// Returns true if a shot was fired.
        /// </summary>
        public bool TryShoot(Vector3 aimDirection, bool isAiming)
        {
            if (_currentWeapon == null || _isReloading) return false;
            if (Time.time < _nextFireTime) return false;

            if (!_currentWeapon.HasAmmo)
            {
                // Click! Empty
                if (_currentWeapon.Data.EmptySound != null)
                {
                    _audioSource.PlayOneShot(_currentWeapon.Data.EmptySound);
                }

                // Auto-reload on empty
                if (_currentWeapon.CanReload)
                {
                    StartReload();
                }
                return false;
            }

            // Fire!
            PerformShot(aimDirection, isAiming);

            // Set next fire time based on fire rate
            _nextFireTime = Time.time + (1f / _currentWeapon.Data.FireRate);

            return true;
        }

        private void PerformShot(Vector3 aimDirection, bool isAiming)
        {
            // Apply auto-aim (mobile assist)
            aimDirection = ApplyAutoAim(aimDirection);

            // Apply spread
            float spread = isAiming ? _currentWeapon.Data.AimSpread : _currentWeapon.Data.BaseSpread;
            spread += _currentSpread;

            Vector3 spreadDirection = ApplySpread(aimDirection, spread);

            // Consume ammo
            _currentWeapon.CurrentMagazine--;

            // Perform raycast
            if (_currentWeapon.Data.Type != WeaponType.RPG)
            {
                PerformHitscan(spreadDirection);
            }

            // Apply recoil
            _currentRecoil += _currentWeapon.Data.RecoilVertical;
            _currentSpread += 0.5f;

            // Visual effects
            SpawnMuzzleFlash();

            // Audio
            if (_currentWeapon.Data.FireSound != null)
            {
                _audioSource.PlayOneShot(_currentWeapon.Data.FireSound);
            }

            // Animation
            _animator.SetTrigger(AnimShoot);

            // Event
            EventBus.Publish(new WeaponFiredEvent
            {
                WeaponId = _currentWeapon.Data.WeaponId,
                Origin = _muzzlePoint.position,
                Direction = spreadDirection
            });
        }

        /// <summary>
        /// Hitscan shooting — instant raycast to determine what was hit.
        /// Used for pistols, rifles, SMGs, shotguns.
        /// </summary>
        private void PerformHitscan(Vector3 direction)
        {
            Vector3 origin = _muzzlePoint != null ? _muzzlePoint.position : transform.position + Vector3.up * 1.5f;

            if (Physics.Raycast(origin, direction, out RaycastHit hit, _currentWeapon.Data.Range, _hitLayers))
            {
                // Calculate damage
                float damage = _currentWeapon.Data.BaseDamage;

                // Check for headshot
                if (hit.collider.CompareTag("Head"))
                {
                    damage *= _currentWeapon.Data.HeadshotMultiplier;
                }

                // Apply damage to target
                IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
                damageable?.TakeDamage(damage, hit.point, direction);

                // Spawn impact effect
                SpawnImpactEffect(hit.point, hit.normal);
            }
        }

        // ====================================================================
        // AUTO-AIM (Mobile Assist)
        // ====================================================================

        /// <summary>
        /// Subtly adjusts aim direction toward the nearest enemy.
        /// This is ESSENTIAL for mobile — without it, shooting feels impossible
        /// on a touchscreen. The strength is adjustable in settings.
        /// </summary>
        private Vector3 ApplyAutoAim(Vector3 aimDirection)
        {
            if (_autoAimStrength <= 0) return aimDirection;

            Vector3 origin = _muzzlePoint != null ? _muzzlePoint.position : transform.position;

            // Find enemies in a cone in front of the player
            Collider[] enemies = Physics.OverlapSphere(origin, _autoAimRange, _enemyLayer);

            float bestScore = float.MaxValue;
            Vector3 bestDirection = aimDirection;

            foreach (Collider enemy in enemies)
            {
                Vector3 toEnemy = (enemy.bounds.center - origin).normalized;
                float angle = Vector3.Angle(aimDirection, toEnemy);

                // Only assist if enemy is within the aim assist cone
                if (angle > _autoAimRadius * 10f) continue;

                float distance = Vector3.Distance(origin, enemy.bounds.center);
                float score = angle + (distance * 0.1f); // Prefer closer + more centered

                if (score < bestScore)
                {
                    bestScore = score;
                    bestDirection = toEnemy;
                }
            }

            // Blend between raw aim and assisted aim
            return Vector3.Slerp(aimDirection, bestDirection, _autoAimStrength).normalized;
        }

        // ====================================================================
        // SPREAD
        // ====================================================================

        private Vector3 ApplySpread(Vector3 direction, float spreadDegrees)
        {
            float spreadRadians = spreadDegrees * Mathf.Deg2Rad;
            Vector3 spread = new Vector3(
                Random.Range(-spreadRadians, spreadRadians),
                Random.Range(-spreadRadians, spreadRadians),
                0f
            );
            return Quaternion.Euler(spread) * direction;
        }

        private void DecaySpread()
        {
            if (_currentWeapon == null) return;
            _currentSpread = Mathf.Max(0, _currentSpread - _currentWeapon.Data.SpreadRecoveryRate * Time.deltaTime);
        }

        private void DecayRecoil()
        {
            if (_currentWeapon == null) return;
            _currentRecoil = Mathf.Max(0, _currentRecoil - _currentWeapon.Data.RecoilRecoverySpeed * Time.deltaTime);
        }

        public float GetCurrentRecoil() => _currentRecoil;

        // ====================================================================
        // RELOADING
        // ====================================================================

        public void StartReload()
        {
            if (_currentWeapon == null || !_currentWeapon.CanReload || _isReloading) return;

            _isReloading = true;
            _animator.SetTrigger(AnimReload);

            if (_currentWeapon.Data.ReloadSound != null)
            {
                _audioSource.PlayOneShot(_currentWeapon.Data.ReloadSound);
            }

            // Reload completes after animation time
            Invoke(nameof(FinishReload), _currentWeapon.Data.ReloadTime);
        }

        private void FinishReload()
        {
            if (_currentWeapon == null) return;

            int ammoNeeded = _currentWeapon.Data.MagazineSize - _currentWeapon.CurrentMagazine;
            int ammoAvailable = Mathf.Min(ammoNeeded, _currentWeapon.ReserveAmmo);

            _currentWeapon.CurrentMagazine += ammoAvailable;
            _currentWeapon.ReserveAmmo -= ammoAvailable;
            _isReloading = false;
        }

        // ====================================================================
        // VISUAL EFFECTS
        // ====================================================================

        private void SpawnMuzzleFlash()
        {
            if (_muzzlePoint == null) return;

            GameObject flash = ObjectPool.Instance?.Get(_muzzleFlashPoolId);
            if (flash != null)
            {
                flash.transform.position = _muzzlePoint.position;
                flash.transform.rotation = _muzzlePoint.rotation;

                // Auto-return to pool after a short duration
                ReturnToPoolAfterDelay(flash, _muzzleFlashPoolId, 0.1f);
            }
        }

        private void SpawnImpactEffect(Vector3 position, Vector3 normal)
        {
            GameObject impact = ObjectPool.Instance?.Get(_bulletImpactPoolId);
            if (impact != null)
            {
                impact.transform.position = position;
                impact.transform.rotation = Quaternion.LookRotation(normal);

                ReturnToPoolAfterDelay(impact, _bulletImpactPoolId, 2f);
            }
        }

        private void ReturnToPoolAfterDelay(GameObject obj, string poolId, float delay)
        {
            StartCoroutine(ReturnToPoolCoroutine(obj, poolId, delay));
        }

        private System.Collections.IEnumerator ReturnToPoolCoroutine(GameObject obj, string poolId, float delay)
        {
            yield return new WaitForSeconds(delay);
            ObjectPool.Instance?.Return(poolId, obj);
        }

        // ====================================================================
        // PUBLIC ACCESSORS
        // ====================================================================

        public List<WeaponInstance> GetAllWeapons() => _weapons;
        public int GetCurrentWeaponIndex() => _currentWeaponIndex;
    }

    /// <summary>
    /// Interface for anything that can take damage (player, NPCs, vehicles).
    /// </summary>
    public interface IDamageable
    {
        void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection);
        float GetHealthPercent();
        bool IsAlive();
    }
}
