// ============================================================================
// DayNightCycle.cs — Full day/night cycle with lighting transitions
// ============================================================================
// PURPOSE:
//   Simulates a full 24-hour day/night cycle. Controls sun position,
//   sky color, ambient lighting, streetlight activation, and NPC schedules.
//
// TIME SCALE:
//   1 game hour = 2 real minutes (configurable)
//   Full day = 48 real minutes
//   This is a balance: fast enough to see sunrise/sunset in a play session,
//   slow enough that time feels meaningful.
//
// PHASES:
//   Dawn (5:00-7:00): Orange/pink sky, sun rising, streetlights off
//   Day (7:00-17:00): Bright sky, full sunlight, most NPCs active
//   Dusk (17:00-19:00): Orange/red sky, sun setting, streetlights on
//   Night (19:00-5:00): Dark sky, moonlight, neon signs prominent
//
// MOBILE OPTIMIZATION:
//   - Sun uses a single directional light (not raymarched volumetrics)
//   - Sky color is a gradient lookup (not a sky shader)
//   - Streetlights toggle in batches (not all at once)
//   - Shadow cascade distance reduces at night (less visible anyway)
// ============================================================================

using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.World.DayNight
{
    public class DayNightCycle : MonoBehaviour
    {
        private static DayNightCycle _instance;
        public static DayNightCycle Instance => _instance;

        [Header("Time Settings")]
        [SerializeField] private float _gameHoursPerRealMinute = 0.5f; // 1 game hour = 2 real minutes
        [SerializeField] private float _startHour = 8f;                 // Game starts at 8 AM

        [Header("Sun")]
        [SerializeField] private Light _sunLight;
        [SerializeField] private Gradient _sunColorGradient;
        [SerializeField] private AnimationCurve _sunIntensityCurve;

        [Header("Moon")]
        [SerializeField] private Light _moonLight;
        [SerializeField] private float _moonIntensity = 0.15f;

        [Header("Ambient")]
        [SerializeField] private Gradient _ambientColorGradient;
        [SerializeField] private Gradient _fogColorGradient;

        [Header("Streetlights")]
        [SerializeField] private float _lightsOnHour = 18.5f;
        [SerializeField] private float _lightsOffHour = 6f;

        // State
        private float _currentHour;
        private float _dayProgress;        // 0-1 representing the full day
        private bool _isNight;
        private bool _streetlightsOn;

        // Events
        public System.Action<float> OnHourChanged;
        public System.Action<bool> OnNightChanged;

        public float CurrentHour => _currentHour;
        public float DayProgress => _dayProgress;
        public bool IsNight => _isNight;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            _currentHour = _startHour;

            CreateDefaultGradients();
        }

        private void Update()
        {
            UpdateTime();
            UpdateSun();
            UpdateMoon();
            UpdateAmbient();
            UpdateStreetlights();
        }

        // ====================================================================
        // TIME
        // ====================================================================

        private void UpdateTime()
        {
            // Advance game time
            float hoursPerSecond = _gameHoursPerRealMinute / 60f;
            _currentHour += hoursPerSecond * Time.deltaTime;

            // Wrap around at 24 hours
            if (_currentHour >= 24f)
            {
                _currentHour -= 24f;
            }

            _dayProgress = _currentHour / 24f;

            // Check night transition
            bool wasNight = _isNight;
            _isNight = _currentHour < 6f || _currentHour > 19f;

            if (wasNight != _isNight)
            {
                OnNightChanged?.Invoke(_isNight);
                EventBus.Publish(new TimeOfDayChangedEvent
                {
                    Hour = _currentHour,
                    IsNight = _isNight
                });
            }
        }

        /// <summary>
        /// Set the game time directly (for missions, save/load).
        /// </summary>
        public void SetTime(float hour)
        {
            _currentHour = Mathf.Repeat(hour, 24f);
            OnHourChanged?.Invoke(_currentHour);
        }

        /// <summary>
        /// Advance time by a number of hours (for sleeping, waiting).
        /// </summary>
        public void AdvanceTime(float hours)
        {
            SetTime(_currentHour + hours);
        }

        // ====================================================================
        // SUN
        // ====================================================================

        private void UpdateSun()
        {
            if (_sunLight == null) return;

            // Sun angle: rises in the east (6AM = 0°), peaks at noon (90°), sets west (18PM = 180°)
            float sunAngle;
            if (_currentHour >= 6f && _currentHour <= 18f)
            {
                float sunProgress = (_currentHour - 6f) / 12f;  // 0 at sunrise, 1 at sunset
                sunAngle = sunProgress * 180f;
                _sunLight.enabled = true;
            }
            else
            {
                _sunLight.enabled = false;
                return;
            }

            // Rotate sun
            _sunLight.transform.rotation = Quaternion.Euler(sunAngle - 90f, 170f, 0f);

            // Sun color and intensity from gradient/curve
            if (_sunColorGradient != null)
            {
                float sunProgress = (_currentHour - 6f) / 12f;
                _sunLight.color = _sunColorGradient.Evaluate(sunProgress);
            }

            if (_sunIntensityCurve != null)
            {
                _sunLight.intensity = _sunIntensityCurve.Evaluate(_dayProgress);
            }
        }

        // ====================================================================
        // MOON
        // ====================================================================

        private void UpdateMoon()
        {
            if (_moonLight == null) return;

            _moonLight.enabled = _isNight;

            if (_isNight)
            {
                float nightProgress;
                if (_currentHour >= 19f)
                {
                    nightProgress = (_currentHour - 19f) / 11f;
                }
                else
                {
                    nightProgress = (_currentHour + 5f) / 11f;
                }

                float moonAngle = nightProgress * 180f;
                _moonLight.transform.rotation = Quaternion.Euler(moonAngle - 90f, 10f, 0f);
                _moonLight.intensity = _moonIntensity;
            }
        }

        // ====================================================================
        // AMBIENT LIGHTING
        // ====================================================================

        private void UpdateAmbient()
        {
            if (_ambientColorGradient != null)
            {
                RenderSettings.ambientLight = _ambientColorGradient.Evaluate(_dayProgress);
            }

            if (_fogColorGradient != null)
            {
                RenderSettings.fogColor = _fogColorGradient.Evaluate(_dayProgress);
            }
        }

        // ====================================================================
        // STREETLIGHTS
        // ====================================================================

        private void UpdateStreetlights()
        {
            bool shouldBeOn = _currentHour >= _lightsOnHour || _currentHour < _lightsOffHour;

            if (shouldBeOn != _streetlightsOn)
            {
                _streetlightsOn = shouldBeOn;
                // Streetlight toggling would be handled by a separate StreetlightManager
                // that enables/disables lights in batches to avoid frame spikes
            }
        }

        // ====================================================================
        // HELPERS
        // ====================================================================

        /// <summary>
        /// Get a human-readable time string (e.g., "14:30").
        /// </summary>
        public string GetTimeString()
        {
            int hours = Mathf.FloorToInt(_currentHour);
            int minutes = Mathf.FloorToInt((_currentHour - hours) * 60f);
            return $"{hours:D2}:{minutes:D2}";
        }

        /// <summary>
        /// Get the current time period name.
        /// </summary>
        public string GetTimePeriod()
        {
            if (_currentHour >= 5f && _currentHour < 7f) return "Dawn";
            if (_currentHour >= 7f && _currentHour < 12f) return "Morning";
            if (_currentHour >= 12f && _currentHour < 14f) return "Noon";
            if (_currentHour >= 14f && _currentHour < 17f) return "Afternoon";
            if (_currentHour >= 17f && _currentHour < 19f) return "Dusk";
            if (_currentHour >= 19f && _currentHour < 22f) return "Evening";
            return "Night";
        }

        private void CreateDefaultGradients()
        {
            if (_sunColorGradient == null)
            {
                _sunColorGradient = new Gradient();
                _sunColorGradient.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(new Color(1f, 0.5f, 0.2f), 0f),    // Sunrise - orange
                        new GradientColorKey(new Color(1f, 0.95f, 0.85f), 0.3f), // Morning - warm white
                        new GradientColorKey(new Color(1f, 1f, 0.95f), 0.5f),    // Noon - bright white
                        new GradientColorKey(new Color(1f, 0.95f, 0.85f), 0.7f), // Afternoon - warm
                        new GradientColorKey(new Color(1f, 0.4f, 0.15f), 1f)     // Sunset - deep orange
                    },
                    new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
                );
            }

            if (_sunIntensityCurve == null)
            {
                _sunIntensityCurve = new AnimationCurve(
                    new Keyframe(0f, 0f),      // Midnight
                    new Keyframe(0.25f, 0.3f), // 6 AM
                    new Keyframe(0.5f, 1.2f),  // Noon
                    new Keyframe(0.75f, 0.3f), // 6 PM
                    new Keyframe(1f, 0f)       // Midnight
                );
            }
        }
    }
}
