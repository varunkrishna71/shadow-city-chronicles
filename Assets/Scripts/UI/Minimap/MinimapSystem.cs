// ============================================================================
// MinimapSystem.cs — Real-time minimap rendering system
// ============================================================================
// PURPOSE:
//   Renders a top-down minimap showing the player's position, nearby NPCs,
//   mission waypoints, and points of interest. Uses a separate overhead
//   camera rendering to a RenderTexture.
//
// MOBILE OPTIMIZATION:
//   - Minimap camera renders at low resolution (256x256)
//   - Only renders specific layers (minimap-relevant objects)
//   - Updates every 3rd frame (not every frame)
//   - Icons are UI sprites (not 3D objects in the minimap view)
//   - Culling mask excludes particle effects and detailed geometry
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowCity.UI.Minimap
{
    public class MinimapSystem : MonoBehaviour
    {
        private static MinimapSystem _instance;
        public static MinimapSystem Instance => _instance;

        [Header("Camera")]
        [SerializeField] private Camera _minimapCamera;
        [SerializeField] private float _cameraHeight = 100f;
        [SerializeField] private float _defaultZoom = 80f;
        [SerializeField] private float _drivingZoom = 120f;
        [SerializeField] private float _zoomSpeed = 5f;

        [Header("Display")]
        [SerializeField] private RawImage _minimapDisplay;
        [SerializeField] private RenderTexture _renderTexture;
        [SerializeField] private int _textureSize = 256;

        [Header("Player Icon")]
        [SerializeField] private RectTransform _playerIcon;
        [SerializeField] private bool _rotateMap = true; // True = map rotates, player stays up

        [Header("Waypoint")]
        [SerializeField] private RectTransform _waypointIcon;
        [SerializeField] private float _waypointIconEdgeMargin = 20f;

        [Header("Icons")]
        [SerializeField] private GameObject _iconPrefab;
        [SerializeField] private Transform _iconContainer;
        [SerializeField] private float _iconRadius = 100f; // Only show icons within this radius

        [Header("Performance")]
        [SerializeField] private int _updateFrameInterval = 3;

        // State
        private Transform _playerTransform;
        private float _currentZoom;
        private float _targetZoom;
        private Vector3 _waypoint;
        private bool _hasWaypoint;
        private int _frameCounter;

        // Icon tracking
        private List<MinimapIcon> _activeIcons = new List<MinimapIcon>();

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            SetupRenderTexture();
        }

        private void Start()
        {
            _currentZoom = _defaultZoom;
            _targetZoom = _defaultZoom;
        }

        private void LateUpdate()
        {
            _frameCounter++;
            if (_frameCounter < _updateFrameInterval) return;
            _frameCounter = 0;

            if (_playerTransform == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) _playerTransform = player.transform;
                else return;
            }

            UpdateCameraPosition();
            UpdatePlayerIcon();
            UpdateWaypointIcon();
            UpdateZoom();
        }

        // ====================================================================
        // SETUP
        // ====================================================================

        private void SetupRenderTexture()
        {
            if (_renderTexture == null)
            {
                _renderTexture = new RenderTexture(_textureSize, _textureSize, 16);
                _renderTexture.antiAliasing = 1;
            }

            if (_minimapCamera != null)
            {
                _minimapCamera.targetTexture = _renderTexture;
            }

            if (_minimapDisplay != null)
            {
                _minimapDisplay.texture = _renderTexture;
            }
        }

        // ====================================================================
        // CAMERA
        // ====================================================================

        private void UpdateCameraPosition()
        {
            if (_minimapCamera == null) return;

            Vector3 camPos = _playerTransform.position;
            camPos.y = _cameraHeight;
            _minimapCamera.transform.position = camPos;

            if (_rotateMap)
            {
                // Map rotates to match player heading
                _minimapCamera.transform.rotation = Quaternion.Euler(
                    90f,
                    _playerTransform.eulerAngles.y,
                    0f
                );
            }
            else
            {
                // North-up minimap
                _minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }

            _minimapCamera.orthographicSize = _currentZoom;
        }

        // ====================================================================
        // ICONS
        // ====================================================================

        private void UpdatePlayerIcon()
        {
            if (_playerIcon == null) return;

            if (_rotateMap)
            {
                _playerIcon.rotation = Quaternion.identity; // Player always points up
            }
            else
            {
                _playerIcon.rotation = Quaternion.Euler(0, 0, -_playerTransform.eulerAngles.y);
            }
        }

        private void UpdateWaypointIcon()
        {
            if (_waypointIcon == null || !_hasWaypoint) return;

            _waypointIcon.gameObject.SetActive(true);

            // Calculate waypoint position relative to minimap
            Vector3 direction = _waypoint - _playerTransform.position;
            float distance = new Vector2(direction.x, direction.z).magnitude;

            if (distance < _currentZoom)
            {
                // Waypoint is on the minimap — show at correct position
                float normalizedX = direction.x / (_currentZoom * 2f);
                float normalizedZ = direction.z / (_currentZoom * 2f);

                if (_rotateMap)
                {
                    float angle = -_playerTransform.eulerAngles.y * Mathf.Deg2Rad;
                    float rotX = normalizedX * Mathf.Cos(angle) - normalizedZ * Mathf.Sin(angle);
                    float rotZ = normalizedX * Mathf.Sin(angle) + normalizedZ * Mathf.Cos(angle);
                    normalizedX = rotX;
                    normalizedZ = rotZ;
                }

                float halfSize = _minimapDisplay.rectTransform.rect.width * 0.5f;
                _waypointIcon.localPosition = new Vector3(
                    normalizedX * halfSize,
                    normalizedZ * halfSize,
                    0f
                );
            }
            else
            {
                // Waypoint is off-screen — clamp to edge
                Vector2 dir2D = new Vector2(direction.x, direction.z).normalized;

                if (_rotateMap)
                {
                    float angle = -_playerTransform.eulerAngles.y * Mathf.Deg2Rad;
                    float rotX = dir2D.x * Mathf.Cos(angle) - dir2D.y * Mathf.Sin(angle);
                    float rotY = dir2D.x * Mathf.Sin(angle) + dir2D.y * Mathf.Cos(angle);
                    dir2D = new Vector2(rotX, rotY);
                }

                float halfSize = _minimapDisplay.rectTransform.rect.width * 0.5f - _waypointIconEdgeMargin;
                _waypointIcon.localPosition = new Vector3(
                    dir2D.x * halfSize,
                    dir2D.y * halfSize,
                    0f
                );
            }
        }

        // ====================================================================
        // ZOOM
        // ====================================================================

        private void UpdateZoom()
        {
            _currentZoom = Mathf.Lerp(_currentZoom, _targetZoom, _zoomSpeed * Time.deltaTime);
        }

        public void SetDrivingMode(bool driving)
        {
            _targetZoom = driving ? _drivingZoom : _defaultZoom;
        }

        // ====================================================================
        // WAYPOINTS
        // ====================================================================

        public void SetWaypoint(Vector3 position)
        {
            _waypoint = position;
            _hasWaypoint = true;

            if (_waypointIcon != null)
            {
                _waypointIcon.gameObject.SetActive(true);
            }
        }

        public void ClearWaypoint()
        {
            _hasWaypoint = false;

            if (_waypointIcon != null)
            {
                _waypointIcon.gameObject.SetActive(false);
            }
        }
    }

    public struct MinimapIcon
    {
        public Transform Target;
        public RectTransform UIIcon;
        public MinimapIconType Type;
    }

    public enum MinimapIconType
    {
        Mission,
        Shop,
        SafeHouse,
        Police,
        Gang,
        Collectible
    }
}
