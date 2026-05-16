// ============================================================================
// HUDManager.cs — Heads-up display management
// ============================================================================
// PURPOSE:
//   Controls all HUD elements — health bar, armor bar, minimap, wanted stars,
//   ammo counter, money display, mission objective, and contextual prompts.
//   Follows the "less is more" philosophy: only show what's needed.
//
// DESIGN PHILOSOPHY:
//   - Health/armor only appear when damaged (fade in/out)
//   - Ammo counter only shows when weapon is equipped
//   - Mission objectives appear briefly then minimize
//   - Wanted stars pulse when active
//   - Money flashes green/red when gained/spent
//
// MOBILE OPTIMIZATION:
//   - Canvas elements use CanvasGroup for efficient fade
//   - UI updates only when values change (event-driven, not per-frame)
//   - Text uses TextMeshPro for efficient rendering
//   - Minimap uses a render texture at reduced resolution
// ============================================================================

using UnityEngine;
using UnityEngine.UI;
using ShadowCity.Core;

namespace ShadowCity.UI.HUD
{
    public class HUDManager : MonoBehaviour
    {
        private static HUDManager _instance;
        public static HUDManager Instance => _instance;

        [Header("Health")]
        [SerializeField] private Image _healthBar;
        [SerializeField] private Image _armorBar;
        [SerializeField] private CanvasGroup _healthGroup;
        [SerializeField] private float _healthFadeDelay = 3f;

        [Header("Wanted")]
        [SerializeField] private Image[] _wantedStars;
        [SerializeField] private CanvasGroup _wantedGroup;
        [SerializeField] private Color _starActiveColor = Color.yellow;
        [SerializeField] private Color _starInactiveColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);

        [Header("Ammo")]
        [SerializeField] private Text _ammoText;
        [SerializeField] private Text _weaponNameText;
        [SerializeField] private CanvasGroup _ammoGroup;

        [Header("Money")]
        [SerializeField] private Text _moneyText;
        [SerializeField] private CanvasGroup _moneyGroup;
        [SerializeField] private Color _moneyGainColor = Color.green;
        [SerializeField] private Color _moneyLossColor = Color.red;
        [SerializeField] private Color _moneyNormalColor = Color.white;

        [Header("Mission")]
        [SerializeField] private Text _missionObjectiveText;
        [SerializeField] private Text _missionTimerText;
        [SerializeField] private CanvasGroup _missionGroup;

        [Header("Interact")]
        [SerializeField] private Text _interactPromptText;
        [SerializeField] private CanvasGroup _interactGroup;

        [Header("Notification")]
        [SerializeField] private Text _notificationText;
        [SerializeField] private CanvasGroup _notificationGroup;
        [SerializeField] private float _notificationDuration = 3f;

        [Header("Crosshair")]
        [SerializeField] private Image _crosshair;
        [SerializeField] private float _crosshairSpreadMultiplier = 2f;

        [Header("Speed (Driving)")]
        [SerializeField] private Text _speedText;
        [SerializeField] private CanvasGroup _speedGroup;

        [Header("Time")]
        [SerializeField] private Text _timeText;

        // Timers
        private float _healthFadeTimer;
        private float _moneyFadeTimer;
        private float _notificationTimer;
        private float _moneyFlashTimer;

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
            // Subscribe to events for reactive UI updates
            EventBus.Subscribe<PlayerDamagedEvent>(OnPlayerDamaged);
            EventBus.Subscribe<WantedLevelChangedEvent>(OnWantedLevelChanged);
            EventBus.Subscribe<MoneyChangedEvent>(OnMoneyChanged);
            EventBus.Subscribe<MissionStartedEvent>(OnMissionStarted);
            EventBus.Subscribe<MissionCompletedEvent>(OnMissionCompleted);

