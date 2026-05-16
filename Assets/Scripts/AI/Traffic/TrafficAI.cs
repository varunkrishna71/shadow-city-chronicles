// ============================================================================
// TrafficAI.cs — Vehicle traffic system for city streets
// ============================================================================
// PURPOSE:
//   Creates realistic traffic flow throughout the city. AI vehicles follow
//   roads, obey traffic lights, react to the player, and avoid collisions.
//
// ARCHITECTURE:
//   Traffic uses a SPLINE-BASED system:
//   1. Roads are defined as splines (curves) in the editor
//   2. Traffic vehicles follow these splines
//   3. At intersections, vehicles choose paths based on weighted randomness
//   4. Traffic density varies by time of day and district
//
//   This is FAR more efficient than full NavMesh pathfinding for vehicles.
//
// MOBILE OPTIMIZATION:
//   - Vehicles beyond 150m are completely frozen
//   - Vehicles beyond 80m use simplified physics (no WheelColliders)
//   - Maximum 20 active traffic vehicles at any time
//   - Vehicles are pooled and recycled when they leave player range
//   - Collision avoidance uses simple raycasts, not physics overlap
// ============================================================================

using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.AI.Traffic
{
    public enum TrafficState
    {
        Driving,        // Normal driving along spline
        Stopped,        // At red light or stop sign
        Yielding,       // Slowing for pedestrian or intersection
        Avoiding,       // Swerving to avoid obstacle
        Panicking,      // Fleeing from danger (gunfire, explosions)
        Crashed         // Vehicle has crashed, driver may exit
    }

    public class TrafficAI : MonoBehaviour
    {
        [Header("Driving")]
        [SerializeField] private float _maxSpeed = 50f;        // km/h
        [SerializeField] private float _acceleration = 8f;
        [SerializeField] private float _brakeForce = 15f;
        [SerializeField] private float _steerSpeed = 3f;

        [Header("Safety")]
        [SerializeField] private float _followDistance = 8f;    // Min distance to car ahead
        [SerializeField] private float _safetyRayLength = 15f;
        [SerializeField] private LayerMask _obstacleLayer;

        [Header("Panic")]
        [SerializeField] private float _panicSpeedMultiplier = 1.5f;
        [SerializeField] private float _panicDuration = 10f;

        [Header("LOD")]
        [SerializeField] private float _fullPhysicsRange = 80f;
        [SerializeField] private float _freezeRange = 150f;

        // Spline following
        private Vector3[] _currentPath;
        private int _currentPathIndex;
        private float _pathProgress;

        // State
        private TrafficState _state = TrafficState.Driving;
        private Rigidbody _rigidbody;
        private float _currentSpeed;
        private float _targetSpeed;
        private float _panicTimer;
        private bool _isFrozen;

        // LOD
        private Transform _playerTransform;
        private float _distanceToPlayer;
        private int _lodLevel; // 0=full, 1=simple, 2=frozen

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        private void Start()
        {
            _targetSpeed = _maxSpeed * Random.Range(0.8f, 1.0f);
        }

        private void Update()
        {
            UpdateLOD();

            if (_isFrozen) return;

            switch (_state)
            {
                case TrafficState.Driving:
                    UpdateDriving();
                    break;
                case TrafficState.Stopped:
                    UpdateStopped();
                    break;
                case TrafficState.Yielding:
                    UpdateYielding();
                    break;
                case TrafficState.Avoiding:
                    UpdateAvoiding();
                    break;
                case TrafficState.Panicking:
                    UpdatePanicking();
                    break;
            }
        }

        // ====================================================================
        // LOD MANAGEMENT
        // ====================================================================

        private void UpdateLOD()
        {
            if (_playerTransform == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) _playerTransform = player.transform;
                else return;
            }

            _distanceToPlayer = Vector3.Distance(transform.position, _playerTransform.position);

            if (_distanceToPlayer > _freezeRange)
            {
                if (!_isFrozen)
                {
                    _isFrozen = true;
                    if (_rigidbody != null)
                    {
                        _rigidbody.isKinematic = true;
                    }
                }
                _lodLevel = 2;
            }
            else if (_distanceToPlayer > _fullPhysicsRange)
            {
                _isFrozen = false;
                _lodLevel = 1;
                if (_rigidbody != null)
                {
                    _rigidbody.isKinematic = true; // Simple movement, no physics
                }
            }
            else
            {
                _isFrozen = false;
                _lodLevel = 0;
                if (_rigidbody != null)
                {
                    _rigidbody.isKinematic = false; // Full physics
                }
            }
        }

        // ====================================================================
        // DRIVING
        // ====================================================================

        private void UpdateDriving()
        {
            if (_currentPath == null || _currentPath.Length == 0) return;

            // Get target point on path
            Vector3 targetPoint = _currentPath[_currentPathIndex];
            Vector3 toTarget = targetPoint - transform.position;
            toTarget.y = 0;

            // Check if reached current waypoint
            if (toTarget.magnitude < 2f)
            {
                _currentPathIndex++;
                if (_currentPathIndex >= _currentPath.Length)
                {
                    // End of path — recycle this vehicle
                    OnPathComplete();
                    return;
                }
            }

            // Steer toward target
            Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, _steerSpeed * Time.deltaTime);

            // Check for obstacles ahead
            float adjustedSpeed = CheckForObstacles();

            // Accelerate/brake
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, adjustedSpeed, _acceleration * Time.deltaTime);

            // Move
            if (_lodLevel == 0 && _rigidbody != null && !_rigidbody.isKinematic)
            {
                _rigidbody.linearVelocity = transform.forward * (_currentSpeed / 3.6f); // km/h to m/s
            }
            else
            {
                transform.position += transform.forward * (_currentSpeed / 3.6f) * Time.deltaTime;
            }
        }

        /// <summary>
        /// Raycast ahead to detect obstacles and adjust speed.
        /// </summary>
        private float CheckForObstacles()
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;

            if (Physics.Raycast(origin, transform.forward, out RaycastHit hit, _safetyRayLength, _obstacleLayer))
            {
                float distance = hit.distance;

                if (distance < _followDistance * 0.3f)
                {
                    // Very close — full stop
                    return 0f;
                }
                else if (distance < _followDistance)
                {
                    // Slow down proportionally
                    float slowFactor = distance / _followDistance;
                    return _targetSpeed * slowFactor;
                }
            }

            return _targetSpeed;
        }

        private void UpdateStopped()
        {
            _currentSpeed = 0f;

            if (_rigidbody != null && !_rigidbody.isKinematic)
            {
                _rigidbody.linearVelocity = Vector3.zero;
            }
        }

        private void UpdateYielding()
        {
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, _targetSpeed * 0.3f, _brakeForce * Time.deltaTime);
        }

        private void UpdateAvoiding()
        {
            // Swerve logic — shift laterally while maintaining forward progress
            UpdateDriving();
        }

        private void UpdatePanicking()
        {
            _panicTimer -= Time.deltaTime;

            if (_panicTimer <= 0)
            {
                _state = TrafficState.Driving;
                return;
            }

            // Drive fast and erratically
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, _maxSpeed * _panicSpeedMultiplier, _acceleration * 2f * Time.deltaTime);

            if (_currentPath != null && _currentPathIndex < _currentPath.Length)
            {
                Vector3 target = _currentPath[_currentPathIndex];
                Vector3 toTarget = (target - transform.position).normalized;
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(toTarget),
                    _steerSpeed * 0.5f * Time.deltaTime
                );
            }

            transform.position += transform.forward * (_currentSpeed / 3.6f) * Time.deltaTime;
        }

        // ====================================================================
        // PUBLIC API
        // ====================================================================

        /// <summary>
        /// Set the path for this traffic vehicle to follow.
        /// Called by TrafficManager when spawning/recycling vehicles.
        /// </summary>
        public void SetPath(Vector3[] path)
        {
            _currentPath = path;
            _currentPathIndex = 0;
            _state = TrafficState.Driving;
        }

        public void StopAtLight()
        {
            _state = TrafficState.Stopped;
        }

        public void GoOnGreen()
        {
            _state = TrafficState.Driving;
        }

        public void Panic()
        {
            _state = TrafficState.Panicking;
            _panicTimer = _panicDuration;
        }

        private void OnPathComplete()
        {
            // Return to traffic pool for recycling
            gameObject.SetActive(false);
        }
    }
}
