// ============================================================================
// VehicleController.cs — Arcade-realistic driving physics system
// ============================================================================
// PURPOSE:
//   Controls vehicle movement with a feel that's fun but weighty.
//   Not a sim racer — think arcade handling with realistic weight transfer.
//   Handles cars, trucks, and SUVs. Bikes use a separate system (BikeController).
//
// PHYSICS APPROACH:
//   Uses Unity's WheelCollider system for each wheel. WheelColliders handle:
//   - Suspension (how the car bounces over bumps)
//   - Friction (how tires grip the road)
//   - Steering geometry (Ackermann steering)
//
//   We ADD arcade-style assists on top:
//   - Counter-steer assist (prevents spin-outs on mobile)
//   - Speed-dependent steering (less turning at high speed)
//   - Automatic gear shifting
//
// MOBILE OPTIMIZATION:
//   - WheelColliders are computationally cheap (built into PhysX)
//   - Visual wheel meshes are separate from physics (can LOD independently)
//   - Particle effects (tire smoke, sparks) use object pooling
//   - Sound uses a single AudioSource with pitch modulation (not multiple clips)
//
// BEGINNER NOTE:
//   WheelCollider is a special Unity component that simulates a car wheel.
//   It handles suspension springs, tire friction, and motor torque.
//   You attach it to an empty GameObject at each wheel position,
//   then sync a visual wheel mesh to match its position.
// ============================================================================

using UnityEngine;
using ShadowCity.Core;
using ShadowCity.Player;

namespace ShadowCity.Vehicles
{
    [System.Serializable]
    public class WheelSetup
    {
        public WheelCollider Collider;
        public Transform VisualMesh;
        public bool IsDriveWheel;
        public bool IsSteerWheel;
    }

    public class VehicleController : MonoBehaviour, IInteractable
    {
        // ====================================================================
        // VEHICLE DATA
        // ====================================================================

        [Header("Vehicle Identity")]
        [SerializeField] private string _vehicleId;
        [SerializeField] private string _vehicleName;
        [SerializeField] private VehicleType _vehicleType = VehicleType.Sedan;

        [Header("Engine")]
        [SerializeField] private float _maxMotorTorque = 1500f;
        [SerializeField] private float _maxBrakeTorque = 3000f;
        [SerializeField] private float _maxSpeed = 180f;           // km/h
        [SerializeField] private float _reverseMaxSpeed = 40f;     // km/h
        [SerializeField] private AnimationCurve _torqueCurve;      // RPM → torque mapping

        [Header("Steering")]
        [SerializeField] private float _maxSteerAngle = 35f;
        [SerializeField] private float _steerSpeed = 5f;
        [SerializeField] private float _counterSteerForce = 0.3f;  // Arcade assist
        [SerializeField] private float _highSpeedSteerReduction = 0.5f;

        [Header("Wheels")]
        [SerializeField] private WheelSetup[] _wheels;

        [Header("Center of Mass")]
        [SerializeField] private Vector3 _centerOfMassOffset = new Vector3(0, -0.5f, 0);

        [Header("Downforce")]
        [SerializeField] private float _downforce = 100f;

        [Header("Audio")]
        [SerializeField] private AudioSource _engineAudio;
        [SerializeField] private float _minEnginePitch = 0.5f;
        [SerializeField] private float _maxEnginePitch = 2.5f;

        [Header("Entry/Exit")]
        [SerializeField] private Transform _driverSeat;
        [SerializeField] private Transform _exitPoint;

        // ====================================================================
        // RUNTIME STATE
        // ====================================================================

        private Rigidbody _rigidbody;
        private float _currentSpeed;          // km/h
        private float _currentSteerAngle;
        private float _motorInput;            // -1 to 1 (brake/reverse to accelerate)
        private float _steerInput;            // -1 to 1 (left to right)
        private bool _handbrakeInput;
        private bool _isOccupied;
        private GameObject _driver;
        private bool _engineRunning;

        // Calculated values
        private float _rpm;
        private int _currentGear;
        private float _throttlePercent;

        public string InteractionPrompt => _isOccupied ? "" : $"Enter {_vehicleName}";
        public bool IsOccupied => _isOccupied;
        public float SpeedKmh => _currentSpeed;
        public float RPM => _rpm;
        public int Gear => _currentGear;

