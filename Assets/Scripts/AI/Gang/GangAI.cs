// ============================================================================
// GangAI.cs — Gang member behavior and territory control
// ============================================================================
// PURPOSE:
//   Gang members control territories and react based on their faction's
//   relationship with the player. They patrol their turf, deal drugs,
//   fight rival gangs, and either help or attack the player.
//
// FACTIONS:
//   - Southside Reapers (The Bishop's gang) — can become allies
//   - Korvac Syndicate — primary enemies
//   - Jade Dragons — neutral, will fight if provoked
//   - Iron Wolves — hired mercenaries, always hostile in missions
//
// MOBILE OPTIMIZATION:
//   - Gang territory checks use simple bounding boxes (not complex shapes)
//   - Group AI uses a leader/follower pattern (only leader pathfinds)
//   - Combat AI simplified to 3 behaviors: Aggressive, Defensive, Flanking
// ============================================================================

using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.AI.Gang
{
    public enum GangFaction
    {
        SouthsideReapers,
        KorvacSyndicate,
        JadeDragons,
        IronWolves
    }

    public enum GangRelation
    {
        Hostile,
        Neutral,
        Friendly
    }

    public enum GangMemberState
    {
        Idle,
        Patrolling,
        Guarding,
        InCombat,
        Flanking,
        Retreating,
        Dead
    }

    public class GangAI : AIAgent
    {
        [Header("Gang Identity")]
        [SerializeField] private GangFaction _faction;
        [SerializeField] private bool _isLeader;
        [SerializeField] private int _combatSkill = 50; // 0-100

        [Header("Territory")]
        [SerializeField] private Bounds _territoryBounds;

        [Header("Combat")]
        [SerializeField] private float _aggressionLevel = 0.5f;
        [SerializeField] private float _combatRange = 20f;
        [SerializeField] private float _flankDistance = 10f;
        [SerializeField] private float _retreatHealthThreshold = 0.2f;

        [Header("Group")]
        [SerializeField] private GangAI _leader;
        [SerializeField] private float _followDistance = 3f;

        // State
        private GangMemberState _state = GangMemberState.Idle;
        private GangRelation _playerRelation = GangRelation.Neutral;
        private Transform _combatTarget;
        private float _combatTimer;
        private Vector3 _guardPosition;

        public GangFaction Faction => _faction;
        public GangRelation PlayerRelation => _playerRelation;

        private void Start()
        {
            // Determine initial player relation based on faction
            _playerRelation = GetDefaultRelation();
            _guardPosition = transform.position;
        }

        protected override void Update()
        {
            base.Update();

            switch (_state)
            {
                case GangMemberState.Idle:
                    UpdateIdle();
                    break;
                case GangMemberState.Patrolling:
                    UpdatePatrol();
                    break;
                case GangMemberState.Guarding:
                    UpdateGuard();
                    break;
                case GangMemberState.InCombat:
                    UpdateCombat();
                    break;
                case GangMemberState.Flanking:
                    UpdateFlank();
                    break;
                case GangMemberState.Retreating:
                    UpdateRetreat();
                    break;
            }
        }

        // ====================================================================
        // STATE BEHAVIORS
        // ====================================================================

        private void UpdateIdle()
        {
            // Check for threats
            if (CurrentThreat != null && ShouldAttack())
            {
                _combatTarget = CurrentThreat;
                EnterState(GangMemberState.InCombat);
                return;
            }

            // If leader, start patrol. If follower, follow leader
            if (_isLeader)
            {
                EnterState(GangMemberState.Patrolling);
            }
            else if (_leader != null)
            {
                float distToLeader = Vector3.Distance(transform.position, _leader.transform.position);
                if (distToLeader > _followDistance * 2f)
                {
                    MoveTo(_leader.transform.position);
                }
            }
        }

        private void UpdatePatrol()
        {
            if (CurrentThreat != null && ShouldAttack())
            {
                _combatTarget = CurrentThreat;
                EnterState(GangMemberState.InCombat);
                return;
            }

            if (HasReachedDestination())
            {
                PickTerritoryWaypoint();
            }
        }

        private void UpdateGuard()
        {
            if (CurrentThreat != null && ShouldAttack())
            {
                _combatTarget = CurrentThreat;
                EnterState(GangMemberState.InCombat);
                return;
            }

            float distFromGuardPos = Vector3.Distance(transform.position, _guardPosition);
            if (distFromGuardPos > 2f)
            {
                MoveTo(_guardPosition);
            }
            else
            {
                StopMoving();
            }
        }

        private void UpdateCombat()
        {
            if (_combatTarget == null)
            {
                EnterState(GangMemberState.Patrolling);
                return;
            }

            float distToTarget = Vector3.Distance(transform.position, _combatTarget.position);
            FaceTarget(_combatTarget.position);

            // Decide combat behavior based on aggression and skill
            if (distToTarget > _combatRange)
            {
                MoveTo(_combatTarget.position, true);
            }
            else if (_aggressionLevel < 0.3f)
            {
                // Defensive — stay back and shoot from cover
                if (distToTarget < _combatRange * 0.3f)
                {
                    // Too close, back up
                    Vector3 retreatDir = (transform.position - _combatTarget.position).normalized;
                    MoveTo(transform.position + retreatDir * 5f);
                }
            }
            else if (_aggressionLevel > 0.7f && _combatSkill > 60)
            {
                // Aggressive + skilled — try to flank
                EnterState(GangMemberState.Flanking);
            }
        }

        private void UpdateFlank()
        {
            if (_combatTarget == null)
            {
                EnterState(GangMemberState.Patrolling);
                return;
            }

            // Calculate flanking position (to the side/behind the target)
            Vector3 toTarget = (_combatTarget.position - transform.position).normalized;
            Vector3 flankDir = Vector3.Cross(toTarget, Vector3.up).normalized;

            // Randomly pick left or right
            if (Random.value > 0.5f) flankDir = -flankDir;

            Vector3 flankPos = _combatTarget.position + flankDir * _flankDistance;

            if (HasReachedDestination())
            {
                MoveTo(flankPos, true);
                EnterState(GangMemberState.InCombat);
            }
        }

        private void UpdateRetreat()
        {
            if (HasReachedDestination())
            {
                // Find cover or return to territory
                EnterState(GangMemberState.Guarding);
            }
        }

        // ====================================================================
        // STATE TRANSITIONS
        // ====================================================================

        private void EnterState(GangMemberState newState)
        {
            _state = newState;
        }

        // ====================================================================
        // HELPERS
        // ====================================================================

        private bool ShouldAttack()
        {
            if (CurrentThreat == null) return false;

            // Check if the threat is the player
            if (CurrentThreat.CompareTag("Player"))
            {
                return _playerRelation == GangRelation.Hostile;
            }

            // Check if the threat is a rival gang member
            GangAI otherGang = CurrentThreat.GetComponent<GangAI>();
            if (otherGang != null && otherGang.Faction != _faction)
            {
                return true;
            }

            return false;
        }

        private void PickTerritoryWaypoint()
        {
            Vector3 randomPoint = new Vector3(
                Random.Range(_territoryBounds.min.x, _territoryBounds.max.x),
                transform.position.y,
                Random.Range(_territoryBounds.min.z, _territoryBounds.max.z)
            );

            if (UnityEngine.AI.NavMesh.SamplePosition(randomPoint, out UnityEngine.AI.NavMeshHit hit, 20f, UnityEngine.AI.NavMesh.AllAreas))
            {
                MoveTo(hit.position);
            }
        }

        private GangRelation GetDefaultRelation()
        {
            switch (_faction)
            {
                case GangFaction.KorvacSyndicate: return GangRelation.Hostile;
                case GangFaction.IronWolves: return GangRelation.Hostile;
                case GangFaction.SouthsideReapers: return GangRelation.Neutral;
                case GangFaction.JadeDragons: return GangRelation.Neutral;
                default: return GangRelation.Neutral;
            }
        }

        public void SetPlayerRelation(GangRelation relation)
        {
            _playerRelation = relation;
        }
    }
}
