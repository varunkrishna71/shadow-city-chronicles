// ============================================================================
// BikeController.cs — Motorcycle physics with leaning and balance
// ============================================================================
// PURPOSE:
//   Separate physics system for motorcycles because bikes handle COMPLETELY
//   differently from cars:
//   - They lean into turns
//   - They can wheelie and stoppie
//   - The rider can fall off
//   - They're faster to accelerate but harder to control
//
// PHYSICS:
//   Uses Rigidbody with custom forces (not WheelColliders) because
//   WheelColliders can't handle the leaning physics of motorcycles.
//   Balance is simulated with a torque force that fights gravity.
//
// MOBILE OPTIMIZATION:
//   - Single Rigidbody (no multi-body physics chain)
//   - Simplified tire friction model
//   - Lean angle computed analytically (no iterative solver)
// ============================================================================

using UnityEngine;
using ShadowCity.Core;
using ShadowCity.Player;

namespace ShadowCity.Vehicles
{
    public class BikeController : MonoBehaviour, IInteractable
    {
        [Header("Bike Identity")]
        [SerializeField] private string _vehicleId;
        [SerializeField] private string _vehicleName;

        [Header("Engine")]
        [SerializeField] private float _maxAcceleration = 25f;
        [SerializeField] private float _maxSpeed = 200f;
        [SerializeField] private float _brakeForce = 30f;
        [SerializeField] private float _engineBraking = 5f;

        [Header("Steering & Lean")]
        [SerializeField] private float _maxSteerAngle = 30f;
        [SerializeField] private float _maxLeanAngle = 45f;
        [SerializeField] private float _leanSpeed = 5f;
        [SerializeField] private float _steerSensitivity = 2f;

        [Header("Balance")]
        [SerializeField] private float _balanceForce = 100f;
        [SerializeField] private float _lowSpeedBalanceThreshold = 15f; // Below this speed, balance is harder
        [SerializeField] private float _fallAngle = 60f;

        [Header("Wheelie / Stoppie")]
        [SerializeField] private float _wheelieThreshold = 0.8f;
        [SerializeField] private float _stoppieThreshold = 0.8f;

        [Header("Ground Detection")]
        [SerializeField] private Transform _frontWheelPoint;
        [SerializeField] private Transform _rearWheelPoint;
        [SerializeField] private float _groundRayLength = 1.0f;
        [SerializeField] private LayerMask _groundLayer;

        [Header("Entry/Exit")]
        [SerializeField] private Transform _riderSeat;
        [SerializeField] private Transform _exitPoint;

        // Runtime state
        private Rigidbody _rigidbody;
        private float _currentSpeed;
        private float _currentLeanAngle;
        private float _steerInput;
        private float _throttleInput;
        private bool _brakeInput;
        private bool _isOccupied;
        private bool _hasFallen;
        private bool _frontGrounded;
        private bool _rearGrounded;
        private GameObject _rider;

        public string InteractionPrompt => _isOccupied ? "" : $"Ride {_vehicleName}";
        public float SpeedKmh => _currentSpeed;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        private void Start()
        {
            _rigidbody.centerOfMass = new Vector3(0, -0.3f, 0.1f);
        }

        private void FixedUpdate()
        {
            if (!_isOccupied || _hasFallen) return;

            CheckGround();
            _currentSpeed = _rigidbody.linearVelocity.magnitude * 3.6f;

            ApplyThrottle();
            ApplySteering();
            ApplyLean();
            ApplyBalance();
            CheckFall();
        }

        // ====================================================================
        // PHYSICS
        // ====================================================================

        private void ApplyThrottle()
        {
            if (_brakeInput)
            {
                Vector3 brakeDirection = -_rigidbody.linearVelocity.normalized;
                _rigidbody.AddForce(brakeDirection * _brakeForce, ForceMode.Acceleration);
                return;
            }

            if (Mathf.Abs(_throttleInput) > 0.1f && _currentSpeed < _maxSpeed)
            {
                Vector3 force = transform.forward * _throttleInput * _maxAcceleration;
                _rigidbody.AddForce(force, ForceMode.Acceleration);
            }
            else if (Mathf.Abs(_throttleInput) < 0.1f)
            {
                // Engine braking
                Vector3 brakingForce = -_rigidbody.linearVelocity.normalized * _engineBraking;
                _rigidbody.AddForce(brakingForce, ForceMode.Acceleration);
            }
        }

        private void ApplySteering()
        {
            if (_currentSpeed < 1f) return;

            float steerAngle = _steerInput * _maxSteerAngle;

            // Speed-dependent steering reduction
            float speedFactor = Mathf.Clamp01(_currentSpeed / _maxSpeed);
            steerAngle *= Mathf.Lerp(1f, 0.3f, speedFactor);

            // Apply turning force
            float turnForce = steerAngle * _steerSensitivity * (_currentSpeed / 50f);
            _rigidbody.AddTorque(Vector3.up * turnForce, ForceMode.Acceleration);
        }