        // ====================================================================
        // INITIALIZATION
        // ====================================================================

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        private void Start()
        {
            // Lower center of mass prevents easy rollovers
            _rigidbody.centerOfMass = _centerOfMassOffset;

            // Default torque curve if not set
            if (_torqueCurve == null || _torqueCurve.length == 0)
            {
                _torqueCurve = new AnimationCurve(
                    new Keyframe(0, 0.5f),
                    new Keyframe(0.3f, 1f),
                    new Keyframe(0.7f, 1f),
                    new Keyframe(1f, 0.6f)
                );
            }
        }

        // ====================================================================
        // PHYSICS UPDATE — Fixed timestep for consistent physics
        // ====================================================================

        private void FixedUpdate()
        {
            if (!_isOccupied || !_engineRunning)
            {
                ApplyBrakes(_maxBrakeTorque);
                return;
            }

            _currentSpeed = _rigidbody.linearVelocity.magnitude * 3.6f; // m/s to km/h

            ApplySteering();
            ApplyMotor();
            ApplyDownforce();
            UpdateWheelVisuals();
            UpdateGear();
        }

        private void Update()
        {
            if (_isOccupied && _engineRunning)
            {
                UpdateEngineSound();
            }
        }

        // ====================================================================
        // STEERING
        // ====================================================================

        private void ApplySteering()
        {
            // Reduce steering at high speed (prevents crazy turns at 200 km/h)
            float speedFactor = Mathf.Clamp01(_currentSpeed / _maxSpeed);
            float steerLimit = Mathf.Lerp(_maxSteerAngle, _maxSteerAngle * _highSpeedSteerReduction, speedFactor);

            // Smooth steering input
            float targetAngle = _steerInput * steerLimit;
            _currentSteerAngle = Mathf.Lerp(_currentSteerAngle, targetAngle, _steerSpeed * Time.fixedDeltaTime);

            // Apply counter-steer assist (arcade feel)
            float sidewaysVelocity = Vector3.Dot(_rigidbody.linearVelocity, transform.right);
            _currentSteerAngle -= sidewaysVelocity * _counterSteerForce;

            // Apply to steer wheels
            foreach (WheelSetup wheel in _wheels)
            {
                if (wheel.IsSteerWheel)
                {
                    wheel.Collider.steerAngle = _currentSteerAngle;
                }
            }
        }

        // ====================================================================
        // MOTOR
        // ====================================================================

        private void ApplyMotor()
        {
            if (_handbrakeInput)
            {
                ApplyBrakes(_maxBrakeTorque);
                return;
            }

            float speedNormalized = _currentSpeed / _maxSpeed;
            float torqueMultiplier = _torqueCurve.Evaluate(speedNormalized);

            if (_motorInput > 0)
            {
                // Accelerate (forward)
                if (_currentSpeed >= _maxSpeed)
                {
                    ApplyBrakes(0);
                    return;
                }

                float torque = _motorInput * _maxMotorTorque * torqueMultiplier;
                ApplyDriveTorque(torque);
                ApplyBrakes(0);
            }
            else if (_motorInput < 0)
            {
                if (_currentSpeed > 5f)
                {
                    // Braking (going forward but pressing back)
                    ApplyBrakes(Mathf.Abs(_motorInput) * _maxBrakeTorque);
                    ApplyDriveTorque(0);
                }
                else
                {
                    // Reverse
                    if (_currentSpeed < _reverseMaxSpeed)
                    {
                        float torque = _motorInput * _maxMotorTorque * 0.5f;
                        ApplyDriveTorque(torque);
                    }
                    ApplyBrakes(0);
                }
            }
            else
            {
                // No input — engine braking
                ApplyDriveTorque(0);
                ApplyBrakes(_maxBrakeTorque * 0.1f);
            }
        }

        private void ApplyDriveTorque(float torque)
        {
            foreach (WheelSetup wheel in _wheels)
            {
                if (wheel.IsDriveWheel)
                {
                    wheel.Collider.motorTorque = torque;
                }
            }
        }

        private void ApplyBrakes(float brakeTorque)
        {
            foreach (WheelSetup wheel in _wheels)
            {
                wheel.Collider.brakeTorque = brakeTorque;
            }
        }

