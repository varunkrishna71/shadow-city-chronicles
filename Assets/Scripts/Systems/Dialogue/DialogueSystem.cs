// ============================================================================
// DialogueSystem.cs — Branching dialogue and conversation system
// ============================================================================
// PURPOSE:
//   Handles all in-game dialogue — NPC conversations, mission briefings,
//   phone calls, and player choice dialogues. Supports branching paths
//   where player choices affect the story.
//
// ARCHITECTURE:
//   Dialogues are defined as ScriptableObjects containing a tree of nodes.
//   Each node is either:
//   - Text node: NPC says something, player taps to continue
//   - Choice node: Player picks from 2-4 options
//   - Event node: Triggers a game event (start mission, give item, etc.)
//
// MOBILE UX:
//   - Tap anywhere to advance text
//   - Choice buttons are large and thumb-friendly
//   - Text types out letter-by-letter (can tap to skip)
//   - Phone call dialogues show a different UI (phone overlay)
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.Systems.Dialogue
{
    [CreateAssetMenu(fileName = "NewDialogue", menuName = "ShadowCity/Dialogue")]
    public class DialogueData : ScriptableObject
    {
        public string DialogueId;
        public DialogueNode[] Nodes;
    }

    [System.Serializable]
    public class DialogueNode
    {
        public string NodeId;
        public DialogueNodeType Type;

        [Header("Speaker")]
        public string SpeakerName;
        public Sprite SpeakerPortrait;

        [Header("Text")]
        [TextArea(2, 5)]
        public string Text;
        public float TextSpeed = 30f;          // Characters per second

        [Header("Choices (for Choice nodes)")]
        public DialogueChoice[] Choices;

        [Header("Navigation")]
        public string NextNodeId;              // For linear progression

        [Header("Events")]
        public string TriggerEventId;          // Game event to trigger
        public string RequiredFlag;            // Only show if this flag is set
        public string SetFlag;                 // Set this flag when reached

        [Header("Audio")]
        public AudioClip VoiceLine;

        [Header("Camera")]
        public bool UseCinematicCamera;
        public Vector3 CameraPosition;
        public Vector3 CameraLookAt;
    }

    public enum DialogueNodeType
    {
        Text,       // NPC speaks, player taps to continue
        Choice,     // Player picks an option
        Event,      // Triggers a game event
        End         // Conversation over
    }

    [System.Serializable]
    public class DialogueChoice
    {
        [TextArea(1, 2)]
        public string ChoiceText;
        public string NextNodeId;
        public string RequiredFlag;    // Only show if flag set
        public string SetFlag;         // Set flag when chosen
        public int ReputationEffect;   // +/- reputation with faction
    }

    public class DialogueSystem : MonoBehaviour
    {
        private static DialogueSystem _instance;
        public static DialogueSystem Instance => _instance;

        // Current dialogue state
        private DialogueData _currentDialogue;
        private DialogueNode _currentNode;
        private Dictionary<string, DialogueNode> _nodeLookup;
        private bool _isActive;
        private bool _isTyping;
        private string _displayedText;
        private float _typeTimer;
        private int _charIndex;

        // Story flags — track player decisions across the game
        private HashSet<string> _storyFlags = new HashSet<string>();

        // Events
        public System.Action<DialogueNode> OnNodeStarted;
        public System.Action<string> OnTextUpdated;          // Partial text during typewriter
        public System.Action<DialogueChoice[]> OnChoicesShown;
        public System.Action OnDialogueEnded;
        public System.Action<string> OnEventTriggered;

        public bool IsActive => _isActive;
        public DialogueNode CurrentNode => _currentNode;

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
            if (!_isActive || !_isTyping) return;

            UpdateTypewriter();
        }

        // ====================================================================
        // DIALOGUE LIFECYCLE
        // ====================================================================

        /// <summary>
        /// Start a dialogue conversation.
        /// </summary>
        public void StartDialogue(DialogueData dialogue)
        {
            if (dialogue == null || dialogue.Nodes == null || dialogue.Nodes.Length == 0)
            {
                Debug.LogError("[DialogueSystem] Invalid dialogue data!");
                return;
            }

            _currentDialogue = dialogue;
            _isActive = true;

            // Build node lookup for fast access
            _nodeLookup = new Dictionary<string, DialogueNode>();
            foreach (DialogueNode node in dialogue.Nodes)
            {
                _nodeLookup[node.NodeId] = node;
            }

            // Set game state
            GameManager.Instance?.SetGameState(GameState.Dialogue);

            EventBus.Publish(new DialogueStartedEvent
            {
                DialogueId = dialogue.DialogueId,
                SpeakerName = dialogue.Nodes[0].SpeakerName
            });

            // Start from first node
            GoToNode(dialogue.Nodes[0].NodeId);
        }

        /// <summary>
        /// End the current dialogue.
        /// </summary>
        public void EndDialogue()
        {
            _isActive = false;
            _currentDialogue = null;
            _currentNode = null;
            _isTyping = false;

            GameManager.Instance?.ReturnToPreviousState();

            EventBus.Publish(new DialogueEndedEvent
            {
                DialogueId = _currentDialogue?.DialogueId ?? ""
            });

            OnDialogueEnded?.Invoke();
        }

        // ====================================================================
        // NODE NAVIGATION
        // ====================================================================

        private void GoToNode(string nodeId)
        {
            if (!_nodeLookup.ContainsKey(nodeId))
            {
                Debug.LogError($"[DialogueSystem] Node '{nodeId}' not found!");
                EndDialogue();
                return;
            }

            _currentNode = _nodeLookup[nodeId];

            // Check required flag
            if (!string.IsNullOrEmpty(_currentNode.RequiredFlag) && !HasFlag(_currentNode.RequiredFlag))
            {
                // Skip this node — go to next
                if (!string.IsNullOrEmpty(_currentNode.NextNodeId))
                {
                    GoToNode(_currentNode.NextNodeId);
                }
                else
                {
                    EndDialogue();
                }
                return;
            }

            // Set flag if specified
            if (!string.IsNullOrEmpty(_currentNode.SetFlag))
            {
                SetFlag(_currentNode.SetFlag);
            }

            // Process node based on type
            switch (_currentNode.Type)
            {
                case DialogueNodeType.Text:
                    StartTypewriter(_currentNode.Text);
                    OnNodeStarted?.Invoke(_currentNode);
                    break;

                case DialogueNodeType.Choice:
                    ShowChoices();
                    OnNodeStarted?.Invoke(_currentNode);
                    break;

                case DialogueNodeType.Event:
                    if (!string.IsNullOrEmpty(_currentNode.TriggerEventId))
                    {
                        OnEventTriggered?.Invoke(_currentNode.TriggerEventId);
                    }
                    if (!string.IsNullOrEmpty(_currentNode.NextNodeId))
                    {
                        GoToNode(_currentNode.NextNodeId);
                    }
                    else
                    {
                        EndDialogue();
                    }
                    break;

                case DialogueNodeType.End:
                    EndDialogue();
                    break;
            }

            // Play voice line if available
            if (_currentNode.VoiceLine != null)
            {
                AudioSource.PlayClipAtPoint(_currentNode.VoiceLine, UnityEngine.Camera.main.transform.position);
            }
        }

        // ====================================================================
        // TYPEWRITER EFFECT
        // ====================================================================

        private void StartTypewriter(string fullText)
        {
            _displayedText = "";
            _charIndex = 0;
            _isTyping = true;
            _typeTimer = 0f;
        }

        private void UpdateTypewriter()
        {
            if (_currentNode == null) return;

            _typeTimer += Time.unscaledDeltaTime;
            float charInterval = 1f / _currentNode.TextSpeed;

            while (_typeTimer >= charInterval && _charIndex < _currentNode.Text.Length)
            {
                _displayedText += _currentNode.Text[_charIndex];
                _charIndex++;
                _typeTimer -= charInterval;

                OnTextUpdated?.Invoke(_displayedText);
            }

            if (_charIndex >= _currentNode.Text.Length)
            {
                _isTyping = false;
            }
        }

        /// <summary>
        /// Called when player taps the screen during dialogue.
        /// </summary>
        public void OnTap()
        {
            if (!_isActive) return;

            if (_isTyping)
            {
                // Skip typewriter — show full text immediately
                _displayedText = _currentNode.Text;
                _isTyping = false;
                OnTextUpdated?.Invoke(_displayedText);
                return;
            }

            if (_currentNode.Type == DialogueNodeType.Text)
            {
                // Advance to next node
                if (!string.IsNullOrEmpty(_currentNode.NextNodeId))
                {
                    GoToNode(_currentNode.NextNodeId);
                }
                else
                {
                    EndDialogue();
                }
            }
        }

        /// <summary>
        /// Called when player selects a dialogue choice.
        /// </summary>
        public void SelectChoice(int choiceIndex)
        {
            if (_currentNode.Type != DialogueNodeType.Choice) return;
            if (choiceIndex < 0 || choiceIndex >= _currentNode.Choices.Length) return;

            DialogueChoice choice = _currentNode.Choices[choiceIndex];

            // Set flag
            if (!string.IsNullOrEmpty(choice.SetFlag))
            {
                SetFlag(choice.SetFlag);
            }

            // Navigate to chosen path
            if (!string.IsNullOrEmpty(choice.NextNodeId))
            {
                GoToNode(choice.NextNodeId);
            }
            else
            {
                EndDialogue();
            }
        }

        // ====================================================================
        // CHOICES
        // ====================================================================

        private void ShowChoices()
        {
            if (_currentNode.Choices == null) return;

            // Filter choices based on required flags
            List<DialogueChoice> available = new List<DialogueChoice>();
            foreach (DialogueChoice choice in _currentNode.Choices)
            {
                if (string.IsNullOrEmpty(choice.RequiredFlag) || HasFlag(choice.RequiredFlag))
                {
                    available.Add(choice);
                }
            }

            // Show text first, then choices
            if (!string.IsNullOrEmpty(_currentNode.Text))
            {
                StartTypewriter(_currentNode.Text);
            }

            OnChoicesShown?.Invoke(available.ToArray());
        }

        // ====================================================================
        // STORY FLAGS
        // ====================================================================

        public void SetFlag(string flag)
        {
            _storyFlags.Add(flag);
            Debug.Log($"[DialogueSystem] Flag set: {flag}");
        }

        public bool HasFlag(string flag)
        {
            return _storyFlags.Contains(flag);
        }

        public void ClearFlag(string flag)
        {
            _storyFlags.Remove(flag);
        }

        public HashSet<string> GetAllFlags()
        {
            return new HashSet<string>(_storyFlags);
        }

        public void SetFlags(HashSet<string> flags)
        {
            _storyFlags = new HashSet<string>(flags);
        }
    }
}
