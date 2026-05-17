// ============================================================================
// PoliceAI.cs — Police response and pursuit system
// ============================================================================
// PURPOSE:
//   Controls police NPCs. They patrol the city, respond to crimes, pursue
//   the player based on wanted level, and can arrest or kill.
//
// BEHAVIOR BY WANTED LEVEL:
//   ★☆☆☆☆ (1): Nearby officers investigate. Attempt arrest on sight.
//   ★★☆☆☆ (2): Police cars respond. Officers shoot if player resists.
//   ★★★☆☆ (3): Roadblocks. Helicopter (visual only on mobile). Aggressive pursuit.
//   ★★★★☆ (4): SWAT teams. Armored vehicles. Shoot on sight.
//   ★★★★★ (5): Military response. Overwhelming force. Nearly impossible to survive.
//
// MOBILE OPTIMIZATION:
//   - Only officers within 100m of player are active
//   - Pursuit pathfinding uses NavMesh corridors (not full A*)
//   - Roadblock positions are pre-calculated (not runtime computed)
//   - Maximum 8 active police NPCs at any time
// ============================================================================

using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.AI.Police
{
    public enum PoliceState
    {
        Patrolling,     // Walking/driving patrol route
        Investigating,  // Moving to last crime location
        Pursuing,       // Chasing the player
        InCombat,       // Shooting at player
        Arresting,      // Attempting arrest (close range)
        SearchArea,     // Player escaped — searching last known area
        Returning        // Crime resolved, returning to patrol
    }

    public class PoliceAI : AIAgent
    {
        [Header("Police Settings")]
        [SerializeField] private float _pursuitSpeed = 8f;
        [SerializeField] private float _investigateSpeed = 4f;
        [SerializeField] private float _arrestRange = 3f;
        [SerializeField] private float _combatRange = 25f;
        [SerializeField] private float _searchDuration = 30f;

        [Header("Patrol")]
        [SerializeField] private Transform[] _patrolWaypoints;
        [SerializeField] private bool _isVehiclePatrol;

        [Header("Equipment")]
        [SerializeField] private bool _hasTaser;
        [SerializeField] private bool _hasPistol = true;
        [SerializeField] private bool _hasRifle;
        [SerializeField] private bool _hasArmor;

        // State
        private PoliceState _state = PoliceState.Patrolling;
        private int _currentWaypointIndex;
        private float _searchTimer;
        private Vector3 _investigatePosition;
        private bool _playerVisible;
        private int _currentWantedLevel;

        protected override void Awake()
        {
            base.Awake();
        }

        private void Start()
        {
            EventBus.Subscribe<WantedLevelChangedEvent>(OnWantedLevelChanged);
            EventBus.Subscribe<WeaponFiredEvent>(OnWeaponFired);

            if (_patrolWaypoints != null && _patrolWaypoints.Length > 0)
            {
                MoveTo(_patrolWaypoints[0].position);
            }
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<WantedLevelChangedEvent>(OnWantedLevelChanged);
            EventBus.Unsubscribe<WeaponFiredEvent>(OnWeaponFired);
        }

        protected override void Update()
        {
            base.Update();

            _playerVisible = CurrentThreat != null;

            switch (_state)
            {
                case PoliceState.Patrolling:
                    UpdatePatrol();
                    break;
                case PoliceState.Investigating:
                    UpdateInvestigation();
                    break;
                case PoliceState.Pursuing:
                    UpdatePursuit();
                    break;
                case PoliceState.InCombat:
                    UpdateCombat();
                    break;
                case PoliceState.Arresting:
                    UpdateArrest();
                    break;
                case PoliceState.SearchArea:
                    UpdateSearch();
                    break;
                case PoliceState.Returning:
                    UpdateReturn();
                    break;
            }
        }

        // ====================================================================
        // STATE BEHAVIORS
        // ====================================================================

        private void UpdatePatrol()
        {
            if (_currentWantedLevel > 0 && _playerVisible)
            {
                EnterState(PoliceState.Pursuing);
                return;
            }

            if (_patrolWaypoints == null || _patrolWaypoints.Length == 0) return;

            if (HasReachedDestination())
            {
                _currentWaypointIndex = (_currentWaypointIndex + 1) % _patrolWaypoints.Length;
                MoveTo(_patrolWaypoints[_currentWaypointIndex].position);
            }
        }

        private void UpdateInvestigation()
        {
            if (_playerVisible && _currentWantedLevel > 0)
            {
                EnterState(PoliceState.Pursuing);
                return;
            }

            if (HasReachedDestination())
            {
                EnterState(PoliceState.SearchArea);
            }
        }

        private void UpdatePursuit()
        {
            if (!_playerVisible)
            {
                EnterState(PoliceState.SearchArea);
                return;
            }

            float distToPlayer = Vector3.Distance(transform.position, CurrentThreat.position);

            // Close enough to arrest (low wanted level)
            if (distToPlayer < _arrestRange && _currentWantedLevel <= 1)
            {
                EnterState(PoliceState.Arresting);
                return;
            }

            // Close enough to engage in combat
            if (distToPlayer < _combatRange && _currentWantedLevel >= 2)
            {
                EnterState(PoliceState.InCombat);
                return;
            }

            // Continue chasing
            MoveTo(CurrentThreat.position, true);
        }

        private void UpdateCombat()
        {
            if (!_playerVisible)
            {
                EnterState(PoliceState.SearchArea);
                return;
            }

            float distToPlayer = Vector3.Distance(transform.position, CurrentThreat.position);

            // Face the player
            FaceTarget(CurrentThreat.position);

            // If player gets too far, pursue
            if (distToPlayer > _combatRange * 1.5f)
            {
                EnterState(PoliceState.Pursuing);
                return;
            }

            // Find cover if possible, otherwise stand and shoot
            if (distToPlayer > _arrestRange)
            {
                // Strafe and shoot logic would go here
                // For now, advance slowly while maintaining aim
                if (distToPlayer > _combatRange * 0.5f)
                {
                    MoveTo(CurrentThreat.position);
                }
            }
        }

        private void UpdateArrest()
        {
            if (CurrentThreat == null)
            {
                EnterState(PoliceState.SearchArea);
                return;
            }

            float distToPlayer = Vector3.Distance(transform.position, CurrentThreat.position);

            if (distToPlayer > _arrestRange * 2f)
            {
                EnterState(PoliceState.Pursuing);
                return;
            }

            FaceTarget(CurrentThreat.position);
            StopMoving();

            // Arrest logic — player must comply or resist
            // If player doesn't move for 3 seconds, arrest succeeds
            // If player runs or attacks, escalate to combat
        }

        private void UpdateSearch()
        {
            _searchTimer -= Time.deltaTime;

            if (_playerVisible && _currentWantedLevel > 0)
            {
                EnterState(PoliceState.Pursuing);
                return;
            }

            if (_searchTimer <= 0)
            {
                EnterState(PoliceState.Returning);
                return;
            }

            // Search pattern — move around last known position
            if (HasReachedDestination())
            {
                Vector3 searchPoint = LastKnownThreatPosition + Random.insideUnitSphere * 15f;
                searchPoint.y = transform.position.y;

                if (UnityEngine.AI.NavMesh.SamplePosition(searchPoint, out UnityEngine.AI.NavMeshHit hit, 15f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    MoveTo(hit.position);
                }
            }
        }

        private void UpdateReturn()
        {
            if (_playerVisible && _currentWantedLevel > 0)
            {
                EnterState(PoliceState.Pursuing);
                return;
            }

            if (HasReachedDestination())
            {
                EnterState(PoliceState.Patrolling);
            }
        }

        // ====================================================================
        // STATE TRANSITIONS
        // ====================================================================

        private void EnterState(PoliceState newState)
        {
            _state = newState;

            switch (newState)
            {
                case PoliceState.Pursuing:
                    if (NavAgent != null)
                    {
                        NavAgent.speed = _pursuitSpeed;
                    }
                    break;

                case PoliceState.Investigating:
                    MoveTo(_investigatePosition);
                    break;

                case PoliceState.SearchArea:
                    _searchTimer = _searchDuration;
                    break;

                case PoliceState.Returning:
                    if (_patrolWaypoints != null && _patrolWaypoints.Length > 0)
                    {
                        MoveTo(_patrolWaypoints[_currentWaypointIndex].position);
                    }
                    break;

                case PoliceState.InCombat:
                    StopMoving();
                    break;
            }
        }

        // ====================================================================
        // EVENT HANDLERS
        // ====================================================================

        private void OnWantedLevelChanged(WantedLevelChangedEvent evt)
        {
            _currentWantedLevel = evt.NewLevel;

            if (_currentWantedLevel > 0 && _state == PoliceState.Patrolling)
            {
                if (_playerVisible)
                {
                    EnterState(PoliceState.Pursuing);
                }
            }
            else if (_currentWantedLevel == 0)
            {
                EnterState(PoliceState.Returning);
            }
        }

        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            if (CanHearSound(evt.Origin, 50f) && _state == PoliceState.Patrolling)
            {
                _investigatePosition = evt.Origin;
                EnterState(PoliceState.Investigating);
            }
        }
    }
}
