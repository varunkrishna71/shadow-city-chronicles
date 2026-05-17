// ============================================================================
// CheckpointSystem.cs — Mission checkpoint and respawn system
// ============================================================================
// PURPOSE:
//   Saves progress within missions so the player doesn't have to restart
//   from the beginning when they die or fail. Checkpoints are placed
//   at key points during missions.
//
// HOW IT WORKS:
//   1. Mission triggers a checkpoint at key moments
//   2. Checkpoint saves: position, health, ammo, objective progress
//   3. On death/failure, player can restart from last checkpoint
//   4. Checkpoints are cleared when mission ends
//
// MOBILE UX:
//   Checkpoints are CRITICAL for mobile because:
//   - Sessions can be interrupted (phone call, notification)
//   - Players may not have 30 minutes for a full mission
//   - Dying should not mean 15 minutes of lost progress
// ============================================================================

using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.Systems.Checkpoint
{
    [System.Serializable]
    public class CheckpointData
    {
        public Vector3 PlayerPosition;
        public Quaternion PlayerRotation;
        public float PlayerHealth;
        public float PlayerArmor;
        public int Money;
        public int MissionObjectiveIndex;
        public int WantedLevel;
        public float GameTimeHours;
        public string CheckpointId;
    }

    public class CheckpointSystem : MonoBehaviour
    {
        private static CheckpointSystem _instance;
        public static CheckpointSystem Instance => _instance;

        [Header("Settings")]
        [SerializeField] private bool _showCheckpointNotification = true;
        [SerializeField] private float _notificationDuration = 2f;

        private CheckpointData _lastCheckpoint;
        private bool _hasCheckpoint;

        public System.Action OnCheckpointSaved;
        public System.Action OnCheckpointLoaded;

        public bool HasCheckpoint => _hasCheckpoint;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        /// <summary>
        /// Save a checkpoint at the current game state.
        /// Called by the mission system at key points.
        /// </summary>
        public void SaveCheckpoint(string checkpointId = "")
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;

            _lastCheckpoint = new CheckpointData
            {
                PlayerPosition = player.transform.position,
                PlayerRotation = player.transform.rotation,
                CheckpointId = checkpointId
            };

            // Save health
            Health.HealthSystem health = player.GetComponent<Health.HealthSystem>();
            if (health != null)
            {
                _lastCheckpoint.PlayerHealth = health.CurrentHealth;
                _lastCheckpoint.PlayerArmor = health.CurrentArmor;
            }

            // Save money
            if (Economy.EconomySystem.Instance != null)
            {
                _lastCheckpoint.Money = Economy.EconomySystem.Instance.CurrentMoney;
            }

            // Save mission progress
            if (Mission.MissionSystem.Instance != null && Mission.MissionSystem.Instance.HasActiveMission)
            {
                _lastCheckpoint.MissionObjectiveIndex = Mission.MissionSystem.Instance.CurrentMission.CurrentObjectiveIndex;
            }

            // Save wanted level
            if (Wanted.WantedSystem.Instance != null)
            {
                _lastCheckpoint.WantedLevel = Wanted.WantedSystem.Instance.CurrentWantedLevel;
            }

            _hasCheckpoint = true;
            OnCheckpointSaved?.Invoke();

            Debug.Log($"[Checkpoint] Saved: {checkpointId} at {_lastCheckpoint.PlayerPosition}");
        }

        /// <summary>
        /// Reload the last checkpoint. Teleports player and restores state.
        /// </summary>
        public void LoadCheckpoint()
        {
            if (!_hasCheckpoint)
            {
                Debug.LogWarning("[Checkpoint] No checkpoint to load!");
                return;
            }

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;

            // Disable character controller to teleport
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            player.transform.position = _lastCheckpoint.PlayerPosition;
            player.transform.rotation = _lastCheckpoint.PlayerRotation;

            if (cc != null) cc.enabled = true;

            // Restore health
            Health.HealthSystem health = player.GetComponent<Health.HealthSystem>();
            if (health != null)
            {
                health.Respawn(_lastCheckpoint.PlayerPosition, _lastCheckpoint.PlayerHealth / health.MaxHealth);
            }

            // Restore money
            Economy.EconomySystem.Instance?.SetMoney(_lastCheckpoint.Money);

            // Restore wanted level
            Wanted.WantedSystem.Instance?.SetWantedLevel(_lastCheckpoint.WantedLevel);

            // Resume game
            GameManager.Instance?.SetGameState(GameState.Playing);

            OnCheckpointLoaded?.Invoke();

            Debug.Log($"[Checkpoint] Loaded: {_lastCheckpoint.CheckpointId}");
        }

        /// <summary>
        /// Clear the current checkpoint (called when mission ends).
        /// </summary>
        public void ClearCheckpoint()
        {
            _hasCheckpoint = false;
            _lastCheckpoint = null;
        }
    }
}
