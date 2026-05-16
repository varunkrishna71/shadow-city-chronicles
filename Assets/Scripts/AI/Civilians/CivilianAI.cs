// ============================================================================
// CivilianAI.cs — Pedestrian behavior system
// ============================================================================
// PURPOSE:
//   Controls civilian NPCs in the city. They walk around, react to threats,
//   have daily routines, and make the city feel alive.
//
// STATES:
//   Wandering → Walking between waypoints, looking around
//   Idle → Standing, checking phone, smoking, talking to another NPC
//   Fleeing → Running away from danger (gunshots, explosions, player with weapon)
//   Cowering → Crouching in place, too scared to run
//   Calling911 → Taking out phone, calling police (increases wanted level)
//   Reacting → Brief shock reaction (seeing a crash, witnessing crime)
//
// DAILY ROUTINES (simplified for mobile):
//   Morning (6-9): Walk to "work" waypoints
//   Day (9-17): Idle at work locations
//   Evening (17-21): Walk to "leisure" waypoints (bars, parks)
//   Night (21-6): Fewer civilians, more "shady" types
//
// MOBILE OPTIMIZATION:
//   - Civilians beyond 50m use simplified behavior (just walk waypoints)
//   - Civilians beyond 100m are frozen (disabled NavMeshAgent)
//   - Only civilians within 20m have full reaction systems
//   - Pooled — never instantiated/destroyed during gameplay
// ============================================================================

