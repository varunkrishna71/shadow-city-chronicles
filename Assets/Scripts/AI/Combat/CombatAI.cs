// ============================================================================
// CombatAI.cs — Tactical combat behavior for armed NPCs
// ============================================================================
// PURPOSE:
//   Handles combat-specific behaviors that are shared across police, gang
//   members, and enemy NPCs. This includes:
//   - Taking cover
//   - Shooting at targets
//   - Flanking maneuvers
//   - Grenade throwing
//   - Suppressive fire
//   - Retreating when injured
//
// ARCHITECTURE:
//   CombatAI is a component that works WITH the faction-specific AI
//   (PoliceAI, GangAI). The faction AI decides WHEN to fight.
//   CombatAI decides HOW to fight.
//
// MOBILE OPTIMIZATION:
//   - Cover points are pre-baked (not runtime calculated)
//   - Shooting uses raycasts with accuracy modifiers (not projectiles)
//   - Only 6 NPCs can be in active combat simultaneously
//   - Animations use simple crossfade (no complex blend trees for enemies)
// ============================================================================

using UnityEngine;
using ShadowCity.Weapons;

namespace ShadowCity.AI.Combat
{
    public enum CombatBehavior
    {
        Aggressive,     // Push forward, close distance
        Defensive,      // Stay in cover, shoot from safety
        Flanking,       // Move to the side/behind target
        Suppressing,    // Keep firing to pin target down
        Retreating      // Fall back to safer position
    }

    public class CombatAI : MonoBehaviour
    {
        [Header("Combat Settings")]
        [SerializeField] private float _fireRate = 2f;          // Shots per second
        [SerializeField] private float _accuracy = 0.5f;        // 0 = terrible, 1 = perfect
        [SerializeField] private float _reactionTime = 0.5f;    // Delay before first shot
        [SerializeField] private float _burstLength = 3f;       // Seconds of sustained fire
        [SerializeField] private float _burstPause = 2f;        // Pause between bursts

        [Header("Cover")]
        [SerializeField] private float _coverSearchRadius = 15f;
        [SerializeField] private LayerMask _coverLayer;
        [SerializeField] private float _minCoverTime = 2f;
        [SerializeField] private float _maxCoverTime = 5f;

        [Header("Damage")]
        [SerializeField] private float _damagePerShot = 15f;
        [SerializeField] private float _effectiveRange = 30f;

        // State
        private CombatBehavior _behavior = CombatBehavior.Defensive;
        private Transform _target;
        private bool _isInCover;
        private bool _isFiring;
        private float _fireTimer;
        private float _burstTimer;
        private float _coverTimer;
        private float _reactionTimer;
        private bool _hasReacted;
        private Vector3 _coverPosition;

        // References
        private AIAgent _agent;
        private Animator _animator;

        private static readonly int AnimIsShooting = Animator.StringToHash("IsShooting");
        private static readonly int AnimInCover = Animator.StringToHash("InCover");

        private void Awake()
        {
            _agent = GetComponent<AIAgent>();
            _animator = GetComponent<Animator>();
        }

        /// <summary>
        /// Begin combat against a target. Called by the faction AI.
        /// </summary>
        public void EngageTarget(Transform target, CombatBehavior behavior)
        {
            _target = target;
            _behavior = behavior;
            _hasReacted = false;
            _reactionTimer = _reactionTime;
        }

        public void Disengage()
        {
            _target = null;
            _isFiring = false;
            _isInCover = false;

            if (_animator != null)
            {
                _animator.SetBool(AnimIsShooting, false);
                _animator.SetBool(AnimInCover, false);
            }
        }

        private void Update()
        {
            if (_target == null) return;

            // Reaction delay — NPCs don't instantly shoot
            if (!_hasReacted)
            {
                _reactionTimer -= Time.deltaTime;
                if (_reactionTimer <= 0)
                {
                    _hasReacted = true;
                }
                return;
            }

            switch (_behavior)
            {
                case CombatBehavior.Aggressive:
                    UpdateAggressive();
                    break;
                case CombatBehavior.Defensive:
                    UpdateDefensive();
                    break;
                case CombatBehavior.Flanking:
                    UpdateFlanking();
                    break;
                case CombatBehavior.Retreating:
                    UpdateRetreating();
                    break;
            }

            UpdateShooting();
        }

        // ====================================================================
        // COMBAT BEHAVIORS
        // ====================================================================

        private void UpdateAggressive()
        {
            float dist = Vector3.Distance(transform.position, _target.position);

            if (dist > _effectiveRange * 0.5f)
            {
                _agent.MoveTo(_target.position, true);
            }
            else
            {
                _agent.StopMoving();
            }

            _agent.FaceTarget(_target.position);
        }

