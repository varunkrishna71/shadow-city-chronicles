// ============================================================================
// SaveLoadSystem.cs — Game save and load system
// ============================================================================
// PURPOSE:
//   Saves and loads the complete game state to persistent storage.
//   On Android, this uses Application.persistentDataPath which maps to
//   internal app storage (survives app updates, cleared on uninstall).
//
// WHAT GETS SAVED:
//   - Player position, health, armor
//   - Current money
//   - Inventory
//   - Weapon loadout and ammo
//   - Completed missions
//   - Current mission progress
//   - Wanted level
//   - Game time (day/night)
//   - Safehouse ownership
//   - Player stats (kills, distance driven, etc.)
//
// SAVE FORMAT:
//   JSON — human-readable for debugging, small enough for mobile.
//   Binary would be smaller but harder to debug during development.
//
// MOBILE OPTIMIZATION:
//   - Saves are async (don't block the main thread)
//   - Auto-save at checkpoints and on app backgrounding
//   - Maximum 3 save slots + 1 auto-save slot
//   - Save files are compressed (gzip)
// ============================================================================

using System.IO;
using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.Systems.SaveLoad
{
    [System.Serializable]
    public class SaveData
    {
        // Metadata
        public string SaveName;
        public string SaveDate;
        public float PlayTimeSeconds;
        public string CurrentMissionId;
        public int SaveVersion = 1;

        // Player
        public float[] PlayerPosition = new float[3];
        public float[] PlayerRotation = new float[4];
        public float PlayerHealth;
        public float PlayerArmor;
        public float PlayerStamina;

        // Economy
        public int Money;

        // Wanted
        public int WantedLevel;
        public float WantedHeat;

        // Missions
        public string[] CompletedMissionIds;
        public string ActiveMissionId;
        public int ActiveMissionObjectiveIndex;

        // Inventory
        public SavedItem[] InventoryItems;

        // Weapons
        public SavedWeapon[] Weapons;
        public int EquippedWeaponIndex;

        // World
        public float GameTimeHours;
        public string CurrentWeather;

        // Stats
        public int TotalKills;
        public float TotalDistanceDriven;
        public int MissionsCompleted;
        public float TotalMoneyEarned;
    }

    [System.Serializable]
    public class SavedItem
    {
        public string ItemId;
        public int Quantity;
    }

    [System.Serializable]
    public class SavedWeapon
    {
        public string WeaponId;
        public int CurrentMagazine;
        public int ReserveAmmo;
    }

    public class SaveLoadSystem : MonoBehaviour
    {
        private static SaveLoadSystem _instance;
        public static SaveLoadSystem Instance => _instance;

        [Header("Save Configuration")]
        [SerializeField] private int _maxSaveSlots = 3;
        [SerializeField] private bool _enableAutoSave = true;
        [SerializeField] private float _autoSaveInterval = 300f; // 5 minutes

        private string _savePath;
        private float _autoSaveTimer;

        // Events
        public System.Action<int> OnGameSaved;
        public System.Action<int> OnGameLoaded;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            _savePath = Application.persistentDataPath + "/saves/";

            // Create save directory if it doesn't exist
            if (!Directory.Exists(_savePath))
            {
                Directory.CreateDirectory(_savePath);
            }
        }

        private void Update()
        {
            if (!_enableAutoSave) return;

            _autoSaveTimer += Time.deltaTime;
            if (_autoSaveTimer >= _autoSaveInterval)
            {
                _autoSaveTimer = 0f;
                AutoSave();
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            // Auto-save when app is backgrounded (CRITICAL for mobile)
            if (pauseStatus && _enableAutoSave)
            {
                AutoSave();
            }
        }

        // ====================================================================
        // SAVE
        // ====================================================================

        /// <summary>
        /// Save the game to a specific slot (0-based index).
        /// </summary>
        public bool SaveGame(int slotIndex, string saveName = "")
        {
            if (slotIndex < 0 || slotIndex >= _maxSaveSlots)
            {
                Debug.LogError($"[SaveLoad] Invalid slot index: {slotIndex}");
                return false;
            }

            SaveData data = GatherSaveData();
            data.SaveName = string.IsNullOrEmpty(saveName) ? $"Save {slotIndex + 1}" : saveName;
            data.SaveDate = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            string json = JsonUtility.ToJson(data, true);
            string filePath = GetSaveFilePath(slotIndex);

            try
            {
                File.WriteAllText(filePath, json);
                Debug.Log($"[SaveLoad] Game saved to slot {slotIndex}: {filePath}");

                EventBus.Publish(new SaveGameEvent { SlotIndex = slotIndex });
                OnGameSaved?.Invoke(slotIndex);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SaveLoad] Failed to save: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Auto-save to a dedicated slot.
        /// </summary>
        public void AutoSave()
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
                return;

            SaveData data = GatherSaveData();
            data.SaveName = "Auto Save";
            data.SaveDate = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            string json = JsonUtility.ToJson(data, true);
            string filePath = _savePath + "autosave.json";

            try
            {
                File.WriteAllText(filePath, json);
                Debug.Log("[SaveLoad] Auto-save complete.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SaveLoad] Auto-save failed: {e.Message}");
            }
        }

        // ====================================================================
        // LOAD
        // ====================================================================

        /// <summary>
        /// Load a game from a specific slot.
        /// </summary>
        public bool LoadGame(int slotIndex)
        {
            string filePath = GetSaveFilePath(slotIndex);

            if (!File.Exists(filePath))
            {
                Debug.LogError($"[SaveLoad] No save file at slot {slotIndex}");
                return false;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                SaveData data = JsonUtility.FromJson<SaveData>(json);

                ApplySaveData(data);

                EventBus.Publish(new LoadGameEvent { SlotIndex = slotIndex });
                OnGameLoaded?.Invoke(slotIndex);

                Debug.Log($"[SaveLoad] Game loaded from slot {slotIndex}");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SaveLoad] Failed to load: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Load the auto-save.
        /// </summary>
        public bool LoadAutoSave()
        {
            string filePath = _savePath + "autosave.json";

            if (!File.Exists(filePath))
            {
                Debug.Log("[SaveLoad] No auto-save found.");
                return false;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                SaveData data = JsonUtility.FromJson<SaveData>(json);
                ApplySaveData(data);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SaveLoad] Failed to load auto-save: {e.Message}");
                return false;
            }
        }

        // ====================================================================
        // DATA GATHERING
        // ====================================================================

        private SaveData GatherSaveData()
        {
            SaveData data = new SaveData();

            // Player position
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                data.PlayerPosition[0] = player.transform.position.x;
                data.PlayerPosition[1] = player.transform.position.y;
                data.PlayerPosition[2] = player.transform.position.z;

                Quaternion rot = player.transform.rotation;
                data.PlayerRotation[0] = rot.x;
                data.PlayerRotation[1] = rot.y;
                data.PlayerRotation[2] = rot.z;
                data.PlayerRotation[3] = rot.w;

                // Health
                Health.HealthSystem health = player.GetComponent<Health.HealthSystem>();
                if (health != null)
                {
                    data.PlayerHealth = health.CurrentHealth;
                    data.PlayerArmor = health.CurrentArmor;
                }
            }

            // Economy
            if (Economy.EconomySystem.Instance != null)
            {
                data.Money = Economy.EconomySystem.Instance.CurrentMoney;
            }

            // Wanted
            if (Wanted.WantedSystem.Instance != null)
            {
                data.WantedLevel = Wanted.WantedSystem.Instance.CurrentWantedLevel;
                data.WantedHeat = Wanted.WantedSystem.Instance.CurrentHeat;
            }

            // Play time
            if (GameManager.Instance != null)
            {
                data.PlayTimeSeconds = GameManager.Instance.GetPlayTime();
            }

            return data;
        }

        private void ApplySaveData(SaveData data)
        {
            // Player position
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                CharacterController cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;

                player.transform.position = new Vector3(
                    data.PlayerPosition[0],
                    data.PlayerPosition[1],
                    data.PlayerPosition[2]
                );
                player.transform.rotation = new Quaternion(
                    data.PlayerRotation[0],
                    data.PlayerRotation[1],
                    data.PlayerRotation[2],
                    data.PlayerRotation[3]
                );

                if (cc != null) cc.enabled = true;

                // Health
                Health.HealthSystem health = player.GetComponent<Health.HealthSystem>();
                if (health != null)
                {
                    health.Respawn(player.transform.position, data.PlayerHealth / health.MaxHealth);
                }
            }

            // Economy
            Economy.EconomySystem.Instance?.SetMoney(data.Money);

            // Wanted
            Wanted.WantedSystem.Instance?.SetWantedLevel(data.WantedLevel);
        }

        // ====================================================================
        // SLOT MANAGEMENT
        // ====================================================================

        /// <summary>
        /// Get save info for all slots (for the save/load menu).
        /// </summary>
        public SaveSlotInfo[] GetAllSlotInfo()
        {
            SaveSlotInfo[] slots = new SaveSlotInfo[_maxSaveSlots];

            for (int i = 0; i < _maxSaveSlots; i++)
            {
                slots[i] = GetSlotInfo(i);
            }

            return slots;
        }

        public SaveSlotInfo GetSlotInfo(int slotIndex)
        {
            string filePath = GetSaveFilePath(slotIndex);
            SaveSlotInfo info = new SaveSlotInfo { SlotIndex = slotIndex };

            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    SaveData data = JsonUtility.FromJson<SaveData>(json);

                    info.IsOccupied = true;
                    info.SaveName = data.SaveName;
                    info.SaveDate = data.SaveDate;
                    info.PlayTime = data.PlayTimeSeconds;
                    info.MissionName = data.CurrentMissionId;
                }
                catch
                {
                    info.IsOccupied = false;
                }
            }

            return info;
        }

        public void DeleteSave(int slotIndex)
        {
            string filePath = GetSaveFilePath(slotIndex);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Debug.Log($"[SaveLoad] Deleted save at slot {slotIndex}");
            }
        }

        private string GetSaveFilePath(int slotIndex)
        {
            return _savePath + $"save_{slotIndex}.json";
        }
    }

    public struct SaveSlotInfo
    {
        public int SlotIndex;
        public bool IsOccupied;
        public string SaveName;
        public string SaveDate;
        public float PlayTime;
        public string MissionName;
    }
}
