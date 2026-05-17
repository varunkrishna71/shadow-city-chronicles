// ============================================================================
// ThirdPersonCamera.cs — GTA-style third-person camera system
// ============================================================================
// PURPOSE:
//   Provides a smooth, responsive third-person camera that:
//   - Follows the player with adjustable distance and angle
//   - Smoothly orbits around the player based on touch/stick input
//   - Handles collision with walls (pulls camera closer to avoid clipping)
//   - Transitions between exploration, aiming, driving, and cinematic modes
//
// CAMERA MODES:
//   1. Exploration — Standard follow cam, player controls orbit
//   2. Aiming — Over-the-shoulder, tighter FOV, crosshair appears
//   3. Driving — Higher angle, wider FOV, follows vehicle velocity
//   4. Cinematic — Scripted camera for cutscenes
//   5. Cover — Offset to show what's ahead while in cover
//
// MOBILE OPTIMIZATION:
//   - Collision detection uses a single SphereCast (not multiple raycasts)
//   - Camera shake uses Perlin noise (no new object allocations)
//   - Smooth damp uses cached velocity (no allocation)
//
// BEGINNER NOTE:
//   The camera is the player's "eyes." If the camera feels bad, the entire
//   game feels bad. Spend MORE time tuning camera feel than almost anything.
//   Test on actual mobile devices — what feels good on PC often feels terrible
//   on a phone.
// ============================================================================

