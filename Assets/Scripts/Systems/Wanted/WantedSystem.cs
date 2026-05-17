// ============================================================================
// WantedSystem.cs — Police wanted level and response system
// ============================================================================
// PURPOSE:
//   Tracks the player's wanted level (0-5 stars) and controls police response.
//   Committing crimes raises the wanted level. Hiding/escaping lowers it.
//
// HOW IT WORKS:
//   - Each crime adds "heat" — when heat crosses a threshold, wanted level increases
//   - Heat decays over time if player isn't seen by police or civilians
//   - Higher wanted levels trigger more aggressive police response
//   - Wanted level 0 = free. Level 5 = military response
//
// WANTED LEVEL THRESHOLDS:
//   ★☆☆☆☆ (1): Minor crime witnessed. 1-2 officers respond.
//   ★★☆☆☆ (2): Violent crime. 3-4 officers, patrol cars.
//   ★★★☆☆ (3): Serious threat. Roadblocks, 6+ officers.
//   ★★★★☆ (4): Extreme threat. SWAT, armored vehicles.
//   ★★★★★ (5): Maximum response. Military, shoot on sight.
//
// ESCAPE MECHANICS:
//   - Player must break line of sight with all officers
//   - Then stay hidden for a duration (longer at higher levels)
//   - Search radius shrinks over time as player stays hidden
//   - Pay 'n' Spray (auto shop) instantly removes wanted level (costs money)
// ============================================================================