        // ====================================================================
        // DOWNFORCE
        // ====================================================================

        private void ApplyDownforce()
        {
            float speedFactor = _currentSpeed / _maxSpeed;
            _rigidbody.AddForce(-transform.up * _downforce * speedFactor * speedFactor);
        }

        // ====================================================================
        // WHEEL VISUALS
        // ====================================================================

        /// <summary>
        /// Sync visual wheel meshes with physics wheel positions.
        /// WheelColliders are invisible — they just do physics.
        /// The visual meshes are what the player sees.
        /// </summary>
        private void UpdateWheelVisuals()
        {
            foreach (WheelSetup wheel in _wheels)
            {
                if (wheel.VisualMesh == null) continue;

                wheel.Collider.GetWorldPose(out Vector3 pos, out Quaternion rot);
                wheel.VisualMesh.position = pos;
                wheel.VisualMesh.rotation = rot;
            }
        }

        // ====================================================================
        // GEAR SYSTEM
        // ====================================================================

        private readonly float[] _gearRatios = { 3.5f, 2.5f, 1.8f, 1.3f, 1.0f, 0.8f };

        private void UpdateGear()
        {
            // Simple automatic transmission
            float speedPercent = _currentSpeed / _maxSpeed;
            _currentGear = Mathf.Clamp(Mathf.FloorToInt(speedPercent * _gearRatios.Length), 0, _gearRatios.Length - 1);

            // Calculate RPM for sound
            float gearRange = _maxSpeed / _gearRatios.Length;
            float speedInGear = _currentSpeed - (_currentGear * gearRange);
            _rpm = Mathf.Clamp01(speedInGear / gearRange);
        }

        // ====================================================================
        // ENGINE SOUND
        // ====================================================================

        private void UpdateEngineSound()
        {
            if (_engineAudio == null) return;

            float pitch = Mathf.Lerp(_minEnginePitch, _maxEnginePitch, _rpm);
            _engineAudio.pitch = pitch;

            float volume = Mathf.Lerp(0.3f, 1f, Mathf.Abs(_motorInput));
            _engineAudio.volume = volume;
        }

        // ====================================================================
        // INPUT
        // ====================================================================

        public void SetMotorInput(float input) => _motorInput = Mathf.Clamp(input, -1f, 1f);
        public void SetSteerInput(float input) => _steerInput = Mathf.Clamp(input, -1f, 1f);
        public void SetHandbrake(bool engaged) => _handbrakeInput = engaged;

        // ====================================================================
        // ENTER / EXIT VEHICLE
        // ====================================================================

        public void Interact(GameObject interactor)
        {
            if (_isOccupied) return;

            PlayerController player = interactor.GetComponent<PlayerController>();
            if (player == null) return;

            EnterVehicle(interactor);
        }

        public void EnterVehicle(GameObject driver)
        {
            _driver = driver;
            _isOccupied = true;
            _engineRunning = true;

            // Hide player model and parent to vehicle
            PlayerController playerController = driver.GetComponent<PlayerController>();
            if (playerController != null)
            {
                playerController.EnterVehicle();
                driver.transform.SetParent(_driverSeat);
                driver.transform.localPosition = Vector3.zero;
                driver.transform.localRotation = Quaternion.identity;
            }

            if (_engineAudio != null)
            {
                _engineAudio.Play();
            }

            EventBus.Publish(new VehicleEnteredEvent
            {
                VehicleId = _vehicleId,
                VehicleType = _vehicleType.ToString()
            });
        }

        public void ExitVehicle()
        {
            if (!_isOccupied) return;

            _engineRunning = false;
            _isOccupied = false;

            PlayerController playerController = _driver.GetComponent<PlayerController>();
            if (playerController != null)
            {
                _driver.transform.SetParent(null);
                Vector3 exitPos = _exitPoint != null ? _exitPoint.position : transform.position + transform.right * 2f;
                playerController.ExitVehicle(exitPos);
            }

            if (_engineAudio != null)
            {
                _engineAudio.Stop();
            }

            EventBus.Publish(new VehicleExitedEvent { VehicleId = _vehicleId });

            _driver = null;
        }
    }

    public enum VehicleType
    {
        Sedan,
        Muscle,
        SUV,
        Truck,
        Van,
        Sports,
        Motorcycle,
        Boat
    }
}