using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.Camera
{
    public enum CameraMode
    {
        Exploration,
        Aiming,
        Driving,
        Cinematic,
        Cover
    }

    public class ThirdPersonCamera : MonoBehaviour
    {
        // ====================================================================
        // TARGET
        // ====================================================================

        [Header("Target")]
        [SerializeField] private Transform _target;
        [SerializeField] private Vector3 _targetOffset = new Vector3(0, 1.5f, 0); // Look at chest, not feet

        // ====================================================================
        // EXPLORATION MODE SETTINGS
        // ====================================================================

        [Header("Exploration Camera")]
        [SerializeField] private float _defaultDistance = 4f;
        [SerializeField] private float _defaultHeight = 1.5f;
        [SerializeField] private float _orbitSensitivity = 3f;
        [SerializeField] private float _minVerticalAngle = -30f;
        [SerializeField] private float _maxVerticalAngle = 60f;

        // ====================================================================
        // AIM MODE SETTINGS
        // ====================================================================

        [Header("Aim Camera")]
        [SerializeField] private float _aimDistance = 1.5f;
        [SerializeField] private float _aimHeight = 1.6f;
        [SerializeField] private Vector3 _aimOffset = new Vector3(0.5f, 0, 0); // Offset right for over-shoulder
        [SerializeField] private float _aimFOV = 45f;
        [SerializeField] private float _aimSensitivity = 2f;

        // ====================================================================
        // DRIVING MODE SETTINGS
        // ====================================================================

        [Header("Driving Camera")]
        [SerializeField] private float _driveDistance = 8f;
        [SerializeField] private float _driveHeight = 3f;
        [SerializeField] private float _driveFOV = 70f;
        [SerializeField] private float _driveFollowSpeed = 3f;

        // ====================================================================
        // COLLISION
        // ====================================================================

        [Header("Collision")]
        [SerializeField] private float _collisionRadius = 0.3f;
        [SerializeField] private LayerMask _collisionLayers;
        [SerializeField] private float _collisionSnapSpeed = 10f;
        [SerializeField] private float _collisionRecoverSpeed = 3f;

        // ====================================================================
        // SMOOTHING
        // ====================================================================

        [Header("Smoothing")]
        [SerializeField] private float _positionSmoothTime = 0.1f;
        [SerializeField] private float _rotationSmoothTime = 0.05f;
        [SerializeField] private float _fovSmoothTime = 0.3f;
        [SerializeField] private float _modeTransitionSpeed = 5f;

        // ====================================================================
        // CAMERA SHAKE
        // ====================================================================

        [Header("Camera Shake")]
        [SerializeField] private float _shakeDecayRate = 5f;

        // ====================================================================
        // RUNTIME STATE
        // ====================================================================

        private CameraMode _currentMode = CameraMode.Exploration;
        private UnityEngine.Camera _camera;

        // Orbit angles
        private float _horizontalAngle;
        private float _verticalAngle = 15f;

        // Current interpolated values
        private float _currentDistance;
        private float _currentHeight;
        private float _currentFOV;

        // Smoothing velocities (used by SmoothDamp)
        private Vector3 _positionVelocity;
        private float _fovVelocity;

        // Collision
        private float _collisionDistance;

        // Shake
        private float _shakeIntensity;
        private float _shakeFrequency;

        // Input
        private Vector2 _lookInput;

        // ====================================================================
        // INITIALIZATION
        // ====================================================================

        private void Awake()
        {
            _camera = GetComponent<UnityEngine.Camera>();
            _currentDistance = _defaultDistance;
            _currentHeight = _defaultHeight;
            _currentFOV = _camera.fieldOfView;
        }

        private void Start()
        {
            if (_target == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    _target = player.transform;
                }
            }

            // Initialize angles from current position
            Vector3 direction = transform.position - _target.position;
            _horizontalAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            _verticalAngle = Mathf.Asin(direction.y / direction.magnitude) * Mathf.Rad2Deg;
        }

        // ====================================================================
        // LATE UPDATE — Camera always updates AFTER player movement
        // ====================================================================

        private void LateUpdate()
        {
            if (_target == null) return;

            UpdateOrbitalAngles();
            UpdateCameraMode();

            Vector3 targetPosition = CalculateDesiredPosition();
            targetPosition = HandleCollision(targetPosition);
            targetPosition += CalculateShakeOffset();

            // Smooth position
            transform.position = Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref _positionVelocity,
                _positionSmoothTime
            );

            // Look at target
            Vector3 lookTarget = _target.position + _targetOffset;
            Quaternion targetRotation = Quaternion.LookRotation(lookTarget - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, (1f / _rotationSmoothTime) * Time.deltaTime);

            // Smooth FOV
            _camera.fieldOfView = Mathf.SmoothDamp(_camera.fieldOfView, _currentFOV, ref _fovVelocity, _fovSmoothTime);

            // Decay shake
            _shakeIntensity = Mathf.Max(0, _shakeIntensity - _shakeDecayRate * Time.deltaTime);
        }

        // ====================================================================
        // ORBITAL MOVEMENT
        // ====================================================================

        /// <summary>
        /// Updates the camera orbit based on touch/stick input.
        /// </summary>
        private void UpdateOrbitalAngles()
        {
            float sensitivity = _currentMode == CameraMode.Aiming ? _aimSensitivity : _orbitSensitivity;

            _horizontalAngle += _lookInput.x * sensitivity;
            _verticalAngle -= _lookInput.y * sensitivity;

            // Clamp vertical angle to prevent flipping
            _verticalAngle = Mathf.Clamp(_verticalAngle, _minVerticalAngle, _maxVerticalAngle);
        }

        /// <summary>
        /// Set look input from touch controls or right stick.
        /// </summary>
        public void SetLookInput(Vector2 input)
        {
            _lookInput = input;
        }

        // ====================================================================
        // CAMERA MODE TRANSITIONS
        // ====================================================================

        public void SetMode(CameraMode mode)
        {
            _currentMode = mode;
        }

        private void UpdateCameraMode()
        {
            float targetDistance;
            float targetHeight;
            float targetFOV;

            switch (_currentMode)
            {
                case CameraMode.Aiming:
                    targetDistance = _aimDistance;
                    targetHeight = _aimHeight;
                    targetFOV = _aimFOV;
                    break;

                case CameraMode.Driving:
                    targetDistance = _driveDistance;
                    targetHeight = _driveHeight;
                    targetFOV = _driveFOV;
                    break;

                default:
                    targetDistance = _defaultDistance;
                    targetHeight = _defaultHeight;
                    targetFOV = 60f;
                    break;
            }

            // Smoothly interpolate between modes
            _currentDistance = Mathf.Lerp(_currentDistance, targetDistance, _modeTransitionSpeed * Time.deltaTime);
            _currentHeight = Mathf.Lerp(_currentHeight, targetHeight, _modeTransitionSpeed * Time.deltaTime);
            _currentFOV = targetFOV;
        }

        // ====================================================================
        // POSITION CALCULATION
        // ====================================================================

        private Vector3 CalculateDesiredPosition()
        {
            // Convert orbital angles to a direction vector
            Quaternion rotation = Quaternion.Euler(_verticalAngle, _horizontalAngle, 0);
            Vector3 offset = rotation * new Vector3(0, 0, -_currentDistance);

            Vector3 targetPos = _target.position + _targetOffset;
            Vector3 desiredPosition = targetPos + offset;
            desiredPosition.y = targetPos.y + _currentHeight;

            // Apply aim offset (over-shoulder) when aiming
            if (_currentMode == CameraMode.Aiming)
            {
                desiredPosition += _target.right * _aimOffset.x;
            }

            return desiredPosition;
        }

        // ====================================================================
        // COLLISION HANDLING
        // ====================================================================

        /// <summary>
        /// Prevents the camera from clipping through walls.
        /// Uses a SphereCast from the target to the desired camera position.
        /// If something is in the way, the camera is pulled closer.
        /// </summary>
        private Vector3 HandleCollision(Vector3 desiredPosition)
        {
            Vector3 targetPos = _target.position + _targetOffset;
            Vector3 direction = desiredPosition - targetPos;
            float maxDistance = direction.magnitude;

            if (Physics.SphereCast(
                targetPos,
                _collisionRadius,
                direction.normalized,
                out RaycastHit hit,
                maxDistance,
                _collisionLayers))
            {
                // Something is between camera and player — pull camera closer
                float safeDistance = hit.distance - _collisionRadius;
                _collisionDistance = Mathf.Lerp(
                    _collisionDistance,
                    safeDistance,
                    _collisionSnapSpeed * Time.deltaTime
                );
            }
            else
            {
                // Clear path — recover to desired distance
                _collisionDistance = Mathf.Lerp(
                    _collisionDistance,
                    maxDistance,
                    _collisionRecoverSpeed * Time.deltaTime
                );
            }

            return targetPos + direction.normalized * _collisionDistance;
        }

        // ====================================================================
        // CAMERA SHAKE
        // ====================================================================

        /// <summary>
        /// Trigger a camera shake. Called during explosions, impacts, etc.
        /// Uses Perlin noise for smooth, organic shake (no allocations).
        /// </summary>
        public void Shake(float intensity, float frequency = 25f)
        {
            _shakeIntensity = Mathf.Max(_shakeIntensity, intensity);
            _shakeFrequency = frequency;
        }

        private Vector3 CalculateShakeOffset()
        {
            if (_shakeIntensity <= 0.01f) return Vector3.zero;

            float time = Time.time * _shakeFrequency;
            float x = (Mathf.PerlinNoise(time, 0f) - 0.5f) * 2f * _shakeIntensity;
            float y = (Mathf.PerlinNoise(0f, time) - 0.5f) * 2f * _shakeIntensity;

            return new Vector3(x, y, 0f);
        }
    }
}