        private void ApplyLean()
        {
            // Target lean angle based on steering and speed
            float targetLean = -_steerInput * _maxLeanAngle * Mathf.Clamp01(_currentSpeed / 30f);

            _currentLeanAngle = Mathf.Lerp(_currentLeanAngle, targetLean, _leanSpeed * Time.fixedDeltaTime);

            // Apply lean by rotating the bike
            Vector3 currentEuler = transform.eulerAngles;
            float currentZ = currentEuler.z;
            if (currentZ > 180f) currentZ -= 360f;

            float leanDiff = _currentLeanAngle - currentZ;
            _rigidbody.AddTorque(transform.forward * leanDiff * 5f, ForceMode.Acceleration);
        }

        private void ApplyBalance()
        {
            // Self-righting force — bikes naturally want to stay upright at speed
            float speedBalance = Mathf.Clamp01(_currentSpeed / _lowSpeedBalanceThreshold);

            Vector3 uprightDirection = Vector3.up;
            Vector3 bikeUp = transform.up;

            float angle = Vector3.Angle(bikeUp, uprightDirection);
            Vector3 correctionAxis = Vector3.Cross(bikeUp, uprightDirection);

            // Stronger correction at higher speeds
            float correctionForce = _balanceForce * speedBalance * (angle / 90f);
            _rigidbody.AddTorque(correctionAxis * correctionForce, ForceMode.Acceleration);
        }

        private void CheckGround()
        {
            Vector3 frontOrigin = _frontWheelPoint != null ? _frontWheelPoint.position : transform.position + transform.forward * 0.8f;
            Vector3 rearOrigin = _rearWheelPoint != null ? _rearWheelPoint.position : transform.position - transform.forward * 0.8f;

            _frontGrounded = Physics.Raycast(frontOrigin, -transform.up, _groundRayLength, _groundLayer);
            _rearGrounded = Physics.Raycast(rearOrigin, -transform.up, _groundRayLength, _groundLayer);
        }

        private void CheckFall()
        {
            float tiltAngle = Vector3.Angle(transform.up, Vector3.up);

            if (tiltAngle > _fallAngle && _currentSpeed < _lowSpeedBalanceThreshold)
            {
                Fall();
            }
        }

        private void Fall()
        {
            _hasFallen = true;

            if (_isOccupied)
            {
                EjectRider();
            }
        }

        // ====================================================================
        // RIDER MANAGEMENT
        // ====================================================================

        public void Interact(GameObject interactor)
        {
            if (_isOccupied) return;
            if (_hasFallen)
            {
                StandUp();
                return;
            }

            MountBike(interactor);
        }

        private void MountBike(GameObject rider)
        {
            _rider = rider;
            _isOccupied = true;

            PlayerController player = rider.GetComponent<PlayerController>();
            if (player != null)
            {
                player.EnterVehicle();
                rider.transform.SetParent(_riderSeat != null ? _riderSeat : transform);
                rider.transform.localPosition = Vector3.zero;
                rider.transform.localRotation = Quaternion.identity;
            }

            EventBus.Publish(new VehicleEnteredEvent
            {
                VehicleId = _vehicleId,
                VehicleType = "Motorcycle"
            });
        }

        private void EjectRider()
        {
            if (_rider == null) return;

            PlayerController player = _rider.GetComponent<PlayerController>();
            if (player != null)
            {
                _rider.transform.SetParent(null);
                Vector3 ejectDirection = (transform.right + Vector3.up).normalized;
                Vector3 exitPos = transform.position + ejectDirection * 2f;
                player.ExitVehicle(exitPos);
            }

            _isOccupied = false;
            EventBus.Publish(new VehicleExitedEvent { VehicleId = _vehicleId });
            _rider = null;
        }

        public void DismountBike()
        {
            if (!_isOccupied) return;

            PlayerController player = _rider.GetComponent<PlayerController>();
            if (player != null)
            {
                _rider.transform.SetParent(null);
                Vector3 exitPos = _exitPoint != null ? _exitPoint.position : transform.position + transform.right * 1.5f;
                player.ExitVehicle(exitPos);
            }

            _isOccupied = false;
            _rider = null;
        }

        private void StandUp()
        {
            _hasFallen = false;
            transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }

        // ====================================================================
        // INPUT
        // ====================================================================

        public void SetThrottleInput(float input) => _throttleInput = Mathf.Clamp(input, -1f, 1f);
        public void SetSteerInput(float input) => _steerInput = Mathf.Clamp(input, -1f, 1f);
        public void SetBrakeInput(bool braking) => _brakeInput = braking;
    }
}
