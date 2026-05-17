// ============================================================================
// MobileInputManager.cs — Touch input handling for mobile controls
// ============================================================================
// PURPOSE:
//   Translates touch screen input into game actions. This is the bridge
//   between the player's fingers and the game systems.
//
// CONTROL LAYOUT:
//   Left side of screen:
//   - Virtual joystick (movement)
//   - Sprint button (hold)
//   - Crouch/Cover button
//
//   Right side of screen:
//   - Look/aim (drag anywhere on right side)
//   - Fire button
//   - Aim button (toggle)
//   - Reload button
//   - Jump button
//   - Interact button (context-sensitive)
//
//   Top:
//   - Weapon wheel (hold button, drag to select)
//   - Pause menu
//   - Phone button
//
//   Driving mode swaps controls:
//   - Left: Steering wheel or tilt controls
//   - Right: Gas/Brake pedals, handbrake, exit vehicle
//
// MOBILE UX PRINCIPLES:
//   1. Buttons must be large enough for thumbs (minimum 44x44 dp)
//   2. Critical actions (fire, brake) must be on the right thumb
//   3. No more than 4 visible buttons at once
//   4. Context-sensitive — only show what's needed
//   5. Haptic feedback on key actions (if device supports it)
// ============================================================================

using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.UI.MobileControls
{
    public enum ControlMode
    {
        OnFoot,
        Driving,
        Bike,
        Swimming,
        Menu
    }

    public class MobileInputManager : MonoBehaviour
    {
        private static MobileInputManager _instance;
        public static MobileInputManager Instance => _instance;

        [Header("Joystick")]
        [SerializeField] private RectTransform _joystickBackground;
        [SerializeField] private RectTransform _joystickHandle;
        [SerializeField] private float _joystickRadius = 80f;

        [Header("Look")]
        [SerializeField] private float _lookSensitivity = 0.3f;
        [SerializeField] private float _aimSensitivity = 0.15f;

        [Header("Driving")]
        [SerializeField] private bool _useTiltSteering = false;
        [SerializeField] private float _tiltSensitivity = 2f;

        [Header("Haptics")]
        [SerializeField] private bool _enableHaptics = true;
        [SerializeField] private long _lightVibrationMs = 10;
        [SerializeField] private long _heavyVibrationMs = 30;

        // State
        private ControlMode _currentMode = ControlMode.OnFoot;
        private Vector2 _moveInput;
        private Vector2 _lookInput;
        private bool _sprintInput;
        private bool _crouchInput;
        private bool _aimInput;
        private bool _fireInput;
        private bool _jumpInput;
        private bool _interactInput;
        private bool _reloadInput;

        // Driving inputs
        private float _steerInput;
        private float _gasInput;
        private float _brakeInput;
        private bool _handbrakeInput;

        // Touch tracking
        private int _joystickTouchId = -1;
        private int _lookTouchId = -1;
        private Vector2 _lookTouchStart;

        // Public accessors
        public Vector2 MoveInput => _moveInput;
        public Vector2 LookInput => _lookInput;
        public bool SprintInput => _sprintInput;
        public bool CrouchInput => _crouchInput;
        public bool AimInput => _aimInput;
        public bool FireInput => _fireInput;
        public bool JumpInput => _jumpInput;
        public bool InteractInput => _interactInput;
        public bool ReloadInput => _reloadInput;
        public float SteerInput => _steerInput;
        public float GasInput => _gasInput;
        public float BrakeInputValue => _brakeInput;
        public bool HandbrakeInput => _handbrakeInput;
        public ControlMode CurrentMode => _currentMode;

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
            // Reset one-frame inputs
            _jumpInput = false;
            _interactInput = false;
            _reloadInput = false;
            _lookInput = Vector2.zero;

            ProcessTouchInput();
            ProcessKeyboardInput(); // For editor testing
        }

        // ====================================================================
        // TOUCH INPUT
        // ====================================================================

        private void ProcessTouchInput()
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                bool isLeftSide = touch.position.x < Screen.width * 0.4f;

                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        if (isLeftSide && _joystickTouchId == -1)
                        {
                            _joystickTouchId = touch.fingerId;
                            UpdateJoystick(touch.position);
                        }
                        else if (!isLeftSide && _lookTouchId == -1)
                        {
                            _lookTouchId = touch.fingerId;
                            _lookTouchStart = touch.position;
                        }
                        break;

                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        if (touch.fingerId == _joystickTouchId)
                        {
                            UpdateJoystick(touch.position);
                        }
                        else if (touch.fingerId == _lookTouchId)
                        {
                            Vector2 delta = touch.deltaPosition;
                            float sensitivity = _aimInput ? _aimSensitivity : _lookSensitivity;
                            _lookInput = delta * sensitivity;
                        }
                        break;

                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        if (touch.fingerId == _joystickTouchId)
                        {
                            _joystickTouchId = -1;
                            _moveInput = Vector2.zero;
                            ResetJoystickVisual();
                        }
                        else if (touch.fingerId == _lookTouchId)
                        {
                            _lookTouchId = -1;
                            _lookInput = Vector2.zero;
                        }
                        break;
                }
            }

            // Tilt steering for vehicles
            if (_currentMode == ControlMode.Driving && _useTiltSteering)
            {
                _steerInput = Mathf.Clamp(Input.acceleration.x * _tiltSensitivity, -1f, 1f);
            }
        }

        private void UpdateJoystick(Vector2 touchPosition)
        {
            if (_joystickBackground == null) return;

            Vector2 backgroundPos = _joystickBackground.position;
            Vector2 direction = touchPosition - backgroundPos;
            float distance = direction.magnitude;

            // Clamp to joystick radius
            if (distance > _joystickRadius)
            {
                direction = direction.normalized * _joystickRadius;
            }

            // Normalize to -1..1 range
            _moveInput = direction / _joystickRadius;

            // Update visual
            if (_joystickHandle != null)
            {
                _joystickHandle.position = backgroundPos + direction;
            }
        }

        private void ResetJoystickVisual()
        {
            if (_joystickHandle != null && _joystickBackground != null)
            {
                _joystickHandle.position = _joystickBackground.position;
            }
        }

        // ====================================================================
        // KEYBOARD INPUT (Editor Testing)
        // ====================================================================

        private void ProcessKeyboardInput()
        {
#if UNITY_EDITOR
            // WASD movement
            Vector2 keyboardMove = Vector2.zero;
            if (Input.GetKey(KeyCode.W)) keyboardMove.y += 1;
            if (Input.GetKey(KeyCode.S)) keyboardMove.y -= 1;
            if (Input.GetKey(KeyCode.A)) keyboardMove.x -= 1;
            if (Input.GetKey(KeyCode.D)) keyboardMove.x += 1;

            if (keyboardMove.sqrMagnitude > 0.1f)
            {
                _moveInput = keyboardMove.normalized;
            }
            else if (_joystickTouchId == -1)
            {
                _moveInput = Vector2.zero;
            }

            // Mouse look
            if (Input.GetMouseButton(1))
            {
                _lookInput = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * _lookSensitivity * 10f;
            }

            _sprintInput = Input.GetKey(KeyCode.LeftShift);
            _crouchInput = Input.GetKey(KeyCode.LeftControl);
            _aimInput = Input.GetMouseButton(1);
            _fireInput = Input.GetMouseButton(0);
            _jumpInput = Input.GetKeyDown(KeyCode.Space);
            _interactInput = Input.GetKeyDown(KeyCode.E);
            _reloadInput = Input.GetKeyDown(KeyCode.R);
#endif
        }

        // ====================================================================
        // BUTTON CALLBACKS (called by UI buttons)
        // ====================================================================

        public void OnFireButtonDown() { _fireInput = true; TriggerHaptic(false); }
        public void OnFireButtonUp() => _fireInput = false;
        public void OnAimToggle() { _aimInput = !_aimInput; }
        public void OnSprintDown() => _sprintInput = true;
        public void OnSprintUp() => _sprintInput = false;
        public void OnCrouchPressed() => _crouchInput = !_crouchInput;
        public void OnJumpPressed() { _jumpInput = true; TriggerHaptic(false); }
        public void OnInteractPressed() { _interactInput = true; TriggerHaptic(false); }
        public void OnReloadPressed() => _reloadInput = true;

        // Driving buttons
        public void OnGasDown() => _gasInput = 1f;
        public void OnGasUp() => _gasInput = 0f;
        public void OnBrakeDown() => _brakeInput = 1f;
        public void OnBrakeUp() => _brakeInput = 0f;
        public void OnHandbrakeDown() => _handbrakeInput = true;
        public void OnHandbrakeUp() => _handbrakeInput = false;
        public void OnExitVehicle() => EventBus.Publish(new VehicleExitedEvent { VehicleId = "" });

        // ====================================================================
        // CONTROL MODE
        // ====================================================================

        public void SetControlMode(ControlMode mode)
        {
            _currentMode = mode;
            // UI would swap visible button layouts here
        }

        // ====================================================================
        // HAPTICS
        // ====================================================================

        private void TriggerHaptic(bool heavy)
        {
            if (!_enableHaptics) return;

#if UNITY_ANDROID && !UNITY_EDITOR
            long duration = heavy ? _heavyVibrationMs : _lightVibrationMs;
            Handheld.Vibrate();
#endif
        }

        public void TriggerShootHaptic() => TriggerHaptic(false);
        public void TriggerExplosionHaptic() => TriggerHaptic(true);
        public void TriggerDamageHaptic() => TriggerHaptic(true);
    }
}
