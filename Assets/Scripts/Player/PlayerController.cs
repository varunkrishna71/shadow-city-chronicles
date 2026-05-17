// ============================================================================
// PlayerController.cs — Third-person character movement and state management
// ============================================================================
// PURPOSE:
//   Controls Marcus's movement through the world. Handles walking, running,
//   sprinting, crouching, jumping, and transitions between states.
//
// ARCHITECTURE:
//   Uses a State Machine pattern. The player is always in exactly ONE state
//   (Idle, Walking, Running, Sprinting, Crouching, InCover, InVehicle, etc.)
//   Each state defines what inputs are valid and what animations play.
//
// MOBILE OPTIMIZATION:
//   - Uses CharacterController (not Rigidbody) for predictable, lightweight movement
//   - Animation parameters are cached as hashes (no string lookups per frame)
//   - Physics checks use layer masks to minimize collision queries
//   - Input is abstracted through InputProvider for touch/gamepad support
//
// BEGINNER NOTE:
//   CharacterController vs Rigidbody:
//   - CharacterController: Direct control over movement. You tell it WHERE to move.
//     Better for character controllers because it's predictable.
//   - Rigidbody: Physics-based. Forces push the character. Better for vehicles
//     and objects that need realistic physics.
//   We use CharacterController for the player because we want PRECISE control.
// ============================================================================