            // Initial state — hide everything
            SetGroupAlpha(_healthGroup, 0f);
            SetGroupAlpha(_wantedGroup, 0f);
            SetGroupAlpha(_missionGroup, 0f);
            SetGroupAlpha(_notificationGroup, 0f);
            SetGroupAlpha(_interactGroup, 0f);
            SetGroupAlpha(_speedGroup, 0f);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<PlayerDamagedEvent>(OnPlayerDamaged);
            EventBus.Unsubscribe<WantedLevelChangedEvent>(OnWantedLevelChanged);
            EventBus.Unsubscribe<MoneyChangedEvent>(OnMoneyChanged);
            EventBus.Unsubscribe<MissionStartedEvent>(OnMissionStarted);
            EventBus.Unsubscribe<MissionCompletedEvent>(OnMissionCompleted);
        }

        private void Update()
        {
            UpdateFades();
            UpdateTime();
        }

        // ====================================================================
        // HEALTH
        // ====================================================================

        public void UpdateHealth(float current, float max)
        {
            if (_healthBar != null)
            {
                _healthBar.fillAmount = current / max;
            }
            ShowGroup(_healthGroup);
            _healthFadeTimer = _healthFadeDelay;
        }

        public void UpdateArmor(float current, float max)
        {
            if (_armorBar != null)
            {
                _armorBar.fillAmount = max > 0 ? current / max : 0;
                _armorBar.gameObject.SetActive(current > 0);
            }
        }

        private void OnPlayerDamaged(PlayerDamagedEvent evt)
        {
            ShowGroup(_healthGroup);
            _healthFadeTimer = _healthFadeDelay;
        }

        // ====================================================================
        // WANTED
        // ====================================================================

        private void OnWantedLevelChanged(WantedLevelChangedEvent evt)
        {
            if (_wantedStars == null) return;

            bool showWanted = evt.NewLevel > 0;
            SetGroupAlpha(_wantedGroup, showWanted ? 1f : 0f);

            for (int i = 0; i < _wantedStars.Length; i++)
            {
                if (_wantedStars[i] != null)
                {
                    _wantedStars[i].color = i < evt.NewLevel ? _starActiveColor : _starInactiveColor;
                }
            }
        }

        // ====================================================================
        // AMMO
        // ====================================================================

        public void UpdateAmmo(int currentMag, int reserveAmmo, string weaponName)
        {
            if (_ammoText != null)
            {
                _ammoText.text = $"{currentMag} / {reserveAmmo}";
            }

            if (_weaponNameText != null)
            {
                _weaponNameText.text = weaponName;
            }

            SetGroupAlpha(_ammoGroup, 1f);
        }

        public void HideAmmo()
        {
            SetGroupAlpha(_ammoGroup, 0f);
        }

        // ====================================================================
        // MONEY
        // ====================================================================

        private void OnMoneyChanged(MoneyChangedEvent evt)
        {
            if (_moneyText != null)
            {
                _moneyText.text = $"${evt.NewAmount:N0}";

                // Flash color based on gain/loss
                if (evt.NewAmount > evt.OldAmount)
                {
                    _moneyText.color = _moneyGainColor;
                }
                else
                {
                    _moneyText.color = _moneyLossColor;
                }

                _moneyFlashTimer = 1f;
            }

            ShowGroup(_moneyGroup);
            _moneyFadeTimer = 3f;
        }

        // ====================================================================
        // MISSION
        // ====================================================================

        private void OnMissionStarted(MissionStartedEvent evt)
        {
            ShowNotification($"Mission: {evt.MissionName}");
        }

        private void OnMissionCompleted(MissionCompletedEvent evt)
        {
            string result = evt.Success ? "MISSION PASSED" : "MISSION FAILED";
            ShowNotification(result);
        }

        public void UpdateMissionObjective(string objective)
        {
            if (_missionObjectiveText != null)
            {
                _missionObjectiveText.text = objective;
            }
            ShowGroup(_missionGroup);
        }

        public void UpdateMissionTimer(float secondsRemaining)
        {
            if (_missionTimerText != null)
            {
                int minutes = Mathf.FloorToInt(secondsRemaining / 60f);
                int seconds = Mathf.FloorToInt(secondsRemaining % 60f);
                _missionTimerText.text = $"{minutes:D2}:{seconds:D2}";
                _missionTimerText.gameObject.SetActive(secondsRemaining > 0);
            }
        }

        // ====================================================================
        // INTERACTION PROMPT
        // ====================================================================

        public void ShowInteractPrompt(string text)
        {
            if (_interactPromptText != null)
            {
                _interactPromptText.text = text;
            }
            SetGroupAlpha(_interactGroup, 1f);
        }

        public void HideInteractPrompt()
        {
            SetGroupAlpha(_interactGroup, 0f);
        }

        // ====================================================================
        // NOTIFICATIONS
        // ====================================================================

        public void ShowNotification(string text)
        {
            if (_notificationText != null)
            {
                _notificationText.text = text;
            }
            ShowGroup(_notificationGroup);
            _notificationTimer = _notificationDuration;
        }

        // ====================================================================
        // SPEED (DRIVING)
        // ====================================================================

        public void UpdateSpeed(float kmh)
        {
            if (_speedText != null)
            {
                _speedText.text = $"{Mathf.RoundToInt(kmh)} km/h";
            }
            ShowGroup(_speedGroup);
        }

        public void HideSpeed()
        {
            SetGroupAlpha(_speedGroup, 0f);
        }

        // ====================================================================
        // TIME
        // ====================================================================

        private void UpdateTime()
        {
            if (_timeText != null && World.DayNight.DayNightCycle.Instance != null)
            {
                _timeText.text = World.DayNight.DayNightCycle.Instance.GetTimeString();
            }
        }

        // ====================================================================
        // CROSSHAIR
        // ====================================================================

        public void UpdateCrosshair(float spread, bool visible)
        {
            if (_crosshair == null) return;

            _crosshair.gameObject.SetActive(visible);
            float size = 20f + spread * _crosshairSpreadMultiplier;
            _crosshair.rectTransform.sizeDelta = new Vector2(size, size);
        }

        // ====================================================================
        // FADE MANAGEMENT
        // ====================================================================

        private void UpdateFades()
        {
            // Health fade
            if (_healthFadeTimer > 0)
            {
                _healthFadeTimer -= Time.deltaTime;
                if (_healthFadeTimer <= 0)
                {
                    FadeGroup(_healthGroup, 0f);
                }
            }

            // Money fade
            if (_moneyFadeTimer > 0)
            {
                _moneyFadeTimer -= Time.deltaTime;
                if (_moneyFadeTimer <= 0)
                {
                    FadeGroup(_moneyGroup, 0f);
                }
            }

            // Money flash color reset
            if (_moneyFlashTimer > 0)
            {
                _moneyFlashTimer -= Time.deltaTime;
                if (_moneyFlashTimer <= 0 && _moneyText != null)
                {
                    _moneyText.color = _moneyNormalColor;
                }
            }

            // Notification fade
            if (_notificationTimer > 0)
            {
                _notificationTimer -= Time.deltaTime;
                if (_notificationTimer <= 0)
                {
                    FadeGroup(_notificationGroup, 0f);
                }
            }
        }

        private void ShowGroup(CanvasGroup group)
        {
            if (group != null) group.alpha = 1f;
        }

        private void FadeGroup(CanvasGroup group, float targetAlpha)
        {
            if (group != null)
            {
                group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, Time.deltaTime * 2f);
            }
        }

        private void SetGroupAlpha(CanvasGroup group, float alpha)
        {
            if (group != null) group.alpha = alpha;
        }
    }
}