using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.AI.Civilians
{
    public enum CivilianState
    {
        Wandering,
        Idle,
        Fleeing,
        Cowering,
        Calling911,
        Reacting
    }

    public class CivilianAI : AIAgent
    {
        [Header("Civilian Settings")]
        [SerializeField] private float _fleeSpeed = 7f;
        [SerializeField] private float _fearThreshold = 50f;
        [SerializeField] private float _cowerThreshold = 80f;
        [SerializeField] private float _fearDecayRate = 10f;
        [SerializeField] private float _callPoliceDelay = 3f;

        [Header("Waypoints")]
        [SerializeField] private float _waypointRadius = 30f;
        [SerializeField] private float _idleDuration = 5f;

        [Header("LOD Distances")]
        [SerializeField] private float _fullBehaviorRange = 20f;
        [SerializeField] private float _simpleBehaviorRange = 50f;

        // State
        private CivilianState _state = CivilianState.Wandering;
        private float _fearLevel;
        private float _idleTimer;
        private Vector3 _currentWaypoint;
        private float _callPoliceTimer;
        private bool _hasCalledPolice;

        // LOD level
        private int _behaviorLOD; // 0 = full, 1 = simple, 2 = frozen
        private Transform _playerTransform;

        protected override void Awake()
        {
            base.Awake();
        }

        private void Start()
        {
            // Register states
            StateMachine.RegisterState(new CivilianWanderState());
            StateMachine.RegisterState(new CivilianIdleState());
            StateMachine.RegisterState(new CivilianFleeState());

            // Start wandering
            PickNewWaypoint();

            // Subscribe to dangerous events
            EventBus.Subscribe<WeaponFiredEvent>(OnWeaponFired);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<WeaponFiredEvent>(OnWeaponFired);
        }

        protected override void Update()
        {
            UpdateBehaviorLOD();

            if (_behaviorLOD >= 2) return; // Frozen — do nothing

            base.Update();

            UpdateFear();
            UpdateState();
        }

        // ====================================================================
        // BEHAVIOR LOD
        // ====================================================================

        private void UpdateBehaviorLOD()
        {
            if (_playerTransform == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) _playerTransform = player.transform;
                else return;
            }

            float distance = Vector3.Distance(transform.position, _playerTransform.position);

            if (distance < _fullBehaviorRange)
                _behaviorLOD = 0;
            else if (distance < _simpleBehaviorRange)
                _behaviorLOD = 1;
            else
                _behaviorLOD = 2;

            // Freeze/unfreeze NavMeshAgent
            if (NavAgent != null)
            {
                NavAgent.enabled = _behaviorLOD < 2;
            }
        }

        // ====================================================================
        // FEAR SYSTEM
        // ====================================================================

        private void UpdateFear()
        {
            // Decay fear over time
            _fearLevel = Mathf.Max(0, _fearLevel - _fearDecayRate * Time.deltaTime);
        }

        public void AddFear(float amount)
        {
            _fearLevel = Mathf.Min(100f, _fearLevel + amount);
        }

        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            if (CanHearSound(evt.Origin, 30f))
            {
                AddFear(40f);
            }
        }

        // ====================================================================
        // STATE MANAGEMENT
        // ====================================================================

        private void UpdateState()
        {
            switch (_state)
            {
                case CivilianState.Wandering:
                    UpdateWandering();
                    break;
                case CivilianState.Idle:
                    UpdateIdle();
                    break;
                case CivilianState.Fleeing:
                    UpdateFleeing();
                    break;
                case CivilianState.Cowering:
                    UpdateCowering();
                    break;
                case CivilianState.Calling911:
                    UpdateCalling911();
                    break;
            }

            // Fear-based transitions (high priority)
            if (_fearLevel >= _cowerThreshold && _state != CivilianState.Cowering)
            {
                EnterState(CivilianState.Cowering);
            }
            else if (_fearLevel >= _fearThreshold && _state != CivilianState.Fleeing && _state != CivilianState.Cowering)
            {
                EnterState(CivilianState.Fleeing);
            }
            else if (_fearLevel < _fearThreshold * 0.5f && (_state == CivilianState.Fleeing || _state == CivilianState.Cowering))
            {
                EnterState(CivilianState.Wandering);
                _hasCalledPolice = false;
            }
        }

        private void EnterState(CivilianState newState)
        {
            _state = newState;

            switch (newState)
            {
                case CivilianState.Wandering:
                    PickNewWaypoint();
                    break;
                case CivilianState.Fleeing:
                    FleeFromThreat();
                    break;
                case CivilianState.Idle:
                    StopMoving();
                    _idleTimer = Random.Range(_idleDuration * 0.5f, _idleDuration * 1.5f);
                    break;
                case CivilianState.Cowering:
                    StopMoving();
                    break;
            }
        }

        // ====================================================================
        // STATE BEHAVIORS
        // ====================================================================

        private void UpdateWandering()
        {
            if (HasReachedDestination())
            {
                EnterState(CivilianState.Idle);
            }
        }

        private void UpdateIdle()
        {
            _idleTimer -= Time.deltaTime;
            if (_idleTimer <= 0)
            {
                EnterState(CivilianState.Wandering);
            }
        }

        private void UpdateFleeing()
        {
            if (HasReachedDestination())
            {
                FleeFromThreat();
            }

            // Call police while fleeing
            if (!_hasCalledPolice && _behaviorLOD == 0)
            {
                _callPoliceTimer += Time.deltaTime;
                if (_callPoliceTimer >= _callPoliceDelay)
                {
                    EnterState(CivilianState.Calling911);
                }
            }
        }

        private void UpdateCowering()
        {
            // Just wait until fear subsides
        }

        private void UpdateCalling911()
        {
            _callPoliceTimer += Time.deltaTime;
            if (_callPoliceTimer >= _callPoliceDelay + 2f)
            {
                // Police have been called!
                _hasCalledPolice = true;
                EventBus.Publish(new WantedLevelChangedEvent { OldLevel = 0, NewLevel = 1 });
                EnterState(CivilianState.Fleeing);
            }
        }

        // ====================================================================
        // NAVIGATION
        // ====================================================================

        private void PickNewWaypoint()
        {
            // Pick a random point within radius on the NavMesh
            Vector3 randomDirection = Random.insideUnitSphere * _waypointRadius;
            randomDirection += transform.position;

            if (UnityEngine.AI.NavMesh.SamplePosition(randomDirection, out UnityEngine.AI.NavMeshHit hit, _waypointRadius, UnityEngine.AI.NavMesh.AllAreas))
            {
                _currentWaypoint = hit.position;
                MoveTo(_currentWaypoint, false);
            }
        }

        private void FleeFromThreat()
        {
            Vector3 threatPos = CurrentThreat != null ? CurrentThreat.position : _playerTransform.position;
            Vector3 fleeDirection = (transform.position - threatPos).normalized;
            Vector3 fleeTarget = transform.position + fleeDirection * 30f;

            if (UnityEngine.AI.NavMesh.SamplePosition(fleeTarget, out UnityEngine.AI.NavMeshHit hit, 30f, UnityEngine.AI.NavMesh.AllAreas))
            {
                MoveTo(hit.position, true);
            }
        }
    }

    // Placeholder states for the state machine
    public class CivilianWanderState : AIState { public override void Execute() { } }
    public class CivilianIdleState : AIState { public override void Execute() { } }
    public class CivilianFleeState : AIState { public override void Execute() { } }
}
