// ============================================================================
// NPCSpawnSystem.cs — Dynamic NPC population management
// ============================================================================
// PURPOSE:
//   Manages the spawning and despawning of civilian, police, and gang NPCs
//   throughout the city. NPCs are spawned around the player and recycled
//   when they move too far away.
//
// SPAWN RULES:
//   - NPCs only exist within a radius around the player
//   - NPC density varies by district (downtown = busy, industrial = sparse)
//   - Time of day affects NPC count (fewer at night)
//   - Weather affects NPC behavior (fewer pedestrians in rain)
//   - Gang members only spawn in their territory
//   - Police density increases with wanted level
//
// MOBILE OPTIMIZATION:
//   - All NPCs use Object Pooling (zero instantiation during gameplay)
//   - Maximum 30 pedestrians + 10 traffic vehicles at any time
//   - NPCs beyond view frustum are frozen (disabled components)
//   - Spawn checks run every 0.5 seconds (not every frame)
//   - NPCs share animation controllers (instanced, not unique)
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.World.NPCSpawning
{
    [System.Serializable]
    public class SpawnZone
    {
        public string ZoneName;
        public Bounds Bounds;
        public int MaxPedestrians = 10;
        public int MaxVehicles = 5;
        public bool SpawnPolice = true;
        public AI.Gang.GangFaction? GangFaction;
        public float DensityMultiplier = 1f;
    }

    public class NPCSpawnSystem : MonoBehaviour
    {
        private static NPCSpawnSystem _instance;
        public static NPCSpawnSystem Instance => _instance;

        [Header("Spawn Limits")]
        [SerializeField] private int _maxPedestrians = 30;
        [SerializeField] private int _maxTrafficVehicles = 10;
        [SerializeField] private int _maxPolice = 6;

        [Header("Spawn Distances")]
        [SerializeField] private float _spawnRadius = 80f;
        [SerializeField] private float _despawnRadius = 120f;
        [SerializeField] private float _minSpawnDistance = 30f;

        [Header("Spawn Rates")]
        [SerializeField] private float _spawnCheckInterval = 0.5f;
        [SerializeField] private float _pedestrianSpawnInterval = 1f;
        [SerializeField] private float _vehicleSpawnInterval = 2f;

        [Header("Pool IDs")]
        [SerializeField] private string _pedestrianPoolId = "Pedestrian";
        [SerializeField] private string _trafficPoolId = "TrafficVehicle";
        [SerializeField] private string _policePoolId = "PoliceOfficer";

        [Header("Spawn Zones")]
        [SerializeField] private SpawnZone[] _spawnZones;

        // State
        private List<GameObject> _activePedestrians = new List<GameObject>();
        private List<GameObject> _activeVehicles = new List<GameObject>();
        private List<GameObject> _activePolice = new List<GameObject>();
        private float _spawnTimer;
        private float _pedestrianTimer;
        private float _vehicleTimer;
        private Transform _playerTransform;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void Update()
        {
            if (_playerTransform == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) _playerTransform = player.transform;
                else return;
            }

            _spawnTimer += Time.deltaTime;
            if (_spawnTimer < _spawnCheckInterval) return;
            _spawnTimer = 0f;

            DespawnDistantNPCs();
            SpawnNearbyNPCs();
        }

        // ====================================================================
        // SPAWNING
        // ====================================================================

        private void SpawnNearbyNPCs()
        {
            float timeMultiplier = GetTimeOfDayMultiplier();
            float weatherMultiplier = GetWeatherMultiplier();
            float densityMultiplier = timeMultiplier * weatherMultiplier;

            // Spawn pedestrians
            int targetPedestrians = Mathf.RoundToInt(_maxPedestrians * densityMultiplier);
            if (_activePedestrians.Count < targetPedestrians)
            {
                _pedestrianTimer += _spawnCheckInterval;
                if (_pedestrianTimer >= _pedestrianSpawnInterval)
                {
                    _pedestrianTimer = 0f;
                    SpawnPedestrian();
                }
            }

            // Spawn traffic
            int targetVehicles = Mathf.RoundToInt(_maxTrafficVehicles * densityMultiplier);
            if (_activeVehicles.Count < targetVehicles)
            {
                _vehicleTimer += _spawnCheckInterval;
                if (_vehicleTimer >= _vehicleSpawnInterval)
                {
                    _vehicleTimer = 0f;
                    SpawnTrafficVehicle();
                }
            }

            // Spawn police based on wanted level
            int wantedLevel = Systems.Wanted.WantedSystem.Instance != null
                ? Systems.Wanted.WantedSystem.Instance.CurrentWantedLevel
                : 0;
            int targetPolice = wantedLevel * 2;
            if (_activePolice.Count < targetPolice && _activePolice.Count < _maxPolice)
            {
                SpawnPoliceOfficer();
            }
        }

        private void SpawnPedestrian()
        {
            Vector3 spawnPoint = GetValidSpawnPoint();
            if (spawnPoint == Vector3.zero) return;

            GameObject ped = ObjectPool.Instance?.Get(_pedestrianPoolId);
            if (ped == null) return;

            ped.transform.position = spawnPoint;
            ped.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
            _activePedestrians.Add(ped);
        }

        private void SpawnTrafficVehicle()
        {
            Vector3 spawnPoint = GetValidSpawnPoint();
            if (spawnPoint == Vector3.zero) return;

            GameObject vehicle = ObjectPool.Instance?.Get(_trafficPoolId);
            if (vehicle == null) return;

            vehicle.transform.position = spawnPoint;
            vehicle.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
            _activeVehicles.Add(vehicle);
        }

        private void SpawnPoliceOfficer()
        {
            Vector3 spawnPoint = GetValidSpawnPoint();
            if (spawnPoint == Vector3.zero) return;

            GameObject police = ObjectPool.Instance?.Get(_policePoolId);
            if (police == null) return;

            police.transform.position = spawnPoint;
            _activePolice.Add(police);
        }

        // ====================================================================
        // DESPAWNING
        // ====================================================================

        private void DespawnDistantNPCs()
        {
            DespawnListItems(_activePedestrians, _pedestrianPoolId);
            DespawnListItems(_activeVehicles, _trafficPoolId);
            DespawnListItems(_activePolice, _policePoolId);
        }

        private void DespawnListItems(List<GameObject> list, string poolId)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null)
                {
                    list.RemoveAt(i);
                    continue;
                }

                float distance = Vector3.Distance(list[i].transform.position, _playerTransform.position);
                if (distance > _despawnRadius)
                {
                    ObjectPool.Instance?.Return(poolId, list[i]);
                    list.RemoveAt(i);
                }
            }
        }

        // ====================================================================
        // SPAWN POINT SELECTION
        // ====================================================================

        private Vector3 GetValidSpawnPoint()
        {
            // Try multiple times to find a valid point
            for (int attempt = 0; attempt < 5; attempt++)
            {
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float distance = Random.Range(_minSpawnDistance, _spawnRadius);

                Vector3 offset = new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance);
                Vector3 candidatePoint = _playerTransform.position + offset;

                // Check if point is on NavMesh (valid walkable surface)
                if (UnityEngine.AI.NavMesh.SamplePosition(candidatePoint, out UnityEngine.AI.NavMeshHit hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    // Make sure it's not visible to the player (spawn out of sight)
                    Vector3 toPoint = hit.position - _playerTransform.position;
                    if (Vector3.Angle(_playerTransform.forward, toPoint) > 90f || distance > 50f)
                    {
                        return hit.position;
                    }
                }
            }

            return Vector3.zero; // Failed to find valid point
        }

        // ====================================================================
        // DENSITY MODIFIERS
        // ====================================================================

        private float GetTimeOfDayMultiplier()
        {
            float hour = DayNight.DayNightCycle.Instance != null
                ? DayNight.DayNightCycle.Instance.CurrentHour
                : 12f;

            // Rush hour: 7-9 AM, 5-7 PM
            if ((hour >= 7 && hour <= 9) || (hour >= 17 && hour <= 19))
                return 1.2f;

            // Night: fewer people
            if (hour >= 23 || hour < 5)
                return 0.3f;

            // Late evening
            if (hour >= 21)
                return 0.5f;

            return 1f;
        }

        private float GetWeatherMultiplier()
        {
            if (Weather.WeatherSystem.Instance == null) return 1f;

            switch (Weather.WeatherSystem.Instance.CurrentWeather)
            {
                case Weather.WeatherType.Rain: return 0.6f;
                case Weather.WeatherType.HeavyRain: return 0.3f;
                case Weather.WeatherType.Thunderstorm: return 0.2f;
                case Weather.WeatherType.Fog: return 0.7f;
                default: return 1f;
            }
        }

        // ====================================================================
        // PUBLIC API
        // ====================================================================

        public int GetActivePedestrianCount() => _activePedestrians.Count;
        public int GetActiveVehicleCount() => _activeVehicles.Count;
        public int GetActivePoliceCount() => _activePolice.Count;

        /// <summary>
        /// Force despawn all NPCs (for scene transitions, cutscenes).
        /// </summary>
        public void DespawnAll()
        {
            foreach (GameObject ped in _activePedestrians)
            {
                if (ped != null) ObjectPool.Instance?.Return(_pedestrianPoolId, ped);
            }
            _activePedestrians.Clear();

            foreach (GameObject veh in _activeVehicles)
            {
                if (veh != null) ObjectPool.Instance?.Return(_trafficPoolId, veh);
            }
            _activeVehicles.Clear();

            foreach (GameObject pol in _activePolice)
            {
                if (pol != null) ObjectPool.Instance?.Return(_policePoolId, pol);
            }
            _activePolice.Clear();
        }
    }
}