        private void UpdateDefensive()
        {
            if (!_isInCover)
            {
                FindAndMoveToCover();
            }
            else
            {
                _agent.FaceTarget(_target.position);

                // Periodically change cover
                _coverTimer -= Time.deltaTime;
                if (_coverTimer <= 0)
                {
                    _isInCover = false;
                }
            }
        }

        private void UpdateFlanking()
        {
            Vector3 toTarget = (_target.position - transform.position).normalized;
            Vector3 flankDir = Vector3.Cross(toTarget, Vector3.up);

            Vector3 flankPos = _target.position + flankDir * 10f;
            _agent.MoveTo(flankPos, true);

            if (_agent.HasReachedDestination())
            {
                _behavior = CombatBehavior.Aggressive;
            }
        }

        private void UpdateRetreating()
        {
            Vector3 retreatDir = (transform.position - _target.position).normalized;
            Vector3 retreatPos = transform.position + retreatDir * 15f;
            _agent.MoveTo(retreatPos, true);
        }

        // ====================================================================
        // SHOOTING
        // ====================================================================

        private void UpdateShooting()
        {
            if (_target == null) return;

            float dist = Vector3.Distance(transform.position, _target.position);
            if (dist > _effectiveRange) return;

            // Check line of sight
            Vector3 origin = transform.position + Vector3.up * 1.5f;
            Vector3 toTarget = (_target.position + Vector3.up - origin).normalized;

            if (Physics.Raycast(origin, toTarget, out RaycastHit hit, dist))
            {
                if (hit.transform != _target && !hit.transform.IsChildOf(_target))
                    return; // Something blocking line of sight
            }

            // Burst fire pattern
            _burstTimer += Time.deltaTime;

            if (_burstTimer < _burstLength)
            {
                // Firing phase
                _fireTimer += Time.deltaTime;

                if (_fireTimer >= 1f / _fireRate)
                {
                    _fireTimer = 0f;
                    FireShot(origin, toTarget, dist);
                }

                _isFiring = true;
            }
            else if (_burstTimer >= _burstLength + _burstPause)
            {
                // Reset burst cycle
                _burstTimer = 0f;
                _isFiring = false;
            }
            else
            {
                _isFiring = false;
            }

            if (_animator != null)
            {
                _animator.SetBool(AnimIsShooting, _isFiring);
            }
        }

        private void FireShot(Vector3 origin, Vector3 direction, float distance)
        {
            // Apply accuracy — less accurate at longer range and lower skill
            float accuracyMod = _accuracy * Mathf.Lerp(1f, 0.3f, distance / _effectiveRange);
            float missAngle = (1f - accuracyMod) * 10f;

            Vector3 spreadDirection = direction + new Vector3(
                Random.Range(-missAngle, missAngle) * Mathf.Deg2Rad,
                Random.Range(-missAngle, missAngle) * Mathf.Deg2Rad,
                0f
            );

            if (Physics.Raycast(origin, spreadDirection, out RaycastHit hit, _effectiveRange))
            {
                IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
                if (damageable != null)
                {
                    // Distance damage falloff
                    float damageMod = Mathf.Lerp(1f, 0.5f, distance / _effectiveRange);
                    damageable.TakeDamage(_damagePerShot * damageMod, hit.point, spreadDirection);
                }
            }
        }

        // ====================================================================
        // COVER
        // ====================================================================

        private void FindAndMoveToCover()
        {
            // Find cover points near the NPC that face away from the target
            Collider[] coverObjects = Physics.OverlapSphere(transform.position, _coverSearchRadius, _coverLayer);

            float bestScore = float.MinValue;
            Vector3 bestCoverPos = transform.position;

            foreach (Collider cover in coverObjects)
            {
                Vector3 coverPos = cover.ClosestPoint(transform.position);

                // Score: prefer cover that is close to us but blocks line of sight to target
                float distToUs = Vector3.Distance(transform.position, coverPos);
                float distToTarget = Vector3.Distance(coverPos, _target.position);

                // Check if cover actually blocks the target
                Vector3 coverToTarget = (_target.position - coverPos).normalized;
                bool blocksTarget = Physics.Raycast(coverPos + Vector3.up, coverToTarget, distToTarget, _coverLayer);

                float score = (blocksTarget ? 10f : 0f) - distToUs + distToTarget * 0.5f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestCoverPos = coverPos;
                }
            }

            _coverPosition = bestCoverPos;
            _agent.MoveTo(_coverPosition, true);
            _isInCover = true;
            _coverTimer = Random.Range(_minCoverTime, _maxCoverTime);

            if (_animator != null)
            {
                _animator.SetBool(AnimInCover, true);
            }
        }
    }
}
