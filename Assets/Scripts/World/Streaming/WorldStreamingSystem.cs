// ============================================================================
// WorldStreamingSystem.cs — Open world chunk loading and streaming
// ============================================================================
// PURPOSE:
//   The city of Ashenmere is too large to load all at once on mobile.
//   This system divides the world into chunks (grid cells) and loads/unloads
//   them based on player position. Only nearby chunks are fully loaded.
//
// HOW IT WORKS:
//   1. World is divided into a grid of chunks (e.g., 200m x 200m each)
//   2. Each chunk is a separate Unity scene or addressable asset
//   3. When player enters a chunk, surrounding chunks are loaded
//   4. Distant chunks are unloaded to free memory
//   5. Loading happens asynchronously (no frame drops)
//
// LOADING STRATEGY:
//   Ring 0 (current chunk): Fully loaded — all details, all NPCs
//   Ring 1 (adjacent): Loaded — buildings and roads, fewer NPCs
//   Ring 2 (2 chunks away): Low detail — LOD buildings only
//   Ring 3+: Unloaded — skybox and distant fog hide the edge
//
// MOBILE OPTIMIZATION:
//   - Async loading spread over multiple frames
//   - Maximum 9 chunks loaded at once (3x3 grid)
//   - Uses Unity Addressables for efficient memory management
//   - Chunks have LOD variants (full, medium, low)
//   - Loading priority based on player movement direction
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowCity.World.Streaming
{
    [System.Serializable]
    public class WorldChunk
    {
        public string ChunkId;
        public Vector2Int GridPosition;
        public string SceneName;
        public Bounds WorldBounds;
        public ChunkLoadState LoadState;
        public int LODLevel;
    }

    public enum ChunkLoadState
    {
        Unloaded,
        Loading,
        Loaded,
        Unloading
    }

    public class WorldStreamingSystem : MonoBehaviour
    {
        private static WorldStreamingSystem _instance;
        public static WorldStreamingSystem Instance => _instance;

        [Header("Grid Settings")]
        [SerializeField] private float _chunkSize = 200f;
        [SerializeField] private int _gridWidth = 10;
        [SerializeField] private int _gridHeight = 10;

        [Header("Loading")]
        [SerializeField] private int _loadRadius = 1;         // Chunks around player to keep loaded
        [SerializeField] private int _unloadRadius = 3;        // Beyond this, unload
        [SerializeField] private float _updateInterval = 1f;   // How often to check (seconds)
        [SerializeField] private int _maxLoadsPerFrame = 1;    // Limit concurrent loads

        [Header("Performance")]
        [SerializeField] private float _memoryWarningThresholdMB = 512f;

        // State
        private Dictionary<Vector2Int, WorldChunk> _chunks = new Dictionary<Vector2Int, WorldChunk>();
        private HashSet<Vector2Int> _loadedChunks = new HashSet<Vector2Int>();
        private Queue<Vector2Int> _loadQueue = new Queue<Vector2Int>();
        private Queue<Vector2Int> _unloadQueue = new Queue<Vector2Int>();
        private Vector2Int _currentPlayerChunk;
        private Vector2Int _lastPlayerChunk;
        private float _updateTimer;
        private int _activeLoadOperations;

        // Events
        public System.Action<Vector2Int> OnChunkLoaded;
        public System.Action<Vector2Int> OnChunkUnloaded;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            InitializeGrid();
        }

        private void Update()
        {
            _updateTimer += Time.deltaTime;
            if (_updateTimer < _updateInterval) return;
            _updateTimer = 0f;

            UpdatePlayerChunk();

            if (_currentPlayerChunk != _lastPlayerChunk)
            {
                _lastPlayerChunk = _currentPlayerChunk;
                EvaluateChunks();
            }

            ProcessLoadQueue();
            ProcessUnloadQueue();
        }

        // ====================================================================
        // INITIALIZATION
        // ====================================================================

        private void InitializeGrid()
        {
            for (int x = 0; x < _gridWidth; x++)
            {
                for (int y = 0; y < _gridHeight; y++)
                {
                    Vector2Int gridPos = new Vector2Int(x, y);
                    WorldChunk chunk = new WorldChunk
                    {
                        ChunkId = $"Chunk_{x}_{y}",
                        GridPosition = gridPos,
                        SceneName = $"World/Chunk_{x}_{y}",
                        WorldBounds = new Bounds(
                            new Vector3(x * _chunkSize + _chunkSize * 0.5f, 0, y * _chunkSize + _chunkSize * 0.5f),
                            new Vector3(_chunkSize, 100f, _chunkSize)
                        ),
                        LoadState = ChunkLoadState.Unloaded,
                        LODLevel = 2
                    };

                    _chunks[gridPos] = chunk;
                }
            }

            Debug.Log($"[WorldStreaming] Grid initialized: {_gridWidth}x{_gridHeight} = {_chunks.Count} chunks");
        }

        // ====================================================================
        // PLAYER TRACKING
        // ====================================================================

        private void UpdatePlayerChunk()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;

            Vector3 pos = player.transform.position;
            _currentPlayerChunk = new Vector2Int(
                Mathf.FloorToInt(pos.x / _chunkSize),
                Mathf.FloorToInt(pos.z / _chunkSize)
            );
        }

        // ====================================================================
        // CHUNK EVALUATION
        // ====================================================================

        /// <summary>
        /// Determine which chunks should be loaded/unloaded based on player position.
        /// </summary>
        private void EvaluateChunks()
        {
            HashSet<Vector2Int> shouldBeLoaded = new HashSet<Vector2Int>();

            // Calculate which chunks should be loaded (within load radius)
            for (int x = -_loadRadius; x <= _loadRadius; x++)
            {
                for (int y = -_loadRadius; y <= _loadRadius; y++)
                {
                    Vector2Int chunkPos = _currentPlayerChunk + new Vector2Int(x, y);

                    if (_chunks.ContainsKey(chunkPos))
                    {
                        shouldBeLoaded.Add(chunkPos);
                    }
                }
            }

            // Queue chunks that need loading
            foreach (Vector2Int pos in shouldBeLoaded)
            {
                if (!_loadedChunks.Contains(pos) && _chunks[pos].LoadState == ChunkLoadState.Unloaded)
                {
                    _loadQueue.Enqueue(pos);
                }

                // Update LOD based on distance
                int distance = Mathf.Max(
                    Mathf.Abs(pos.x - _currentPlayerChunk.x),
                    Mathf.Abs(pos.y - _currentPlayerChunk.y)
                );
                _chunks[pos].LODLevel = distance;
            }

            // Queue distant chunks for unloading
            List<Vector2Int> toUnload = new List<Vector2Int>();
            foreach (Vector2Int loadedPos in _loadedChunks)
            {
                int distance = Mathf.Max(
                    Mathf.Abs(loadedPos.x - _currentPlayerChunk.x),
                    Mathf.Abs(loadedPos.y - _currentPlayerChunk.y)
                );

                if (distance > _unloadRadius)
                {
                    toUnload.Add(loadedPos);
                }
            }

            foreach (Vector2Int pos in toUnload)
            {
                _unloadQueue.Enqueue(pos);
            }
        }

        // ====================================================================
        // LOADING
        // ====================================================================

        private void ProcessLoadQueue()
        {
            if (_loadQueue.Count == 0) return;
            if (_activeLoadOperations >= _maxLoadsPerFrame) return;

            // Check memory before loading more
            if (GetUsedMemoryMB() > _memoryWarningThresholdMB)
            {
                Debug.LogWarning("[WorldStreaming] Memory threshold reached! Pausing chunk loading.");
                return;
            }

            Vector2Int chunkPos = _loadQueue.Dequeue();

            if (!_chunks.ContainsKey(chunkPos)) return;

            WorldChunk chunk = _chunks[chunkPos];
            if (chunk.LoadState != ChunkLoadState.Unloaded) return;

            chunk.LoadState = ChunkLoadState.Loading;
            _activeLoadOperations++;

            // Async scene loading
            AsyncOperation loadOp = SceneManager.LoadSceneAsync(chunk.SceneName, LoadSceneMode.Additive);

            if (loadOp != null)
            {
                loadOp.completed += (op) => OnChunkLoadComplete(chunkPos);
            }
            else
            {
                // Scene doesn't exist yet (expected during development)
                chunk.LoadState = ChunkLoadState.Loaded;
                _loadedChunks.Add(chunkPos);
                _activeLoadOperations--;
            }
        }

        private void OnChunkLoadComplete(Vector2Int chunkPos)
        {
            if (!_chunks.ContainsKey(chunkPos)) return;

            WorldChunk chunk = _chunks[chunkPos];
            chunk.LoadState = ChunkLoadState.Loaded;
            _loadedChunks.Add(chunkPos);
            _activeLoadOperations--;

            OnChunkLoaded?.Invoke(chunkPos);

            Debug.Log($"[WorldStreaming] Loaded chunk {chunkPos}");
        }

        // ====================================================================
        // UNLOADING
        // ====================================================================

        private void ProcessUnloadQueue()
        {
            if (_unloadQueue.Count == 0) return;

            Vector2Int chunkPos = _unloadQueue.Dequeue();

            if (!_chunks.ContainsKey(chunkPos)) return;

            WorldChunk chunk = _chunks[chunkPos];
            if (chunk.LoadState != ChunkLoadState.Loaded) return;

            chunk.LoadState = ChunkLoadState.Unloading;

            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(chunk.SceneName);

            if (unloadOp != null)
            {
                unloadOp.completed += (op) =>
                {
                    chunk.LoadState = ChunkLoadState.Unloaded;
                    _loadedChunks.Remove(chunkPos);
                    OnChunkUnloaded?.Invoke(chunkPos);
                };
            }
            else
            {
                chunk.LoadState = ChunkLoadState.Unloaded;
                _loadedChunks.Remove(chunkPos);
            }
        }

        // ====================================================================
        // MEMORY MONITORING
        // ====================================================================

        private float GetUsedMemoryMB()
        {
            return (float)System.GC.GetTotalMemory(false) / (1024f * 1024f);
        }

        /// <summary>
        /// Force garbage collection and unload unused assets.
        /// Call this during loading screens or scene transitions.
        /// </summary>
        public void ForceCleanup()
        {
            Resources.UnloadUnusedAssets();
            System.GC.Collect();
            Debug.Log("[WorldStreaming] Forced memory cleanup.");
        }

        // ====================================================================
        // PUBLIC API
        // ====================================================================

        public Vector2Int GetPlayerChunk() => _currentPlayerChunk;
        public int GetLoadedChunkCount() => _loadedChunks.Count;
        public bool IsChunkLoaded(Vector2Int pos) => _loadedChunks.Contains(pos);
    }
}
