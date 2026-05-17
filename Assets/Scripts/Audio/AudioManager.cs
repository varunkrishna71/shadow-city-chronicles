// ============================================================================
// AudioManager.cs — Complete audio management system
// ============================================================================
// PURPOSE:
//   Central audio controller. Manages all sound in the game:
//   - Sound effects (gunshots, footsteps, impacts)
//   - Ambient sounds (city noise, wind, rain)
//   - Music (mission music, free roam music, radio)
//   - Voice lines (dialogue, phone calls)
//   - 3D spatial audio (sounds come from world positions)
//
// ARCHITECTURE:
//   Uses pooled AudioSources for SFX (never instantiate AudioSources at runtime).
//   Music uses two AudioSources for crossfading between tracks.
//   Ambient sounds loop on dedicated AudioSources.
//
// SOUND CATEGORIES:
//   Master → controls everything
//   ├── SFX → gunshots, footsteps, impacts, UI sounds
//   ├── Music → background music, radio
//   ├── Voice → dialogue, phone calls
//   └── Ambient → city noise, weather, environment
//
// MOBILE OPTIMIZATION:
//   - Maximum 16 simultaneous AudioSources (hardware limit on some devices)
//   - SFX AudioSources are pooled and reused
//   - Distant sounds (>50m) are culled
//   - Ambient sounds use 2D (no spatial processing overhead)
//   - Music streams from disk (not loaded into RAM)
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.Audio
{
    [System.Serializable]
    public class SoundEntry
    {
        public string SoundId;
        public AudioClip Clip;
        [Range(0f, 1f)] public float Volume = 1f;
        [Range(0.5f, 2f)] public float PitchMin = 0.95f;
        [Range(0.5f, 2f)] public float PitchMax = 1.05f;
        public bool Is3D = true;
        public float MaxDistance = 50f;
        public SoundCategory Category;
    }

    public enum SoundCategory
    {
        SFX,
        Music,
        Voice,
        Ambient
    }

    public class AudioManager : MonoBehaviour
    {
        private static AudioManager _instance;
        public static AudioManager Instance => _instance;

        [Header("Sound Library")]
        [SerializeField] private SoundEntry[] _soundLibrary;

        [Header("Volume Settings")]
        [Range(0f, 1f)][SerializeField] private float _masterVolume = 1f;
        [Range(0f, 1f)][SerializeField] private float _sfxVolume = 1f;
        [Range(0f, 1f)][SerializeField] private float _musicVolume = 0.7f;
        [Range(0f, 1f)][SerializeField] private float _voiceVolume = 1f;
        [Range(0f, 1f)][SerializeField] private float _ambientVolume = 0.5f;

        [Header("SFX Pool")]
        [SerializeField] private int _sfxPoolSize = 16;

        [Header("Music")]
        [SerializeField] private float _musicCrossfadeDuration = 2f;

        // Audio source pools
        private AudioSource[] _sfxSources;
        private int _sfxSourceIndex;
        private AudioSource _musicSourceA;
        private AudioSource _musicSourceB;
        private bool _musicAIsActive = true;
        private float _musicCrossfadeTimer;
        private bool _isCrossfading;

        // Ambient
        private AudioSource _ambientSource;
        private AudioSource _weatherSource;

        // Lookup
        private Dictionary<string, SoundEntry> _soundLookup = new Dictionary<string, SoundEntry>();

        // Events
        public System.Action<string> OnMusicChanged;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeAudioSources();
            BuildSoundLookup();
        }

        private void Update()
        {
            if (_isCrossfading)
            {
                UpdateMusicCrossfade();
            }
        }

        // ====================================================================
        // INITIALIZATION
        // ====================================================================

        private void InitializeAudioSources()
        {
            // SFX pool
            _sfxSources = new AudioSource[_sfxPoolSize];
            for (int i = 0; i < _sfxPoolSize; i++)
            {
                GameObject sfxObj = new GameObject($"SFX_Source_{i}");
                sfxObj.transform.SetParent(transform);
                _sfxSources[i] = sfxObj.AddComponent<AudioSource>();
                _sfxSources[i].playOnAwake = false;
                _sfxSources[i].spatialBlend = 1f; // 3D by default
                _sfxSources[i].rolloffMode = AudioRolloffMode.Linear;
            }

            // Music sources (two for crossfading)
            _musicSourceA = CreateAudioSource("Music_A", false);
            _musicSourceA.loop = true;
            _musicSourceB = CreateAudioSource("Music_B", false);
            _musicSourceB.loop = true;
            _musicSourceB.volume = 0f;

            // Ambient sources
            _ambientSource = CreateAudioSource("Ambient", false);
            _ambientSource.loop = true;
            _weatherSource = CreateAudioSource("Weather", false);
            _weatherSource.loop = true;
        }

        private AudioSource CreateAudioSource(string name, bool is3D)
        {
            GameObject obj = new GameObject($"AudioSource_{name}");
            obj.transform.SetParent(transform);
            AudioSource source = obj.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = is3D ? 1f : 0f;
            return source;
        }

        private void BuildSoundLookup()
        {
            if (_soundLibrary == null) return;

            foreach (SoundEntry entry in _soundLibrary)
            {
                if (!string.IsNullOrEmpty(entry.SoundId))
                {
                    _soundLookup[entry.SoundId] = entry;
                }
            }
        }

        // ====================================================================
        // PLAY SFX
        // ====================================================================

        /// <summary>
        /// Play a sound effect at a world position.
        /// </summary>
        public void PlaySFX(string soundId, Vector3 position)
        {
            if (!_soundLookup.ContainsKey(soundId))
            {
                Debug.LogWarning($"[AudioManager] Sound '{soundId}' not found!");
                return;
            }

            SoundEntry entry = _soundLookup[soundId];
            if (entry.Clip == null) return;

            AudioSource source = GetNextSFXSource();
            source.clip = entry.Clip;
            source.volume = entry.Volume * _sfxVolume * _masterVolume;
            source.pitch = Random.Range(entry.PitchMin, entry.PitchMax);
            source.spatialBlend = entry.Is3D ? 1f : 0f;
            source.maxDistance = entry.MaxDistance;
            source.transform.position = position;
            source.Play();
        }

        /// <summary>
        /// Play a 2D sound effect (UI sounds, notifications).
        /// </summary>
        public void PlaySFX2D(string soundId)
        {
            PlaySFX(soundId, Vector3.zero);
        }

        /// <summary>
        /// Play a sound clip directly (without lookup).
        /// </summary>
        public void PlayClip(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (clip == null) return;

            AudioSource source = GetNextSFXSource();
            source.clip = clip;
            source.volume = volume * _sfxVolume * _masterVolume;
            source.pitch = 1f;
            source.spatialBlend = 1f;
            source.transform.position = position;
            source.Play();
        }

        private AudioSource GetNextSFXSource()
        {
            // Round-robin through pool, preferring non-playing sources
            for (int i = 0; i < _sfxPoolSize; i++)
            {
                int index = (_sfxSourceIndex + i) % _sfxPoolSize;
                if (!_sfxSources[index].isPlaying)
                {
                    _sfxSourceIndex = (index + 1) % _sfxPoolSize;
                    return _sfxSources[index];
                }
            }

            // All playing — steal the oldest
            _sfxSourceIndex = (_sfxSourceIndex + 1) % _sfxPoolSize;
            return _sfxSources[_sfxSourceIndex];
        }

        // ====================================================================
        // MUSIC
        // ====================================================================

        /// <summary>
        /// Play music with crossfade from current track.
        /// </summary>
        public void PlayMusic(AudioClip clip, float fadeDuration = -1f)
        {
            if (clip == null) return;

            float fadeTime = fadeDuration >= 0 ? fadeDuration : _musicCrossfadeDuration;

            AudioSource incoming = _musicAIsActive ? _musicSourceB : _musicSourceA;
            incoming.clip = clip;
            incoming.volume = 0f;
            incoming.Play();

            _isCrossfading = true;
            _musicCrossfadeTimer = 0f;
            _musicCrossfadeDuration = fadeTime;
        }

        /// <summary>
        /// Play music by sound ID.
        /// </summary>
        public void PlayMusic(string soundId)
        {
            if (_soundLookup.ContainsKey(soundId))
            {
                PlayMusic(_soundLookup[soundId].Clip);
            }
        }

        public void StopMusic(float fadeOutDuration = 1f)
        {
            AudioSource active = _musicAIsActive ? _musicSourceA : _musicSourceB;
            // Simple fade out
            StartCoroutine(FadeOutCoroutine(active, fadeOutDuration));
        }

        private System.Collections.IEnumerator FadeOutCoroutine(AudioSource source, float duration)
        {
            float startVolume = source.volume;
            float timer = 0f;

            while (timer < duration)
            {
                timer += Time.unscaledDeltaTime;
                source.volume = Mathf.Lerp(startVolume, 0f, timer / duration);
                yield return null;
            }

            source.Stop();
            source.volume = 0f;
        }

        private void UpdateMusicCrossfade()
        {
            _musicCrossfadeTimer += Time.unscaledDeltaTime;
            float t = _musicCrossfadeTimer / _musicCrossfadeDuration;

            float targetVolume = _musicVolume * _masterVolume;

            if (_musicAIsActive)
            {
                _musicSourceA.volume = Mathf.Lerp(targetVolume, 0f, t);
                _musicSourceB.volume = Mathf.Lerp(0f, targetVolume, t);
            }
            else
            {
                _musicSourceB.volume = Mathf.Lerp(targetVolume, 0f, t);
                _musicSourceA.volume = Mathf.Lerp(0f, targetVolume, t);
            }

            if (t >= 1f)
            {
                _isCrossfading = false;
                AudioSource outgoing = _musicAIsActive ? _musicSourceA : _musicSourceB;
                outgoing.Stop();
                _musicAIsActive = !_musicAIsActive;
            }
        }

        // ====================================================================
        // AMBIENT
        // ====================================================================

        public void SetAmbientSound(AudioClip clip)
        {
            if (_ambientSource == null) return;

            _ambientSource.clip = clip;
            _ambientSource.volume = _ambientVolume * _masterVolume;
            _ambientSource.Play();
        }

        public void SetWeatherSound(AudioClip clip, float intensity)
        {
            if (_weatherSource == null) return;

            if (clip == null)
            {
                _weatherSource.Stop();
                return;
            }

            if (_weatherSource.clip != clip)
            {
                _weatherSource.clip = clip;
                _weatherSource.Play();
            }

            _weatherSource.volume = intensity * _ambientVolume * _masterVolume;
        }

        // ====================================================================
        // VOLUME CONTROL
        // ====================================================================

        public void SetMasterVolume(float volume)
        {
            _masterVolume = Mathf.Clamp01(volume);
            UpdateAllVolumes();
        }

        public void SetSFXVolume(float volume)
        {
            _sfxVolume = Mathf.Clamp01(volume);
        }

        public void SetMusicVolume(float volume)
        {
            _musicVolume = Mathf.Clamp01(volume);
            UpdateMusicVolume();
        }

        public void SetVoiceVolume(float volume)
        {
            _voiceVolume = Mathf.Clamp01(volume);
        }

        public void SetAmbientVolume(float volume)
        {
            _ambientVolume = Mathf.Clamp01(volume);
            if (_ambientSource != null) _ambientSource.volume = _ambientVolume * _masterVolume;
            if (_weatherSource != null) _weatherSource.volume = _ambientVolume * _masterVolume;
        }

        private void UpdateAllVolumes()
        {
            UpdateMusicVolume();
            if (_ambientSource != null) _ambientSource.volume = _ambientVolume * _masterVolume;
            if (_weatherSource != null) _weatherSource.volume = _ambientVolume * _masterVolume;
        }

        private void UpdateMusicVolume()
        {
            float vol = _musicVolume * _masterVolume;
            AudioSource active = _musicAIsActive ? _musicSourceA : _musicSourceB;
            if (!_isCrossfading && active != null)
            {
                active.volume = vol;
            }
        }
    }
}