using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.Systems.Wanted
{
    public class WantedSystem : MonoBehaviour
    {
        private static WantedSystem _instance;
        public static WantedSystem Instance => _instance;

        [Header("Wanted Level")]
        [SerializeField] private int _maxWantedLevel = 5;
        [SerializeField] private float[] _levelThresholds = { 20f, 50f, 100f, 175f, 300f };

        [Header("Heat")]
        [SerializeField] private float _heatDecayRate = 2f;         // Per second when unseen
        [SerializeField] private float _heatDecayDelay = 5f;        // Seconds before decay starts
        [SerializeField] private float _witnessHeatBonus = 1.5f;    // Multiplier when witnessed

        [Header("Escape")]
        [SerializeField] private float[] _escapeTimers = { 15f, 30f, 60f, 90f, 120f };
        [SerializeField] private float _searchRadiusBase = 50f;
        [SerializeField] private float _searchRadiusShrinkRate = 2f;

        [Header("Crime Heat Values")]
        [SerializeField] private float _heatCarTheft = 10f;
        [SerializeField] private float _heatAssault = 15f;
        [SerializeField] private float _heatGunfire = 25f;
        [SerializeField] private float _heatKillCivilian = 40f;
        [SerializeField] private float _heatKillPolice = 60f;
        [SerializeField] private float _heatExplosion = 50f;

        // State
        private int _currentWantedLevel;
        private float _currentHeat;
        private float _heatDecayTimer;
        private float _escapeTimer;
        private float _currentSearchRadius;
        private bool _isBeingPursued;
        private bool _isHidden;
        private Vector3 _lastKnownPlayerPosition;

        // Events
        public System.Action<int> OnWantedLevelChanged;
        public System.Action OnEscaped;
        public System.Action OnArrested;

        public int CurrentWantedLevel => _currentWantedLevel;
        public float CurrentHeat => _currentHeat;
        public bool IsBeingPursued => _isBeingPursued;
        public float SearchRadius => _currentSearchRadius;
        public Vector3 LastKnownPlayerPosition => _lastKnownPlayerPosition;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void Start()
        {
            EventBus.Subscribe<WeaponFiredEvent>(OnWeaponFired);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<WeaponFiredEvent>(OnWeaponFired);
        }

        private void Update()
        {
            if (_currentWantedLevel == 0) return;

            UpdateHeatDecay();
            UpdateEscape();
            UpdateWantedLevel();
        }

        // ====================================================================
        // CRIME REPORTING
        // ====================================================================

        /// <summary>
        /// Report a crime committed by the player. Heat is added, and
        /// wanted level may increase.
        /// </summary>
        public void ReportCrime(CrimeType crime, Vector3 position, bool witnessed)
        {
            float heat = GetCrimeHeat(crime);

            if (witnessed)
            {
                heat *= _witnessHeatBonus;
            }

            AddHeat(heat);
            _lastKnownPlayerPosition = position;
            _heatDecayTimer = _heatDecayDelay;

            Debug.Log($"[WantedSystem] Crime: {crime}, Heat: +{heat:F0}, Total: {_currentHeat:F0}, Level: {_currentWantedLevel}");
        }

        private float GetCrimeHeat(CrimeType crime)
        {
            switch (crime)
            {
                case CrimeType.CarTheft: return _heatCarTheft;
                case CrimeType.Assault: return _heatAssault;
                case CrimeType.Gunfire: return _heatGunfire;
                case CrimeType.KillCivilian: return _heatKillCivilian;
                case CrimeType.KillPolice: return _heatKillPolice;
                case CrimeType.Explosion: return _heatExplosion;
                default: return 5f;
            }
        }

        // ====================================================================
        // HEAT MANAGEMENT
        // ====================================================================

        private void AddHeat(float amount)
        {
            _currentHeat += amount;
        }

        private void UpdateHeatDecay()
        {
            if (_isBeingPursued) return; // No decay while being chased

            _heatDecayTimer -= Time.deltaTime;
            if (_heatDecayTimer > 0) return;

            _currentHeat -= _heatDecayRate * Time.deltaTime;
            _currentHeat = Mathf.Max(0, _currentHeat);
        }

        private void UpdateWantedLevel()
        {
            int newLevel = 0;

            for (int i = 0; i < _levelThresholds.Length; i++)
            {
                if (_currentHeat >= _levelThresholds[i])
                {
                    newLevel = i + 1;
                }
            }

            newLevel = Mathf.Min(newLevel, _maxWantedLevel);

            if (newLevel != _currentWantedLevel)
            {
                int oldLevel = _currentWantedLevel;
                _currentWantedLevel = newLevel;

                EventBus.Publish(new WantedLevelChangedEvent
                {
                    OldLevel = oldLevel,
                    NewLevel = newLevel
                });

                OnWantedLevelChanged?.Invoke(newLevel);

                if (newLevel == 0)
                {
                    OnEscaped?.Invoke();
                    _isBeingPursued = false;
                }
                else if (newLevel > 0 && !_isBeingPursued)
                {
                    _isBeingPursued = true;
                }
            }
        }

        // ====================================================================
        // ESCAPE
        // ====================================================================

        /// <summary>
        /// Called when police lose sight of the player.
        /// Starts the escape timer based on current wanted level.
        /// </summary>
        public void PlayerHidden()
        {
            if (_currentWantedLevel == 0) return;

            _isHidden = true;
            _escapeTimer = _escapeTimers[Mathf.Min(_currentWantedLevel - 1, _escapeTimers.Length - 1)];
            _currentSearchRadius = _searchRadiusBase * _currentWantedLevel;
        }

        /// <summary>
        /// Called when police spot the player again during escape.
        /// Resets the escape timer.
        /// </summary>
        public void PlayerSpotted(Vector3 position)
        {
            _isHidden = false;
            _lastKnownPlayerPosition = position;
            _escapeTimer = 0;
        }

        private void UpdateEscape()
        {
            if (!_isHidden) return;

            _escapeTimer -= Time.deltaTime;
            _currentSearchRadius -= _searchRadiusShrinkRate * Time.deltaTime;
            _currentSearchRadius = Mathf.Max(10f, _currentSearchRadius);

            if (_escapeTimer <= 0)
            {
                // Player escaped!
                ClearWantedLevel();
            }
        }

        // ====================================================================
        // PUBLIC API
        // ====================================================================

        /// <summary>
        /// Immediately clear wanted level (e.g., Pay 'n' Spray, cheat code).
        /// </summary>
        public void ClearWantedLevel()
        {
            int oldLevel = _currentWantedLevel;
            _currentWantedLevel = 0;
            _currentHeat = 0;
            _isBeingPursued = false;
            _isHidden = false;

            if (oldLevel > 0)
            {
                EventBus.Publish(new WantedLevelChangedEvent
                {
                    OldLevel = oldLevel,
                    NewLevel = 0
                });
                OnEscaped?.Invoke();
            }
        }

        /// <summary>
        /// Force a specific wanted level (for missions/scripted events).
        /// </summary>
        public void SetWantedLevel(int level)
        {
            level = Mathf.Clamp(level, 0, _maxWantedLevel);

            if (level > 0)
            {
                _currentHeat = _levelThresholds[level - 1];
            }
            else
            {
                ClearWantedLevel();
            }
        }

        // ====================================================================
        // EVENT HANDLERS
        // ====================================================================

        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            ReportCrime(CrimeType.Gunfire, evt.Origin, true);
        }
    }

    public enum CrimeType
    {
        CarTheft,
        Assault,
        Gunfire,
        KillCivilian,
        KillPolice,
        Explosion,
        Trespassing,
        Speeding,
        HitAndRun
    }
}
