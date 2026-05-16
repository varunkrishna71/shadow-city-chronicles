// ============================================================================
// WeatherSystem.cs — Dynamic weather system with smooth transitions
// ============================================================================
// PURPOSE:
//   Controls the game's weather — clear skies, clouds, rain, fog, and
//   thunderstorms. Weather affects gameplay (wet roads = less grip),
//   visuals (rain particles, fog density), and audio (rain sounds).
//
// WEATHER TYPES:
//   Clear → Sunny sky, good visibility, dry roads
//   Cloudy → Overcast, reduced light, moderate visibility
//   Rain → Wet streets with reflections, reduced grip, rain particles
//   HeavyRain → Intense rain, very low visibility, flooded streets
//   Fog → Dense fog, extremely low visibility, eerie atmosphere
//   Thunderstorm → Heavy rain + lightning flashes + thunder sounds
//
// MOBILE OPTIMIZATION:
//   - Rain uses a single particle system (not per-drop physics)
//   - Fog uses Unity's built-in fog (free on GPU)
//   - Lightning is a screen flash (not volumetric light)
//   - Wet road effect is a material property change (not extra geometry)
//   - Weather transitions spread changes over multiple frames
// ============================================================================

using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.World.Weather
{
    public enum WeatherType
    {
        Clear,
        Cloudy,
        Rain,
        HeavyRain,
        Fog,
        Thunderstorm
    }

    [System.Serializable]
    public class WeatherPreset
    {
        public WeatherType Type;

        [Header("Sky")]
        public Color SkyColor;
        public Color AmbientColor;
        public float SunIntensity;
        public Color FogColor;
        public float FogDensity;

        [Header("Effects")]
        public float RainIntensity;          // 0 = no rain, 1 = max
        public float WindStrength;
        public float WetnessFactor;          // 0 = dry, 1 = soaked

        [Header("Gameplay")]
        public float TractionMultiplier;     // How much grip vehicles have
        public float VisibilityRange;        // Draw distance modifier

        [Header("Probability")]
        public float Weight;                 // How likely this weather is
        public float MinDuration;            // Minimum seconds
        public float MaxDuration;            // Maximum seconds
    }

    public class WeatherSystem : MonoBehaviour
    {
        private static WeatherSystem _instance;
        public static WeatherSystem Instance => _instance;

        [Header("Weather Presets")]
        [SerializeField] private WeatherPreset[] _presets;

        [Header("Transitions")]
        [SerializeField] private float _transitionDuration = 10f;

        [Header("Rain")]
        [SerializeField] private ParticleSystem _rainParticles;
        [SerializeField] private int _maxRainParticles = 2000;

        [Header("Lightning")]
        [SerializeField] private float _minLightningInterval = 5f;
        [SerializeField] private float _maxLightningInterval = 20f;
        [SerializeField] private Light _lightningLight;

        [Header("Wind")]
        [SerializeField] private float _windChangeSpeed = 0.5f;

        // State
        private WeatherType _currentWeather = WeatherType.Clear;
        private WeatherType _targetWeather = WeatherType.Clear;
        private WeatherPreset _currentPreset;
        private WeatherPreset _targetPreset;
        private float _transitionProgress;
        private bool _isTransitioning;

        // Weather timer
        private float _weatherTimer;
        private float _currentWeatherDuration;

        // Lightning
        private float _lightningTimer;
        private float _nextLightningTime;
        private float _lightningFlashTimer;

        // Interpolated values
        private float _currentRainIntensity;
        private float _currentFogDensity;
        private float _currentWetness;
        private float _currentTraction = 1f;
        private Vector3 _currentWindDirection;

        // Events
        public System.Action<WeatherType> OnWeatherChanged;

        public WeatherType CurrentWeather => _currentWeather;
        public float RainIntensity => _currentRainIntensity;
        public float Wetness => _currentWetness;
        public float TractionMultiplier => _currentTraction;
        public Vector3 WindDirection => _currentWindDirection;

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
            if (_presets == null || _presets.Length == 0)
            {
                CreateDefaultPresets();
            }

            _currentPreset = GetPreset(WeatherType.Clear);
            _targetPreset = _currentPreset;
            ApplyWeatherImmediate(_currentPreset);
            ScheduleNextWeatherChange();
        }

        private void Update()
        {
            UpdateTransition();
            UpdateWeatherTimer();
            UpdateLightning();
            UpdateWind();
            UpdateRain();
        }

        // ====================================================================
        // WEATHER TRANSITION
        // ====================================================================

        /// <summary>
        /// Start transitioning to a new weather type.
        /// </summary>
        public void SetWeather(WeatherType newWeather, float transitionTime = -1f)
        {
            if (newWeather == _currentWeather && !_isTransitioning) return;

            _targetWeather = newWeather;
            _targetPreset = GetPreset(newWeather);
            _transitionProgress = 0f;
            _isTransitioning = true;

            if (transitionTime >= 0)
            {
                _transitionDuration = transitionTime;
            }

            EventBus.Publish(new WeatherChangedEvent
            {
                NewWeather = newWeather.ToString(),
                TransitionDuration = _transitionDuration
            });
        }

        private void UpdateTransition()
        {
            if (!_isTransitioning) return;

            _transitionProgress += Time.deltaTime / _transitionDuration;

            if (_transitionProgress >= 1f)
            {
                _transitionProgress = 1f;
                _isTransitioning = false;
                _currentWeather = _targetWeather;
                _currentPreset = _targetPreset;
                OnWeatherChanged?.Invoke(_currentWeather);
                ScheduleNextWeatherChange();
            }

            // Interpolate all weather values
            float t = Mathf.SmoothStep(0f, 1f, _transitionProgress);

            // Sky and lighting
            RenderSettings.ambientLight = Color.Lerp(_currentPreset.AmbientColor, _targetPreset.AmbientColor, t);
            RenderSettings.fogColor = Color.Lerp(_currentPreset.FogColor, _targetPreset.FogColor, t);
            _currentFogDensity = Mathf.Lerp(_currentPreset.FogDensity, _targetPreset.FogDensity, t);
            RenderSettings.fogDensity = _currentFogDensity;
            RenderSettings.fog = _currentFogDensity > 0.001f;

            // Rain
            _currentRainIntensity = Mathf.Lerp(_currentPreset.RainIntensity, _targetPreset.RainIntensity, t);
            _currentWetness = Mathf.Lerp(_currentPreset.WetnessFactor, _targetPreset.WetnessFactor, t);
            _currentTraction = Mathf.Lerp(_currentPreset.TractionMultiplier, _targetPreset.TractionMultiplier, t);
        }

        private void ApplyWeatherImmediate(WeatherPreset preset)
        {
            RenderSettings.ambientLight = preset.AmbientColor;
            RenderSettings.fogColor = preset.FogColor;
            RenderSettings.fogDensity = preset.FogDensity;
            RenderSettings.fog = preset.FogDensity > 0.001f;

            _currentRainIntensity = preset.RainIntensity;
            _currentWetness = preset.WetnessFactor;
            _currentTraction = preset.TractionMultiplier;
            _currentFogDensity = preset.FogDensity;
        }

        // ====================================================================
        // WEATHER SCHEDULING
        // ====================================================================

        private void UpdateWeatherTimer()
        {
            if (_isTransitioning) return;

            _weatherTimer -= Time.deltaTime;
            if (_weatherTimer <= 0)
            {
                PickRandomWeather();
            }
        }

        private void ScheduleNextWeatherChange()
        {
            _currentWeatherDuration = Random.Range(_currentPreset.MinDuration, _currentPreset.MaxDuration);
            _weatherTimer = _currentWeatherDuration;
        }

        private void PickRandomWeather()
        {
            float totalWeight = 0f;
            foreach (WeatherPreset preset in _presets)
            {
                totalWeight += preset.Weight;
            }

            float random = Random.Range(0f, totalWeight);
            float cumulative = 0f;

            foreach (WeatherPreset preset in _presets)
            {
                cumulative += preset.Weight;
                if (random <= cumulative)
                {
                    SetWeather(preset.Type);
                    return;
                }
            }
        }

        // ====================================================================
        // RAIN PARTICLES
        // ====================================================================

        private void UpdateRain()
        {
            if (_rainParticles == null) return;

            if (_currentRainIntensity > 0.01f)
            {
                if (!_rainParticles.isPlaying) _rainParticles.Play();

                var emission = _rainParticles.emission;
                emission.rateOverTime = _currentRainIntensity * _maxRainParticles;

                // Follow camera
                if (UnityEngine.Camera.main != null)
                {
                    _rainParticles.transform.position = UnityEngine.Camera.main.transform.position + Vector3.up * 20f;
                }
            }
            else
            {
                if (_rainParticles.isPlaying) _rainParticles.Stop();
            }
        }

        // ====================================================================
        // LIGHTNING
        // ====================================================================

        private void UpdateLightning()
        {
            if (_currentWeather != WeatherType.Thunderstorm) return;

            _lightningTimer += Time.deltaTime;

            if (_lightningTimer >= _nextLightningTime)
            {
                TriggerLightning();
                _lightningTimer = 0f;
                _nextLightningTime = Random.Range(_minLightningInterval, _maxLightningInterval);
            }

            // Flash decay
            if (_lightningFlashTimer > 0)
            {
                _lightningFlashTimer -= Time.deltaTime * 5f;
                if (_lightningLight != null)
                {
                    _lightningLight.intensity = _lightningFlashTimer * 3f;
                }
            }
        }

        private void TriggerLightning()
        {
            _lightningFlashTimer = 1f;

            if (_lightningLight != null)
            {
                _lightningLight.intensity = 3f;
                _lightningLight.transform.position = UnityEngine.Camera.main.transform.position +
                    new Vector3(Random.Range(-50f, 50f), 100f, Random.Range(-50f, 50f));
            }
        }

        // ====================================================================
        // WIND
        // ====================================================================

        private void UpdateWind()
        {
            float windTarget = _targetPreset != null ? _targetPreset.WindStrength : 0f;
            float time = Time.time * _windChangeSpeed;
            _currentWindDirection = new Vector3(
                Mathf.PerlinNoise(time, 0f) - 0.5f,
                0f,
                Mathf.PerlinNoise(0f, time) - 0.5f
            ).normalized * windTarget;
        }

        // ====================================================================
        // HELPERS
        // ====================================================================

        private WeatherPreset GetPreset(WeatherType type)
        {
            foreach (WeatherPreset preset in _presets)
            {
                if (preset.Type == type) return preset;
            }
            return _presets.Length > 0 ? _presets[0] : null;
        }

        private void CreateDefaultPresets()
        {
            _presets = new WeatherPreset[]
            {
                new WeatherPreset
                {
                    Type = WeatherType.Clear,
                    AmbientColor = new Color(0.6f, 0.6f, 0.7f),
                    FogColor = new Color(0.7f, 0.8f, 0.9f),
                    FogDensity = 0.001f,
                    RainIntensity = 0f,
                    WindStrength = 0.2f,
                    WetnessFactor = 0f,
                    TractionMultiplier = 1f,
                    Weight = 3f,
                    MinDuration = 300f,
                    MaxDuration = 600f
                },
                new WeatherPreset
                {
                    Type = WeatherType.Cloudy,
                    AmbientColor = new Color(0.4f, 0.4f, 0.5f),
                    FogColor = new Color(0.5f, 0.5f, 0.55f),
                    FogDensity = 0.003f,
                    RainIntensity = 0f,
                    WindStrength = 0.5f,
                    WetnessFactor = 0f,
                    TractionMultiplier = 1f,
                    Weight = 3f,
                    MinDuration = 200f,
                    MaxDuration = 500f
                },
                new WeatherPreset
                {
                    Type = WeatherType.Rain,
                    AmbientColor = new Color(0.3f, 0.3f, 0.4f),
                    FogColor = new Color(0.4f, 0.4f, 0.45f),
                    FogDensity = 0.008f,
                    RainIntensity = 0.5f,
                    WindStrength = 1f,
                    WetnessFactor = 0.7f,
                    TractionMultiplier = 0.7f,
                    Weight = 2f,
                    MinDuration = 180f,
                    MaxDuration = 400f
                },
                new WeatherPreset
                {
                    Type = WeatherType.Thunderstorm,
                    AmbientColor = new Color(0.15f, 0.15f, 0.2f),
                    FogColor = new Color(0.2f, 0.2f, 0.25f),
                    FogDensity = 0.015f,
                    RainIntensity = 1f,
                    WindStrength = 2f,
                    WetnessFactor = 1f,
                    TractionMultiplier = 0.5f,
                    Weight = 1f,
                    MinDuration = 120f,
                    MaxDuration = 300f
                }
            };
        }
    }
}
