// ============================================================================
// ObjectPool.cs — Reusable object pooling system
// ============================================================================
// PURPOSE:
//   Instantiating and destroying objects (bullets, effects, NPCs) every frame
//   causes MASSIVE garbage collection spikes on mobile. Object pooling creates
//   objects once, then reuses them by activating/deactivating.
//
// BEGINNER NOTE:
//   Imagine a restaurant. Instead of buying new plates for every customer and
//   throwing them away after, you wash and reuse them. Object pooling is the
//   same concept — reuse game objects instead of creating/destroying them.
//
// MOBILE OPTIMIZATION:
//   This is THE most important optimization for mobile. Without pooling,
//   a shooting game would stutter every time GC runs (every few seconds).
//   With pooling, zero allocations during gameplay = smooth 30+ FPS.
//
// USAGE:
//   // Get a bullet from the pool
//   GameObject bullet = ObjectPool.Instance.Get("Bullet");
//   bullet.transform.position = firePoint.position;
//
//   // Return it when done (instead of Destroy)
//   ObjectPool.Instance.Return("Bullet", bullet);
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace ShadowCity.Core
{
    [System.Serializable]
    public class PoolConfig
    {
        public string PoolId;
        public GameObject Prefab;
        public int InitialSize = 10;
        public int MaxSize = 50;
        public bool AutoExpand = true;
    }

    public class ObjectPool : MonoBehaviour
    {
        private static ObjectPool _instance;
        public static ObjectPool Instance => _instance;

        [SerializeField] private List<PoolConfig> _poolConfigs = new List<PoolConfig>();

        // Each pool is a queue (FIFO) of inactive objects ready for reuse
        private Dictionary<string, Queue<GameObject>> _pools
            = new Dictionary<string, Queue<GameObject>>();

        // Track active objects so we can return them to the correct pool
        private Dictionary<string, List<GameObject>> _activeObjects
            = new Dictionary<string, List<GameObject>>();

        // Cache configs for runtime access
        private Dictionary<string, PoolConfig> _configLookup
            = new Dictionary<string, PoolConfig>();

        // Parent transforms to keep hierarchy clean
        private Dictionary<string, Transform> _poolParents
            = new Dictionary<string, Transform>();

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            InitializePools();
        }

        private void InitializePools()
        {
            foreach (PoolConfig config in _poolConfigs)
            {
                CreatePool(config);
            }
        }

        /// <summary>
        /// Creates a new pool. Can be called at runtime for dynamic pools.
        /// </summary>
        public void CreatePool(PoolConfig config)
        {
            if (_pools.ContainsKey(config.PoolId))
            {
                Debug.LogWarning($"[ObjectPool] Pool '{config.PoolId}' already exists.");
                return;
            }

            // Create parent object for organization
            GameObject parent = new GameObject($"Pool_{config.PoolId}");
            parent.transform.SetParent(transform);
            _poolParents[config.PoolId] = parent.transform;

            _pools[config.PoolId] = new Queue<GameObject>();
            _activeObjects[config.PoolId] = new List<GameObject>();
            _configLookup[config.PoolId] = config;

            // Pre-instantiate objects
            for (int i = 0; i < config.InitialSize; i++)
            {
                GameObject obj = CreateNewObject(config.PoolId);
                obj.SetActive(false);
                _pools[config.PoolId].Enqueue(obj);
            }

            Debug.Log($"[ObjectPool] Created pool '{config.PoolId}' with {config.InitialSize} objects.");
        }

        /// <summary>
        /// Get an object from the pool. Returns null if pool is exhausted
        /// and auto-expand is disabled.
        /// </summary>
        public GameObject Get(string poolId)
        {
            if (!_pools.ContainsKey(poolId))
            {
                Debug.LogError($"[ObjectPool] Pool '{poolId}' does not exist!");
                return null;
            }

            GameObject obj;

            if (_pools[poolId].Count > 0)
            {
                obj = _pools[poolId].Dequeue();
            }
            else if (_configLookup[poolId].AutoExpand
                     && _activeObjects[poolId].Count < _configLookup[poolId].MaxSize)
            {
                obj = CreateNewObject(poolId);
            }
            else
            {
                Debug.LogWarning($"[ObjectPool] Pool '{poolId}' exhausted!");
                return null;
            }

            obj.SetActive(true);
            _activeObjects[poolId].Add(obj);
            return obj;
        }

        /// <summary>
        /// Return an object to the pool instead of destroying it.
        /// </summary>
        public void Return(string poolId, GameObject obj)
        {
            if (!_pools.ContainsKey(poolId))
            {
                Debug.LogError($"[ObjectPool] Pool '{poolId}' does not exist!");
                Destroy(obj);
                return;
            }

            obj.SetActive(false);
            obj.transform.SetParent(_poolParents[poolId]);
            _activeObjects[poolId].Remove(obj);
            _pools[poolId].Enqueue(obj);
        }

        /// <summary>
        /// Return ALL active objects in a pool. Useful for scene transitions.
        /// </summary>
        public void ReturnAll(string poolId)
        {
            if (!_activeObjects.ContainsKey(poolId)) return;

            // Iterate backwards since we're modifying the list
            for (int i = _activeObjects[poolId].Count - 1; i >= 0; i--)
            {
                Return(poolId, _activeObjects[poolId][i]);
            }
        }

        private GameObject CreateNewObject(string poolId)
        {
            PoolConfig config = _configLookup[poolId];
            GameObject obj = Instantiate(config.Prefab, _poolParents[poolId]);
            obj.name = $"{config.PoolId}_{_activeObjects[poolId].Count + _pools[poolId].Count}";
            return obj;
        }

        /// <summary>
        /// Get pool statistics for debugging.
        /// </summary>
        public string GetPoolStats()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Object Pool Stats ===");
            foreach (var kvp in _pools)
            {
                int available = kvp.Value.Count;
                int active = _activeObjects[kvp.Key].Count;
                sb.AppendLine($"  {kvp.Key}: {active} active, {available} available");
            }
            return sb.ToString();
        }
    }
}
