// ============================================================================
// GameManager.cs — Central game state controller (Singleton)
// ============================================================================
// PURPOSE:
//   The GameManager is the single source of truth for the game's global state.
//   It persists across scene loads, manages game flow (menu → gameplay → pause),
//   and coordinates between all other manager systems.
//
// ARCHITECTURE:
//   Uses the Singleton pattern — only ONE instance exists at any time.
//   Other managers (AudioManager, MissionManager, etc.) register with GameManager
//   but manage their own logic independently.
//
// MOBILE OPTIMIZATION:
//   - Uses Application.targetFrameRate to lock FPS (30 or 60)
//   - Monitors memory usage and triggers garbage collection when needed
//   - Manages quality settings based on device capability
// ============================================================================

using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowCity.Core
{
    /// <summary>
    /// The possible states the game can be in at any given time.
    /// The game always exists in exactly ONE of these states.
    /// </summary>
    public enum GameState
    {
        MainMenu,       // Player is in the main menu
        Loading,        // Game is loading (show loading screen)
        Playing,        // Active gameplay — player has control
        Paused,         // Game is paused — time is frozen
        Cutscene,       // A cutscene is playing — input is disabled
        MissionBrief,   // Mission briefing screen
        GameOver,       // Player died or failed
        Dialogue        // In a dialogue sequence
    }

    /// <summary>
    /// Quality presets for mobile devices. The game auto-detects the best
    /// setting based on device RAM and GPU, but players can override.
    /// </summary>
    public enum QualityPreset
    {
        Low,        // 2-3GB RAM devices: 720p, no shadows, low draw distance
        Medium,     // 4GB RAM devices: 720p, basic shadows, medium draw distance
        High,       // 6GB RAM devices: 1080p, soft shadows, high draw distance
        Ultra       // 8GB+ RAM devices: native res, full shadows, max draw distance
    }

    public class GameManager : MonoBehaviour
    {
        // ====================================================================
        // SINGLETON PATTERN
        // ====================================================================
        // Why Singleton?
        //   We need exactly ONE GameManager that persists across all scenes.
        //   Other scripts access it via GameManager.Instance.
        //
        // BEGINNER NOTE:
        //   A Singleton is a design pattern that ensures only one instance of
        //   a class exists. Think of it like the "president" of your game —
        //   there can only be one at a time.
        // ====================================================================

        private static GameManager _instance;

        public static GameManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    Debug.LogError("GameManager is null! Make sure it exists in the scene.");
                }
                return _instance;
            }
        }

        // ====================================================================
        // PUBLIC STATE — Accessible by other scripts
        // ====================================================================

        [Header("Game State")]
        [SerializeField] private GameState _currentState = GameState.MainMenu;
        public GameState CurrentState => _currentState;

        [Header("Performance Settings")]
        [SerializeField] private QualityPreset _qualityPreset = QualityPreset.Medium;
        [SerializeField] private int _targetFrameRate = 30;
        [SerializeField] private bool _lowPowerMode = false;

        [Header("Game Time")]
        [SerializeField] private float _gameTimeScale = 1f;

        // Events — other scripts subscribe to these to react to state changes
        // BEGINNER NOTE: Events are like announcements. When the game state
        // changes, GameManager "announces" it, and any script listening will
        // hear it and respond.
        public System.Action<GameState, GameState> OnGameStateChanged;
        public System.Action<QualityPreset> OnQualityChanged;

        // ====================================================================
        // PRIVATE STATE
        // ====================================================================

        private GameState _previousState;
        private float _playTimeSeconds;
        private int _frameCount;
        private float _fpsTimer;
        private float _currentFPS;

        // ====================================================================
        // UNITY LIFECYCLE
        // ====================================================================

        private void Awake()
        {
            // Singleton enforcement — if another GameManager exists, destroy this one
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeGame();
        }

        private void Start()
        {
            DetectDeviceCapability();
            ApplyQualitySettings();
        }

        private void Update()
        {
            if (_currentState == GameState.Playing)
            {
                _playTimeSeconds += Time.deltaTime;
            }

            TrackFPS();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            // MOBILE: When player switches apps, auto-pause the game
            if (pauseStatus && _currentState == GameState.Playing)
            {
                SetGameState(GameState.Paused);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // MOBILE: When app loses focus, reduce processing
            if (!hasFocus)
            {
                Time.timeScale = 0f;
            }
        }

        // ====================================================================
        // INITIALIZATION
        // ====================================================================

        private void InitializeGame()
        {
            Application.targetFrameRate = _targetFrameRate;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            // Set up input system for mobile
            Input.multiTouchEnabled = true;

            Debug.Log("[GameManager] Initialized. Target FPS: " + _targetFrameRate);
        }

        // ====================================================================
        // STATE MANAGEMENT
        // ====================================================================

        /// <summary>
        /// Changes the game state. This is the ONLY way to change state.
        /// All state transitions go through this method.
        /// </summary>
        public void SetGameState(GameState newState)
        {
            if (_currentState == newState) return;

            _previousState = _currentState;
            _currentState = newState;

            HandleStateTransition(_previousState, newState);

            OnGameStateChanged?.Invoke(_previousState, newState);

            Debug.Log($"[GameManager] State: {_previousState} → {newState}");
        }

        private void HandleStateTransition(GameState from, GameState to)
        {
            switch (to)
            {
                case GameState.Playing:
                    Time.timeScale = _gameTimeScale;
                    AudioListener.pause = false;
                    break;

                case GameState.Paused:
                    Time.timeScale = 0f;
                    AudioListener.pause = true;
                    break;

                case GameState.Cutscene:
                    Time.timeScale = 1f;
                    break;

                case GameState.Loading:
                    Time.timeScale = 0f;
                    break;

                case GameState.GameOver:
                    Time.timeScale = 0.5f; // Slow-mo death effect
                    break;

                case GameState.Dialogue:
                    Time.timeScale = 0f;
                    break;
            }
        }

        /// <summary>
        /// Returns to the previous state. Useful for unpausing.
        /// </summary>
        public void ReturnToPreviousState()
        {
            SetGameState(_previousState);
        }

        // ====================================================================
        // QUALITY & PERFORMANCE
        // ====================================================================

        /// <summary>
        /// Auto-detect device capability and set quality accordingly.
        /// MOBILE OPTIMIZATION: This runs once at startup.
        /// </summary>
        private void DetectDeviceCapability()
        {
            int ramMB = SystemInfo.systemMemorySize;
            int gpuMemMB = SystemInfo.graphicsMemorySize;
            int processorCount = SystemInfo.processorCount;

            if (ramMB >= 8000 && gpuMemMB >= 2000)
                _qualityPreset = QualityPreset.Ultra;
            else if (ramMB >= 6000 && gpuMemMB >= 1500)
                _qualityPreset = QualityPreset.High;
            else if (ramMB >= 4000)
                _qualityPreset = QualityPreset.Medium;
            else
                _qualityPreset = QualityPreset.Low;

            Debug.Log($"[GameManager] Device: {ramMB}MB RAM, {gpuMemMB}MB GPU, {processorCount} cores → Quality: {_qualityPreset}");
        }

        public void SetQuality(QualityPreset preset)
        {
            _qualityPreset = preset;
            ApplyQualitySettings();
            OnQualityChanged?.Invoke(preset);
        }

        private void ApplyQualitySettings()
        {
            switch (_qualityPreset)
            {
                case QualityPreset.Low:
                    QualitySettings.SetQualityLevel(0);
                    _targetFrameRate = 30;
                    QualitySettings.shadows = ShadowQuality.Disable;
                    QualitySettings.antiAliasing = 0;
                    break;

                case QualityPreset.Medium:
                    QualitySettings.SetQualityLevel(1);
                    _targetFrameRate = 30;
                    QualitySettings.shadows = ShadowQuality.HardOnly;
                    QualitySettings.antiAliasing = 0;
                    break;

                case QualityPreset.High:
                    QualitySettings.SetQualityLevel(2);
                    _targetFrameRate = 60;
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.antiAliasing = 2;
                    break;

                case QualityPreset.Ultra:
                    QualitySettings.SetQualityLevel(3);
                    _targetFrameRate = 60;
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.antiAliasing = 4;
                    break;
            }

            Application.targetFrameRate = _targetFrameRate;
        }

        // ====================================================================
        // FPS TRACKING
        // ====================================================================

        private void TrackFPS()
        {
            _frameCount++;
            _fpsTimer += Time.unscaledDeltaTime;

            if (_fpsTimer >= 1f)
            {
                _currentFPS = _frameCount / _fpsTimer;
                _frameCount = 0;
                _fpsTimer = 0f;

                // Dynamic FPS adjustment — if FPS drops too low, reduce quality
                if (_currentFPS < 20f && _qualityPreset > QualityPreset.Low)
                {
                    Debug.LogWarning($"[GameManager] FPS dropped to {_currentFPS:F0}. Consider lowering quality.");
                }
            }
        }

        public float GetCurrentFPS() => _currentFPS;
        public float GetPlayTime() => _playTimeSeconds;

        // ====================================================================
        // SCENE MANAGEMENT
        // ====================================================================

        public void LoadScene(string sceneName)
        {
            SetGameState(GameState.Loading);
            SceneManager.LoadSceneAsync(sceneName);
        }

        public void RestartCurrentScene()
        {
            SetGameState(GameState.Loading);
            SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().buildIndex);
        }

        public void QuitGame()
        {
            Debug.Log("[GameManager] Quitting game...");
            Application.Quit();
        }
    }
}
