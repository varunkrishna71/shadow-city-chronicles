// ============================================================================
// CoverSystem.cs — Snap-to-cover system for tactical combat
// ============================================================================
// PURPOSE:
//   Allows the player to take cover behind objects (walls, cars, crates).
//   When near a valid cover surface and pressing the cover button, the player
//   snaps to the surface and can peek, blind fire, or transition between covers.
//
// HOW IT WORKS:
//   1. Constantly scans for nearby cover surfaces using SphereCast
//   2. When player presses cover button near a valid surface, snap to it
//   3. Player can slide along the cover edge
//   4. Player can peek left/right to aim and shoot
//   5. Player can vault over low cover
//
// MOBILE OPTIMIZATION:
//   - Cover detection uses a single SphereCast (not multiple raycasts)
//   - Cover surfaces are pre-tagged (no runtime material checks)
//   - Animation blending uses cached hash parameters
// ============================================================================

using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.Player
{
    public enum CoverType
    {
        None,
        Full,   // Tall cover — player is fully hidden
        Half    // Low cover — player crouches, can vault over
    }

    public enum CoverSide
    {
        Left,
        Right
    }

    public class CoverSystem : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField] private float _coverDetectionRange = 1.5f;
        [SerializeField] private float _coverSnapDistance = 0.5f;
        [SerializeField] private LayerMask _coverLayer;
        [SerializeField] private float _coverCheckInterval = 0.2f; // Check every 0.2s, not every frame

        [Header("Movement")]
        [SerializeField] private float _coverSlideSpeed = 3f;
        [SerializeField] private float _peekDistance = 0.4f;
        [SerializeField] private float _vaultSpeed = 5f;

        [Header("Cover Heights")]
        [SerializeField] private float _halfCoverHeight = 1.0f;
        [SerializeField] private float _fullCoverHeight = 1.8f;

        // State
        private bool _isInCover;
        private CoverType _currentCoverType;
        private Vector3 _coverNormal;       // Direction away from cover surface
        private Vector3 _coverPoint;        // Exact point on the cover surface
        private Vector3 _coverDirection;    // Direction along the cover (left/right)

        private bool _isPeeking;
        private CoverSide _peekSide;
        private float _coverCheckTimer;

        // Nearby cover info (updated periodically, not every frame)
        private bool _coverAvailable;
        private RaycastHit _nearestCoverHit;

        // References
        private PlayerController _playerController;
        private CharacterController _characterController;
        private Animator _animator;

        // Animation hashes
        private static readonly int AnimInCover = Animator.StringToHash("InCover");
        private static readonly int AnimCoverType = Animator.StringToHash("CoverType");
        private static readonly int AnimPeekSide = Animator.StringToHash("PeekSide");
        private static readonly int AnimIsPeeking = Animator.StringToHash("IsPeeking");

        public bool IsInCover => _isInCover;
        public CoverType CurrentCoverType => _currentCoverType;
        public bool IsPeeking => _isPeeking;

        private void Awake()
        {
            _playerController = GetComponent<PlayerController>();
            _characterController = GetComponent<CharacterController>();
            _animator = GetComponent<Animator>();
        }

        private void Update()
        {
            if (_playerController.CurrentState == PlayerState.Dead ||
                _playerController.CurrentState == PlayerState.InVehicle)
                return;

            // Periodically check for nearby cover (not every frame!)
            _coverCheckTimer += Time.deltaTime;
            if (_coverCheckTimer >= _coverCheckInterval)
            {
                _coverCheckTimer = 0f;
                ScanForCover();
            }

            if (_isInCover)
            {
                ProcessCoverMovement();
            }
        }

        // ====================================================================
        // COVER DETECTION
        // ====================================================================

        /// <summary>
        /// Scans for cover surfaces near the player.
        /// Uses a single SphereCast for efficiency.
        /// </summary>
        private void ScanForCover()
        {
            if (_isInCover) return;

            // Cast a sphere forward to detect cover
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            Vector3 direction = transform.forward;

            _coverAvailable = Physics.SphereCast(
                origin,
                0.3f,
                direction,
                out _nearestCoverHit,
                _coverDetectionRange,
                _coverLayer
            );
        }

        /// <summary>
        /// Called when player presses the cover button.
        /// Attempts to enter cover if a valid surface is nearby.
        /// </summary>
        public void TryEnterCover()
        {
            if (_isInCover)
            {
                ExitCover();
                return;
            }

            if (!_coverAvailable) return;

            _coverPoint = _nearestCoverHit.point;
            _coverNormal = _nearestCoverHit.normal;

            // Determine cover type based on height
            float coverHeight = GetCoverHeight(_nearestCoverHit.collider);

            if (coverHeight >= _fullCoverHeight)
                _currentCoverType = CoverType.Full;
            else if (coverHeight >= _halfCoverHeight)
                _currentCoverType = CoverType.Half;
            else
                return; // Too short to be cover

            // Calculate the direction along the cover surface
            _coverDirection = Vector3.Cross(_coverNormal, Vector3.up).normalized;

            // Snap player to cover position
            Vector3 snapPosition = _coverPoint + _coverNormal * _coverSnapDistance;
            snapPosition.y = transform.position.y;

            _characterController.enabled = false;
            transform.position = snapPosition;
            _characterController.enabled = true;

            // Face away from cover (toward enemies)
            transform.rotation = Quaternion.LookRotation(-_coverNormal);

            _isInCover = true;
            _isPeeking = false;

            UpdateAnimator();
        }

        /// <summary>
        /// Exit cover and return to normal movement.
        /// </summary>
        public void ExitCover()
        {
            _isInCover = false;
            _isPeeking = false;
            _currentCoverType = CoverType.None;

            UpdateAnimator();
        }

        // ====================================================================
        // COVER MOVEMENT
        // ====================================================================

        private void ProcessCoverMovement()
        {
            // Player can slide left/right along the cover
            // MoveInput.x controls sliding direction
        }

        /// <summary>
        /// Slide along the cover surface in the given direction.
        /// </summary>
        public void SlideAlongCover(float direction)
        {
            if (!_isInCover) return;

            Vector3 slideDirection = _coverDirection * direction;
            Vector3 newPosition = transform.position + slideDirection * _coverSlideSpeed * Time.deltaTime;

            // Check if there's still cover at the new position
            Vector3 checkOrigin = newPosition + Vector3.up * 0.5f;

            if (Physics.Raycast(checkOrigin, -_coverNormal, out RaycastHit hit, _coverSnapDistance + 0.5f, _coverLayer))
            {
                // Still have cover — move
                _characterController.Move(slideDirection * _coverSlideSpeed * Time.deltaTime);

                // Update cover point and normal (surface might curve)
                _coverPoint = hit.point;
                _coverNormal = hit.normal;
                _coverDirection = Vector3.Cross(_coverNormal, Vector3.up).normalized;
            }
            else
            {
                // Reached the edge of cover — stop or allow exit
            }
        }

        // ====================================================================
        // PEEK AND FIRE
        // ====================================================================

        /// <summary>
        /// Peek out from cover to aim. Called when player holds aim button in cover.
        /// </summary>
        public void StartPeek(CoverSide side)
        {
            if (!_isInCover) return;

            _isPeeking = true;
            _peekSide = side;

            UpdateAnimator();
        }

        public void StopPeek()
        {
            _isPeeking = false;
            UpdateAnimator();
        }

        /// <summary>
        /// Get the peek position for the camera and aiming system.
        /// </summary>
        public Vector3 GetPeekPosition()
        {
            if (!_isPeeking) return transform.position;

            Vector3 peekOffset = _peekSide == CoverSide.Left ? -_coverDirection : _coverDirection;
            return transform.position + peekOffset * _peekDistance + Vector3.up * 1.5f;
        }

        // ====================================================================
        // VAULT
        // ====================================================================

        /// <summary>
        /// Vault over low cover. Only works with half-cover.
        /// </summary>
        public bool TryVault()
        {
            if (!_isInCover || _currentCoverType != CoverType.Half) return false;

            // Check if there's space on the other side
            Vector3 vaultEnd = _coverPoint - _coverNormal * 1.5f + Vector3.up * 0.1f;

            if (!Physics.Raycast(vaultEnd + Vector3.up * 2f, Vector3.down, 3f, _coverLayer))
            {
                // Space is clear — vault
                ExitCover();
                _characterController.enabled = false;
                transform.position = vaultEnd;
                _characterController.enabled = true;
                return true;
            }

            return false;
        }

        // ====================================================================
        // HELPERS
        // ====================================================================

        private float GetCoverHeight(Collider coverCollider)
        {
            return coverCollider.bounds.size.y;
        }

        private void UpdateAnimator()
        {
            _animator.SetBool(AnimInCover, _isInCover);
            _animator.SetInteger(AnimCoverType, (int)_currentCoverType);
            _animator.SetBool(AnimIsPeeking, _isPeeking);
            _animator.SetInteger(AnimPeekSide, _isPeeking ? (int)_peekSide : -1);
        }

        // ====================================================================
        // DEBUG
        // ====================================================================

        private void OnDrawGizmosSelected()
        {
            // Cover detection range
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f + transform.forward * _coverDetectionRange, 0.3f);

            if (_isInCover)
            {
                // Cover normal
                Gizmos.color = Color.green;
                Gizmos.DrawRay(_coverPoint, _coverNormal * 2f);

                // Cover direction
                Gizmos.color = Color.blue;
                Gizmos.DrawRay(_coverPoint, _coverDirection * 2f);
                Gizmos.DrawRay(_coverPoint, -_coverDirection * 2f);
            }
        }
    }
}
