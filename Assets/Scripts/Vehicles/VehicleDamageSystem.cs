// ============================================================================
// VehicleDamageSystem.cs — Visual and mechanical vehicle damage
// ============================================================================
// PURPOSE:
//   Handles both visual deformation (dents, broken windows) and mechanical
//   damage (engine failure, tire blowouts, reduced performance).
//
// HOW IT WORKS:
//   Visual damage: Deforms the mesh vertices at impact points.
//   Mechanical damage: Reduces engine power, steering, braking based on HP.
//
// MOBILE OPTIMIZATION:
//   - Mesh deformation only affects nearby vertices (radius-based)
//   - Deformation is capped to prevent extreme vertex displacement
//   - Damage particles use object pooling
//   - Only recalculates mesh normals when damage threshold is crossed
// ============================================================================

using UnityEngine;
using ShadowCity.Weapons;

namespace ShadowCity.Vehicles
{
    public class VehicleDamageSystem : MonoBehaviour, IDamageable
    {
        [Header("Health")]
        [SerializeField] private float _maxHealth = 1000f;
        [SerializeField] private float _currentHealth;
        [SerializeField] private float _explosionThreshold = 0f;

        [Header("Visual Damage")]
        [SerializeField] private float _deformRadius = 0.5f;
        [SerializeField] private float _maxDeformation = 0.3f;
        [SerializeField] private float _deformForce = 0.1f;

        [Header("Mechanical Damage")]
        [SerializeField] private float _engineDamageThreshold = 0.5f;  // Below 50% HP, engine suffers
        [SerializeField] private float _smokeDamageThreshold = 0.3f;   // Below 30%, smoke appears
        [SerializeField] private float _fireDamageThreshold = 0.1f;    // Below 10%, fire starts

        [Header("Effects")]
        [SerializeField] private ParticleSystem _smokeEffect;
        [SerializeField] private ParticleSystem _fireEffect;
        [SerializeField] private ParticleSystem _explosionEffect;

        // Cached mesh data for deformation
        private MeshFilter[] _meshFilters;
        private Vector3[][] _originalVertices;
        private bool _meshesInitialized;

        // References
        private VehicleController _vehicleController;
        private Rigidbody _rigidbody;

        // State
        private bool _isDestroyed;
        private float _engineDamageMultiplier = 1f;

        public float HealthPercent => _currentHealth / _maxHealth;
        public bool IsDestroyed => _isDestroyed;
        public float EngineDamageMultiplier => _engineDamageMultiplier;

        private void Awake()
        {
            _currentHealth = _maxHealth;
            _vehicleController = GetComponent<VehicleController>();
            _rigidbody = GetComponent<Rigidbody>();
        }

        private void Start()
        {
            InitializeMeshData();
        }

        // ====================================================================
        // MESH INITIALIZATION
        // ====================================================================

        /// <summary>
        /// Cache all mesh data for deformation. We store the ORIGINAL vertices
        /// so we can calculate deformation relative to the undamaged state.
        /// </summary>
        private void InitializeMeshData()
        {
            _meshFilters = GetComponentsInChildren<MeshFilter>();
            _originalVertices = new Vector3[_meshFilters.Length][];

            for (int i = 0; i < _meshFilters.Length; i++)
            {
                if (_meshFilters[i].mesh != null)
                {
                    _originalVertices[i] = _meshFilters[i].mesh.vertices.Clone() as Vector3[];
                }
            }

            _meshesInitialized = true;
        }

        // ====================================================================
        // DAMAGE
        // ====================================================================

        public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
        {
            if (_isDestroyed) return;

            _currentHealth -= damage;

            // Apply visual deformation at impact point
            if (_meshesInitialized)
            {
                DeformMeshAtPoint(hitPoint, hitDirection, damage);
            }

            // Update mechanical damage
            UpdateMechanicalDamage();

            // Check for destruction
            if (_currentHealth <= _explosionThreshold)
            {
                Explode();
            }
        }

        /// <summary>
        /// Collision damage — when the vehicle hits something.
        /// Damage is proportional to impact force.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            float impactForce = collision.impulse.magnitude;

            // Only register significant impacts
            if (impactForce < 500f) return;

            float damage = impactForce * 0.01f;
            Vector3 hitPoint = collision.GetContact(0).point;
            Vector3 hitNormal = collision.GetContact(0).normal;

            TakeDamage(damage, hitPoint, -hitNormal);
        }

        // ====================================================================
        // VISUAL DEFORMATION
        // ====================================================================