using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.Player
{
    public enum PlayerState
    {
        Idle,
        Walking,
        Running,
        Sprinting,
        Crouching,
        CrouchWalking,
        InCover,
        Aiming,
        Shooting,
        Melee,
        Climbing,
        Falling,
        Ragdoll,
        InVehicle,
        Dead,
        Dialogue
    }

    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Animator))]
    public class PlayerController : MonoBehaviour
    {
        // ====================================================================
        // CONFIGURATION — Tuned for GTA-like feel on mobile
        // ====================================================================

        [Header("Movement Speeds")]
        [SerializeField] private float _walkSpeed = 2.0f;
        [SerializeField] private float _runSpeed = 5.0f;
        [SerializeField] private float _sprintSpeed = 8.0f;
        [SerializeField] private float _crouchSpeed = 1.5f;
        [SerializeField] private float _aimWalkSpeed = 1.8f;

        [Header("Movement Feel")]
        [SerializeField] private float _acceleration = 10f;
        [SerializeField] private float _deceleration = 12f;
        [SerializeField] private float _rotationSpeed = 10f;
        [SerializeField] private float _gravity = -20f;
        [SerializeField] private float _groundedGravity = -2f;

        [Header("Stamina")]
        [SerializeField] private float _maxStamina = 100f;
        [SerializeField] private float _sprintStaminaDrain = 15f;  // per second
        [SerializeField] private float _staminaRegenRate = 8f;     // per second
        [SerializeField] private float _staminaRegenDelay = 2f;    // seconds after sprint

        [Header("Ground Check")]
        [SerializeField] private float _groundCheckRadius = 0.3f;
        [SerializeField] private LayerMask _groundLayer;

        [Header("Interaction")]
        [SerializeField] private float _interactionRange = 2.5f;
        [SerializeField] private LayerMask _interactableLayer;

        // ====================================================================
        // CACHED REFERENCES
        // ====================================================================

        private CharacterController _controller;
        private Animator _animator;
        private Transform _cameraTransform;

        // ====================================================================
        // RUNTIME STATE
        // ====================================================================

        private PlayerState _currentState = PlayerState.Idle;
        public PlayerState CurrentState => _currentState;

        private Vector3 _moveDirection;
        private Vector3 _verticalVelocity;
        private float _currentSpeed;
        private float _targetSpeed;
        private float _currentStamina;
        private float _staminaRegenTimer;
        private bool _isGrounded;
        private bool _isSprinting;

        // Input values (set by InputProvider or touch controls)
        private Vector2 _moveInput;
        private bool _sprintInput;
        private bool _crouchInput;
        private bool _aimInput;
        private bool _interactInput;

        // Animation parameter hashes — NEVER use strings in Update()!
        // String.GetHashCode() is called once here, then we use ints forever.
        private static readonly int AnimSpeed = Animator.StringToHash("Speed");
        private static readonly int AnimIsGrounded = Animator.StringToHash("IsGrounded");
        private static readonly int AnimIsCrouching = Animator.StringToHash("IsCrouching");
        private static readonly int AnimIsAiming = Animator.StringToHash("IsAiming");
        private static readonly int AnimVerticalSpeed = Animator.StringToHash("VerticalSpeed");
        private static readonly int AnimMoveX = Animator.StringToHash("MoveX");
        private static readonly int AnimMoveZ = Animator.StringToHash("MoveZ");

        // ====================================================================
        // INITIALIZATION
        // ====================================================================

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _animator = GetComponent<Animator>();
            _currentStamina = _maxStamina;
        }

        private void Start()
        {
            _cameraTransform = Camera.main.transform;

            // Subscribe to game state changes to disable movement during cutscenes, etc.
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStateChanged += HandleGameStateChanged;
            }
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStateChanged -= HandleGameStateChanged;
            }
        }

        // ====================================================================
        // UPDATE LOOP
        // ====================================================================

        private void Update()
        {
            // Don't process movement if game isn't in Playing state
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
                return;

            if (_currentState == PlayerState.Dead || _currentState == PlayerState.InVehicle)
                return;

            CheckGrounded();
            ProcessInput();
            ProcessMovement();
            ProcessStamina();
            UpdateAnimator();
            CheckInteraction();
        }

        // ====================================================================
        // INPUT PROCESSING
        // ====================================================================

        /// <summary>
        /// Sets movement input from the mobile touch joystick or gamepad.
        /// Called by the InputProvider/MobileControls system.
        /// </summary>
        public void SetMoveInput(Vector2 input)
        {
            _moveInput = input;
        }

        public void SetSprintInput(bool sprinting) => _sprintInput = sprinting;
        public void SetCrouchInput(bool crouching) => _crouchInput = crouching;
        public void SetAimInput(bool aiming) => _aimInput = aiming;
        public void SetInteractInput(bool interact) => _interactInput = interact;

        private void ProcessInput()
        {
            // Determine target speed based on input and state
            float inputMagnitude = _moveInput.magnitude;

            if (inputMagnitude < 0.1f)
            {
                _targetSpeed = 0f;
                SetState(PlayerState.Idle);
            }
            else if (_aimInput)
            {
                _targetSpeed = _aimWalkSpeed;
                SetState(PlayerState.Aiming);
            }
            else if (_crouchInput)
            {
                _targetSpeed = _crouchSpeed;
                SetState(inputMagnitude > 0.1f ? PlayerState.CrouchWalking : PlayerState.Crouching);
            }
            else if (_sprintInput && _currentStamina > 0 && inputMagnitude > 0.5f)
            {
                _targetSpeed = _sprintSpeed;
                _isSprinting = true;
                SetState(PlayerState.Sprinting);
            }
            else if (inputMagnitude > 0.5f)
            {
                _targetSpeed = _runSpeed;
                _isSprinting = false;
                SetState(PlayerState.Running);
            }
            else
            {
                _targetSpeed = _walkSpeed;
                _isSprinting = false;
                SetState(PlayerState.Walking);
            }
        }

        // ====================================================================
        // MOVEMENT
        // ====================================================================

        private void ProcessMovement()
        {
            // Smooth speed transition (acceleration/deceleration)
            float accel = _moveInput.magnitude > 0.1f ? _acceleration : _deceleration;
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, _targetSpeed, accel * Time.deltaTime);

            if (_moveInput.magnitude > 0.1f)
            {
                // Calculate movement direction relative to camera
                // This makes "up" on the joystick always move forward relative to where
                // the camera is looking — essential for third-person games
                Vector3 forward = _cameraTransform.forward;
                Vector3 right = _cameraTransform.right;

                // Flatten to horizontal plane (ignore camera pitch)
                forward.y = 0f;
                right.y = 0f;
                forward.Normalize();
                right.Normalize();

                _moveDirection = (forward * _moveInput.y + right * _moveInput.x).normalized;

                // Rotate character to face movement direction (smooth)
                if (_moveDirection.sqrMagnitude > 0.01f && !_aimInput)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(_moveDirection);
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        _rotationSpeed * Time.deltaTime
                    );
                }
            }

            // Apply horizontal movement
            Vector3 movement = _moveDirection * _currentSpeed;

            // Apply gravity
            if (_isGrounded && _verticalVelocity.y < 0)
            {
                _verticalVelocity.y = _groundedGravity;
            }
            else
            {
                _verticalVelocity.y += _gravity * Time.deltaTime;
            }

            movement.y = _verticalVelocity.y;

            // Move the character
            _controller.Move(movement * Time.deltaTime);
        }

        // ====================================================================
        // STAMINA
        // ====================================================================

        private void ProcessStamina()
        {
            if (_isSprinting)
            {
                _currentStamina -= _sprintStaminaDrain * Time.deltaTime;
                _staminaRegenTimer = _staminaRegenDelay;

                if (_currentStamina <= 0)
                {
                    _currentStamina = 0;
                    _isSprinting = false;
                    _sprintInput = false;
                }
            }
            else
            {
                _staminaRegenTimer -= Time.deltaTime;

                if (_staminaRegenTimer <= 0)
                {
                    _currentStamina = Mathf.Min(
                        _currentStamina + _staminaRegenRate * Time.deltaTime,
                        _maxStamina
                    );
                }
            }
        }

        public float GetStaminaNormalized() => _currentStamina / _maxStamina;

        // ====================================================================
        // GROUND CHECK
        // ====================================================================

        private void CheckGrounded()
        {
            // SphereCast from slightly above feet to detect ground
            Vector3 origin = transform.position + Vector3.up * _groundCheckRadius;
            _isGrounded = Physics.CheckSphere(origin, _groundCheckRadius, _groundLayer);

            if (!_isGrounded && _verticalVelocity.y < -10f)
            {
                SetState(PlayerState.Falling);
            }
        }

        // ====================================================================
        // INTERACTION
        // ====================================================================

        private void CheckInteraction()
        {
            if (!_interactInput) return;
            _interactInput = false;

            // Raycast forward to find interactable objects
            Ray ray = new Ray(transform.position + Vector3.up, transform.forward);

            if (Physics.Raycast(ray, out RaycastHit hit, _interactionRange, _interactableLayer))
            {
                IInteractable interactable = hit.collider.GetComponent<IInteractable>();
                interactable?.Interact(gameObject);
            }
        }

        // ====================================================================
        // STATE MANAGEMENT
        // ====================================================================

        private void SetState(PlayerState newState)
        {
            if (_currentState == newState) return;
            _currentState = newState;
        }

        /// <summary>
        /// Force player into vehicle state. Called by VehicleSystem.
        /// </summary>
        public void EnterVehicle()
        {
            _currentState = PlayerState.InVehicle;
            _controller.enabled = false;
            _moveInput = Vector2.zero;
            _currentSpeed = 0f;
        }

        /// <summary>
        /// Exit vehicle state. Called by VehicleSystem.
        /// </summary>
        public void ExitVehicle(Vector3 exitPosition)
        {
            _controller.enabled = true;
            transform.position = exitPosition;
            _currentState = PlayerState.Idle;
        }

        public void Kill()
        {
            _currentState = PlayerState.Dead;
            _controller.enabled = false;
            EventBus.Publish(new PlayerDeathEvent
            {
                CauseOfDeath = "Killed",
                DeathPosition = transform.position
            });
        }

        // ====================================================================
        // ANIMATION
        // ====================================================================

        private void UpdateAnimator()
        {
            float normalizedSpeed = _currentSpeed / _sprintSpeed;

            _animator.SetFloat(AnimSpeed, normalizedSpeed, 0.1f, Time.deltaTime);
            _animator.SetBool(AnimIsGrounded, _isGrounded);
            _animator.SetBool(AnimIsCrouching, _crouchInput);
            _animator.SetBool(AnimIsAiming, _aimInput);
            _animator.SetFloat(AnimVerticalSpeed, _verticalVelocity.y);

            // Strafe blend for aiming movement
            if (_aimInput)
            {
                Vector3 localMove = transform.InverseTransformDirection(_moveDirection);
                _animator.SetFloat(AnimMoveX, localMove.x, 0.1f, Time.deltaTime);
                _animator.SetFloat(AnimMoveZ, localMove.z, 0.1f, Time.deltaTime);
            }
        }

        // ====================================================================
        // EVENT HANDLERS
        // ====================================================================

        private void HandleGameStateChanged(GameState oldState, GameState newState)
        {
            if (newState == GameState.Cutscene || newState == GameState.Dialogue)
            {
                _moveInput = Vector2.zero;
                _currentSpeed = 0f;
            }
        }

        // ====================================================================
        // GIZMOS — Debug visualization in editor
        // ====================================================================

        private void OnDrawGizmosSelected()
        {
            // Ground check sphere
            Gizmos.color = _isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * _groundCheckRadius, _groundCheckRadius);

            // Interaction range
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position + Vector3.up, transform.forward * _interactionRange);
        }
    }

    /// <summary>
    /// Interface for any object the player can interact with
    /// (vehicles, NPCs, pickups, doors, etc.)
    /// </summary>
    public interface IInteractable
    {
        string InteractionPrompt { get; }
        void Interact(GameObject interactor);
    }
}
