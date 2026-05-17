// ============================================================================
// MissionSystem.cs — Complete mission management and scripting system
// ============================================================================
// PURPOSE:
//   Manages all missions in the game — story missions, side missions,
//   random events. Handles mission flow, objectives, rewards, and scripting.
//
// ARCHITECTURE:
//   Missions are defined as ScriptableObjects (data-driven).
//   Each mission contains a list of OBJECTIVES that must be completed in order.
//   Objectives can be: go to location, kill targets, collect items, survive,
//   drive somewhere, escort someone, etc.
//
// MISSION FLOW:
//   Available → Accepted → Active → [Objective 1] → [Objective 2] → ... → Complete/Failed
//
// MOBILE OPTIMIZATION:
//   - Only the active mission's scripts run
//   - Inactive mission triggers use simple distance checks (not colliders)
//   - Mission UI updates on event, not per-frame
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.Systems.Mission
{
    [CreateAssetMenu(fileName = "NewMission", menuName = "ShadowCity/Mission Data")]
    public class MissionData : ScriptableObject
    {
        [Header("Identity")]
        public string MissionId;
        public string MissionName;
        [TextArea(3, 10)]
        public string Description;
        public MissionType Type;
        public Sprite Icon;

        [Header("Requirements")]
        public string[] RequiredCompletedMissions;  // Must complete these first
        public int RequiredPlayerLevel;
        public int RequiredMoney;

        [Header("Objectives")]
        public ObjectiveData[] Objectives;

        [Header("Rewards")]
        public int MoneyReward;
        public int RespectReward;
        public string[] ItemRewards;           // Item IDs
        public string UnlocksWeapon;           // Weapon ID
        public string UnlocksMission;          // Next mission ID

        [Header("Settings")]
        public Vector3 StartLocation;
        public float TimeLimit;                // 0 = no time limit
        public bool FailOnPlayerDeath = true;
        public bool FailOnVehicleDestroyed;
        public bool RestartOnFail = true;

        [Header("Story")]
        public string IntroDialogueId;
        public string OutroDialogueId;
        public string[] CutsceneIds;
    }

    public enum MissionType
    {
        Story,          // Main story missions
        Side,           // Optional side missions
        Stranger,       // Random encounter missions
        Taxi,           // Taxi fare missions
        Race,           // Vehicle races
        Assassination,  // Target elimination
        Collection,     // Collectible hunts
        Gang,           // Gang territory missions
        Heist           // Multi-part heist missions
    }

    [System.Serializable]
    public class ObjectiveData
    {
        public string ObjectiveId;
        [TextArea(1, 3)]
        public string Description;          // "Go to the warehouse"
        public ObjectiveType Type;
        public Vector3 TargetLocation;
        public float TargetRadius = 5f;
        public string TargetId;             // NPC ID, item ID, etc.
        public int TargetCount = 1;         // How many to kill/collect
        public float TimeLimit;             // 0 = no limit
        public bool IsOptional;             // Optional bonus objectives
        public bool ShowOnMap = true;
    }

    public enum ObjectiveType
    {
        GoToLocation,       // Reach a waypoint
        KillTarget,         // Kill specific NPC(s)
        KillCount,          // Kill X enemies
        CollectItem,        // Pick up specific item
        DeliverItem,        // Bring item to location
        EscortNPC,          // Protect an NPC
        SurviveTime,        // Stay alive for X seconds
        DriveToLocation,    // Drive a specific vehicle somewhere
        FollowNPC,          // Follow without being detected
        TalkToNPC,          // Interact with specific NPC
        LoseWantedLevel,    // Escape the police
        DestroyTarget,      // Destroy vehicles/objects
        TakePhoto,          // Use camera at location (future feature)
        Custom              // Scripted objective
    }

    public enum MissionStatus
    {
        Locked,         // Requirements not met
        Available,      // Can be started
        Active,         // Currently in progress
        Completed,      // Successfully finished
        Failed          // Player failed
    }

    /// <summary>
    /// Runtime tracking for an active mission.
    /// </summary>
    public class ActiveMission
    {
        public MissionData Data;
        public MissionStatus Status;
        public int CurrentObjectiveIndex;
        public Dictionary<string, int> ObjectiveProgress;  // objectiveId → progress
        public float ElapsedTime;
        public float TimeRemaining;
        public Vector3 CheckpointPosition;
        public int CheckpointObjectiveIndex;

        public ActiveMission(MissionData data)
        {
            Data = data;
            Status = MissionStatus.Active;
            CurrentObjectiveIndex = 0;
            ObjectiveProgress = new Dictionary<string, int>();
            ElapsedTime = 0f;
            TimeRemaining = data.TimeLimit;
        }

        public ObjectiveData CurrentObjective =>
            CurrentObjectiveIndex < Data.Objectives.Length
                ? Data.Objectives[CurrentObjectiveIndex]
                : null;
    }

    public class MissionSystem : MonoBehaviour
    {
        private static MissionSystem _instance;
        public static MissionSystem Instance => _instance;

        [Header("Mission Database")]
        [SerializeField] private MissionData[] _allMissions;

        // State
        private ActiveMission _currentMission;
        private Dictionary<string, MissionStatus> _missionStatuses = new Dictionary<string, MissionStatus>();
        private List<string> _completedMissions = new List<string>();

        // Events
        public System.Action<MissionData> OnMissionStarted;
        public System.Action<MissionData, bool> OnMissionEnded;  // data, success
        public System.Action<ObjectiveData> OnObjectiveUpdated;
        public System.Action<ObjectiveData> OnObjectiveCompleted;

        public ActiveMission CurrentMission => _currentMission;
        public bool HasActiveMission => _currentMission != null;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            InitializeMissionStatuses();
        }

        private void Update()
        {
            if (_currentMission == null) return;

            _currentMission.ElapsedTime += Time.deltaTime;

            // Time limit check
            if (_currentMission.Data.TimeLimit > 0)
            {
                _currentMission.TimeRemaining -= Time.deltaTime;
                if (_currentMission.TimeRemaining <= 0)
                {
                    FailMission("Time ran out!");
                }
            }

            // Check current objective completion
            CheckObjectiveCompletion();
        }

        // ====================================================================
        // MISSION LIFECYCLE
        // ====================================================================

        private void InitializeMissionStatuses()
        {
            foreach (MissionData mission in _allMissions)
            {
                _missionStatuses[mission.MissionId] = MissionStatus.Locked;
            }

            // Unlock first story mission
            if (_allMissions.Length > 0)
            {
                _missionStatuses[_allMissions[0].MissionId] = MissionStatus.Available;
            }
        }

        /// <summary>
        /// Start a mission. Returns false if requirements aren't met.
        /// </summary>
        public bool StartMission(string missionId)
        {
            MissionData data = System.Array.Find(_allMissions, m => m.MissionId == missionId);
            if (data == null)
            {
                Debug.LogError($"[MissionSystem] Mission '{missionId}' not found!");
                return false;
            }

            if (!CanStartMission(data))
            {
                Debug.Log($"[MissionSystem] Cannot start '{missionId}' — requirements not met.");
                return false;
            }

            // End current mission if one is active
            if (_currentMission != null)
            {
                FailMission("Started another mission.");
            }

            _currentMission = new ActiveMission(data);
            _missionStatuses[missionId] = MissionStatus.Active;

            // Save checkpoint at mission start
            _currentMission.CheckpointPosition = GetPlayerPosition();
            _currentMission.CheckpointObjectiveIndex = 0;

            EventBus.Publish(new MissionStartedEvent
            {
                MissionId = data.MissionId,
                MissionName = data.MissionName
            });

            OnMissionStarted?.Invoke(data);

            Debug.Log($"[MissionSystem] Started mission: {data.MissionName}");
            return true;
        }

        /// <summary>
        /// Complete the current mission successfully.
        /// </summary>
        public void CompleteMission()
        {
            if (_currentMission == null) return;

            MissionData data = _currentMission.Data;
            _currentMission.Status = MissionStatus.Completed;
            _missionStatuses[data.MissionId] = MissionStatus.Completed;
            _completedMissions.Add(data.MissionId);

            // Grant rewards
            if (data.MoneyReward > 0)
            {
                Economy.EconomySystem.Instance?.AddMoney(data.MoneyReward, $"Mission: {data.MissionName}");
            }

            // Unlock next missions
            if (!string.IsNullOrEmpty(data.UnlocksMission))
            {
                UnlockMission(data.UnlocksMission);
            }

            // Also check all missions for unlockability
            RefreshMissionAvailability();

            EventBus.Publish(new MissionCompletedEvent
            {
                MissionId = data.MissionId,
                Success = true,
                MoneyReward = data.MoneyReward
            });

            OnMissionEnded?.Invoke(data, true);

            Debug.Log($"[MissionSystem] Completed mission: {data.MissionName}");
            _currentMission = null;
        }

        /// <summary>
        /// Fail the current mission.
        /// </summary>
        public void FailMission(string reason)
        {
            if (_currentMission == null) return;

            MissionData data = _currentMission.Data;
            _currentMission.Status = MissionStatus.Failed;
            _missionStatuses[data.MissionId] = MissionStatus.Available; // Can retry

            EventBus.Publish(new MissionCompletedEvent
            {
                MissionId = data.MissionId,
                Success = false,
                MoneyReward = 0
            });

            OnMissionEnded?.Invoke(data, false);

            Debug.Log($"[MissionSystem] Failed mission: {data.MissionName}. Reason: {reason}");
            _currentMission = null;
        }

        // ====================================================================
        // OBJECTIVES
        // ====================================================================

        /// <summary>
        /// Report progress on the current objective.
        /// </summary>
        public void ReportObjectiveProgress(string objectiveId, int amount = 1)
        {
            if (_currentMission == null) return;

            ObjectiveData currentObj = _currentMission.CurrentObjective;
            if (currentObj == null || currentObj.ObjectiveId != objectiveId) return;

            if (!_currentMission.ObjectiveProgress.ContainsKey(objectiveId))
            {
                _currentMission.ObjectiveProgress[objectiveId] = 0;
            }

            _currentMission.ObjectiveProgress[objectiveId] += amount;
            OnObjectiveUpdated?.Invoke(currentObj);
        }

        private void CheckObjectiveCompletion()
        {
            ObjectiveData currentObj = _currentMission.CurrentObjective;
            if (currentObj == null)
            {
                CompleteMission();
                return;
            }

            bool completed = false;

            switch (currentObj.Type)
            {
                case ObjectiveType.GoToLocation:
                case ObjectiveType.DriveToLocation:
                    float dist = Vector3.Distance(GetPlayerPosition(), currentObj.TargetLocation);
                    completed = dist <= currentObj.TargetRadius;
                    break;

                case ObjectiveType.KillTarget:
                case ObjectiveType.KillCount:
                case ObjectiveType.CollectItem:
                case ObjectiveType.DestroyTarget:
                    int progress = 0;
                    _currentMission.ObjectiveProgress.TryGetValue(currentObj.ObjectiveId, out progress);
                    completed = progress >= currentObj.TargetCount;
                    break;

                case ObjectiveType.SurviveTime:
                    completed = _currentMission.ElapsedTime >= currentObj.TimeLimit;
                    break;

                case ObjectiveType.LoseWantedLevel:
                    completed = Wanted.WantedSystem.Instance != null && Wanted.WantedSystem.Instance.CurrentWantedLevel == 0;
                    break;

                case ObjectiveType.TalkToNPC:
                    _currentMission.ObjectiveProgress.TryGetValue(currentObj.ObjectiveId, out int talkProgress);
                    completed = talkProgress > 0;
                    break;
            }

            if (completed)
            {
                AdvanceObjective();
            }
        }

        private void AdvanceObjective()
        {
            ObjectiveData completedObj = _currentMission.CurrentObjective;

            OnObjectiveCompleted?.Invoke(completedObj);

            _currentMission.CurrentObjectiveIndex++;

            // Save checkpoint
            _currentMission.CheckpointPosition = GetPlayerPosition();
            _currentMission.CheckpointObjectiveIndex = _currentMission.CurrentObjectiveIndex;

            if (_currentMission.CurrentObjectiveIndex >= _currentMission.Data.Objectives.Length)
            {
                CompleteMission();
            }
            else
            {
                OnObjectiveUpdated?.Invoke(_currentMission.CurrentObjective);
            }
        }

        // ====================================================================
        // HELPERS
        // ====================================================================

        private bool CanStartMission(MissionData data)
        {
            if (_missionStatuses.ContainsKey(data.MissionId) && _missionStatuses[data.MissionId] == MissionStatus.Completed)
                return false;

            // Check prerequisite missions
            if (data.RequiredCompletedMissions != null)
            {
                foreach (string reqId in data.RequiredCompletedMissions)
                {
                    if (!_completedMissions.Contains(reqId)) return false;
                }
            }

            // Check money requirement
            if (data.RequiredMoney > 0 && Economy.EconomySystem.Instance != null)
            {
                if (!Economy.EconomySystem.Instance.CanAfford(data.RequiredMoney)) return false;
            }

            return true;
        }

        private void UnlockMission(string missionId)
        {
            if (_missionStatuses.ContainsKey(missionId))
            {
                _missionStatuses[missionId] = MissionStatus.Available;
            }
        }

        private void RefreshMissionAvailability()
        {
            foreach (MissionData mission in _allMissions)
            {
                if (_missionStatuses[mission.MissionId] == MissionStatus.Locked)
                {
                    if (CanStartMission(mission))
                    {
                        _missionStatuses[mission.MissionId] = MissionStatus.Available;
                    }
                }
            }
        }

        private Vector3 GetPlayerPosition()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            return player != null ? player.transform.position : Vector3.zero;
        }

        public MissionStatus GetMissionStatus(string missionId)
        {
            return _missionStatuses.ContainsKey(missionId) ? _missionStatuses[missionId] : MissionStatus.Locked;
        }

        public List<MissionData> GetAvailableMissions()
        {
            List<MissionData> available = new List<MissionData>();
            foreach (MissionData mission in _allMissions)
            {
                if (_missionStatuses[mission.MissionId] == MissionStatus.Available)
                {
                    available.Add(mission);
                }
            }
            return available;
        }

        public bool IsMissionCompleted(string missionId)
        {
            return _completedMissions.Contains(missionId);
        }
    }
}
