// ============================================================================
// AIStateMachine.cs — Finite State Machine for all AI agents
// ============================================================================
// PURPOSE:
//   The foundation for ALL AI in the game — civilians, police, gang members,
//   traffic. Every AI agent uses this state machine to decide what to do.
//
// HOW STATE MACHINES WORK:
//   An AI agent is always in exactly ONE state (e.g., Patrolling, Chasing, Fleeing).
//   Each state defines:
//   - What the AI DOES while in that state (Execute)
//   - What conditions cause a TRANSITION to another state
//   - What happens when ENTERING or LEAVING the state
//
// BEGINNER NOTE:
//   Think of it like a flowchart:
//   
//   [Idle] --sees enemy--> [Alert] --confirmed threat--> [Combat]
//     ^                                                      |
//     |_______________ enemy dies or escapes ________________|
//
//   The AI is always in ONE box. Arrows are transitions.
//   Each box has rules for what happens inside it.
//
// MOBILE OPTIMIZATION:
//   - States are reused (not instantiated each frame)
//   - Transitions checked on a timer (not every frame)
//   - Uses object references instead of string lookups
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace ShadowCity.AI
{
    /// <summary>
    /// Base class for all AI states. Extend this to create specific behaviors.
    /// </summary>
    public abstract class AIState
    {
        protected AIAgent Agent;

        public void Initialize(AIAgent agent)
        {
            Agent = agent;
        }

        /// <summary>Called once when entering this state.</summary>
        public virtual void Enter() { }

        /// <summary>Called every update tick while in this state.</summary>
        public abstract void Execute();

        /// <summary>Called once when leaving this state.</summary>
        public virtual void Exit() { }

        /// <summary>
        /// Check if this state should transition to another.
        /// Returns the new state type, or null to stay in current state.
        /// </summary>
        public virtual AIState CheckTransitions() { return null; }
    }

    /// <summary>
    /// The state machine controller. Attach this to any AI agent.
    /// </summary>
    public class AIStateMachine : MonoBehaviour
    {
        private AIState _currentState;
        private Dictionary<System.Type, AIState> _stateCache = new Dictionary<System.Type, AIState>();
        private AIAgent _agent;

        [Header("Performance")]
        [SerializeField] private float _transitionCheckInterval = 0.2f;
        private float _transitionTimer;

        public AIState CurrentState => _currentState;
        public string CurrentStateName => _currentState?.GetType().Name ?? "None";

        public void Initialize(AIAgent agent)
        {
            _agent = agent;
        }

        /// <summary>
        /// Register a state. States are cached and reused.
        /// </summary>
        public void RegisterState<T>(T state) where T : AIState
        {
            state.Initialize(_agent);
            _stateCache[typeof(T)] = state;
        }

        /// <summary>
        /// Force transition to a specific state type.
        /// </summary>
        public void TransitionTo<T>() where T : AIState
        {
            System.Type stateType = typeof(T);

            if (!_stateCache.ContainsKey(stateType))
            {
                Debug.LogError($"[AIStateMachine] State {stateType.Name} not registered!");
                return;
            }

            TransitionTo(_stateCache[stateType]);
        }

        public void TransitionTo(AIState newState)
        {
            if (newState == _currentState) return;

            _currentState?.Exit();
            _currentState = newState;
            _currentState.Enter();
        }

        private void Update()
        {
            if (_currentState == null) return;

            // Execute current state behavior
            _currentState.Execute();

            // Check transitions periodically (not every frame)
            _transitionTimer += Time.deltaTime;
            if (_transitionTimer >= _transitionCheckInterval)
            {
                _transitionTimer = 0f;
                AIState nextState = _currentState.CheckTransitions();
                if (nextState != null)
                {
                    TransitionTo(nextState);
                }
            }
        }
    }

    /// <summary>
    /// Base component for all AI agents. Provides shared functionality
    /// like perception, navigation, and health.
    /// </summary>
    [RequireComponent(typeof(AIStateMachine))]
    public class AIAgent : MonoBehaviour
    {
        [Header("Perception")]
        [SerializeField] private float _sightRange = 30f;
        [SerializeField] private float _sightAngle = 120f;
        [SerializeField] private float _hearingRange = 15f;
        [SerializeField] private LayerMask _sightBlockers;

        [Header("Navigation")]
        [SerializeField] private float _moveSpeed = 3f;
        [SerializeField] private float _runSpeed = 6f;
        [SerializeField] private float _rotationSpeed = 5f;

        // References
        private AIStateMachine _stateMachine;
        private UnityEngine.AI.NavMeshAgent _navAgent;
        private Animator _animator;

        // Perception cache — updated periodically, not every frame
        private Transform _currentThreat;
        private Vector3 _lastKnownThreatPosition;
        private float _perceptionTimer;
        private float _perceptionInterval = 0.3f;

        // Animation hashes
        private static readonly int AnimSpeed = Animator.StringToHash("Speed");
        private static readonly int AnimIsAlert = Animator.StringToHash("IsAlert");

        public AIStateMachine StateMachine => _stateMachine;
        public UnityEngine.AI.NavMeshAgent NavAgent => _navAgent;
        public Animator Anim => _animator;
        public Transform CurrentThreat => _currentThreat;
        public Vector3 LastKnownThreatPosition => _lastKnownThreatPosition;
        public float SightRange => _sightRange;
        public float MoveSpeed => _moveSpeed;
        public float RunSpeed => _runSpeed;

        protected virtual void Awake()
        {
            _stateMachine = GetComponent<AIStateMachine>();
            _navAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            _animator = GetComponent<Animator>();

            _stateMachine.Initialize(this);
        }

        protected virtual void Update()
        {
            // Periodic perception update
            _perceptionTimer += Time.deltaTime;
            if (_perceptionTimer >= _perceptionInterval)
            {
                _perceptionTimer = 0f;
                UpdatePerception();
            }

            // Update animation
            if (_animator != null && _navAgent != null)
            {
                float speed = _navAgent.velocity.magnitude / _runSpeed;
                _animator.SetFloat(AnimSpeed, speed, 0.1f, Time.deltaTime);
            }
        }

        // ====================================================================
        // PERCEPTION
        // ====================================================================

        /// <summary>
        /// Updates what the AI can see and hear. Called periodically.
        /// </summary>
        private void UpdatePerception()
        {
            _currentThreat = null;

            // Check if player is visible
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;

            Vector3 toPlayer = player.transform.position - transform.position;
            float distance = toPlayer.magnitude;

            // Range check
            if (distance > _sightRange) return;

            // Angle check (is player in front of us?)
            float angle = Vector3.Angle(transform.forward, toPlayer.normalized);
            if (angle > _sightAngle * 0.5f) return;

            // Line of sight check (is there a wall between us?)
            if (Physics.Raycast(transform.position + Vector3.up, toPlayer.normalized, distance, _sightBlockers))
                return;

            // Player is visible!
            _currentThreat = player.transform;
            _lastKnownThreatPosition = player.transform.position;
        }

        /// <summary>
        /// Check if AI can hear a sound at the given position.
        /// Used for gunshots, explosions, etc.
        /// </summary>
        public bool CanHearSound(Vector3 soundPosition, float soundRadius)
        {
            float distance = Vector3.Distance(transform.position, soundPosition);
            return distance <= Mathf.Min(_hearingRange, soundRadius);
        }

        // ====================================================================
        // NAVIGATION
        // ====================================================================

        public void MoveTo(Vector3 destination, bool run = false)
        {
            if (_navAgent == null || !_navAgent.isOnNavMesh) return;

            _navAgent.speed = run ? _runSpeed : _moveSpeed;
            _navAgent.SetDestination(destination);
        }

        public void StopMoving()
        {
            if (_navAgent != null && _navAgent.isOnNavMesh)
            {
                _navAgent.ResetPath();
            }
        }

        public bool HasReachedDestination()
        {
            if (_navAgent == null) return true;
            return !_navAgent.pathPending && _navAgent.remainingDistance <= _navAgent.stoppingDistance + 0.1f;
        }

        public void FaceTarget(Vector3 target)
        {
            Vector3 direction = (target - transform.position).normalized;
            direction.y = 0;
            if (direction.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, _rotationSpeed * Time.deltaTime);
            }
        }
    }
}