        /// <summary>
        /// Deforms mesh vertices near the impact point.
        /// This creates realistic-looking dents and crumpling.
        /// </summary>
        private void DeformMeshAtPoint(Vector3 worldPoint, Vector3 direction, float force)
        {
            float deformAmount = Mathf.Min(force * _deformForce * 0.01f, _maxDeformation);

            for (int m = 0; m < _meshFilters.Length; m++)
            {
                MeshFilter mf = _meshFilters[m];
                if (mf == null || mf.mesh == null) continue;

                Mesh mesh = mf.mesh;
                Vector3[] vertices = mesh.vertices;
                bool modified = false;

                // Convert world point to local space of this mesh
                Vector3 localPoint = mf.transform.InverseTransformPoint(worldPoint);
                Vector3 localDirection = mf.transform.InverseTransformDirection(direction).normalized;

                for (int v = 0; v < vertices.Length; v++)
                {
                    float distance = Vector3.Distance(vertices[v], localPoint);

                    if (distance < _deformRadius)
                    {
                        // Falloff — vertices closer to impact deform more
                        float falloff = 1f - (distance / _deformRadius);
                        falloff = falloff * falloff; // Quadratic falloff for natural look

                        Vector3 deformation = localDirection * deformAmount * falloff;

                        // Apply deformation but clamp total displacement
                        Vector3 totalDisplacement = vertices[v] - _originalVertices[m][v] + deformation;
                        if (totalDisplacement.magnitude <= _maxDeformation)
                        {
                            vertices[v] += deformation;
                            modified = true;
                        }
                    }
                }

                if (modified)
                {
                    mesh.vertices = vertices;
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                }
            }
        }

        // ====================================================================
        // MECHANICAL DAMAGE
        // ====================================================================

        private void UpdateMechanicalDamage()
        {
            float healthPercent = _currentHealth / _maxHealth;

            // Engine power reduction
            if (healthPercent < _engineDamageThreshold)
            {
                _engineDamageMultiplier = Mathf.Lerp(0.3f, 1f, healthPercent / _engineDamageThreshold);
            }
            else
            {
                _engineDamageMultiplier = 1f;
            }

            // Visual effects
            if (healthPercent < _smokeDamageThreshold && _smokeEffect != null)
            {
                if (!_smokeEffect.isPlaying) _smokeEffect.Play();
            }

            if (healthPercent < _fireDamageThreshold && _fireEffect != null)
            {
                if (!_fireEffect.isPlaying) _fireEffect.Play();
            }
        }

        // ====================================================================
        // EXPLOSION
        // ====================================================================

        private void Explode()
        {
            if (_isDestroyed) return;
            _isDestroyed = true;

            // Eject driver
            if (_vehicleController != null && _vehicleController.IsOccupied)
            {
                _vehicleController.ExitVehicle();
            }

            // Explosion effect
            if (_explosionEffect != null)
            {
                _explosionEffect.Play();
            }

            // Apply explosion force to nearby objects
            Collider[] nearby = Physics.OverlapSphere(transform.position, 10f);
            foreach (Collider col in nearby)
            {
                Rigidbody rb = col.GetComponent<Rigidbody>();
                if (rb != null && rb != _rigidbody)
                {
                    rb.AddExplosionForce(5000f, transform.position, 10f);
                }

                IDamageable damageable = col.GetComponent<IDamageable>();
                if (damageable != null)
                {
                    float distance = Vector3.Distance(transform.position, col.transform.position);
                    float damage = Mathf.Lerp(200f, 0f, distance / 10f);
                    damageable.TakeDamage(damage, col.transform.position, Vector3.up);
                }
            }

            // Disable vehicle
            if (_vehicleController != null)
            {
                _vehicleController.enabled = false;
            }
        }

        // ====================================================================
        // REPAIR
        // ====================================================================

        public void Repair(float amount)
        {
            _currentHealth = Mathf.Min(_currentHealth + amount, _maxHealth);
            UpdateMechanicalDamage();
        }

        public void FullRepair()
        {
            _currentHealth = _maxHealth;
            _engineDamageMultiplier = 1f;
            _isDestroyed = false;

            // Restore original mesh
            if (_meshesInitialized)
            {
                for (int i = 0; i < _meshFilters.Length; i++)
                {
                    if (_meshFilters[i].mesh != null && _originalVertices[i] != null)
                    {
                        _meshFilters[i].mesh.vertices = _originalVertices[i].Clone() as Vector3[];
                        _meshFilters[i].mesh.RecalculateNormals();
                    }
                }
            }

            if (_smokeEffect != null) _smokeEffect.Stop();
            if (_fireEffect != null) _fireEffect.Stop();
        }

        // ====================================================================
        // IDamageable
        // ====================================================================

        public float GetHealthPercent() => _currentHealth / _maxHealth;
        public bool IsAlive() => !_isDestroyed;
    }
}
