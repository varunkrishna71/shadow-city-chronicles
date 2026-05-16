// ============================================================================
// EventBus.cs — Decoupled event communication system
// ============================================================================
// PURPOSE:
//   Allows any script to send messages to any other script WITHOUT needing
//   a direct reference. This prevents "spaghetti code" where everything
//   knows about everything else.
//
// HOW IT WORKS:
//   1. Define an event struct (e.g., PlayerDamagedEvent)
//   2. Any script can SUBSCRIBE to that event type
//   3. Any script can PUBLISH that event
//   4. All subscribers receive the event data
//
// BEGINNER NOTE:
//   Think of it like a radio station. Publishers broadcast on a frequency,
//   and subscribers tune in to that frequency. They don't need to know
//   each other — they just need to agree on the frequency (event type).
//
// EXAMPLE:
//   // In HealthSystem.cs — Subscribe to damage events:
//   EventBus.Subscribe<PlayerDamagedEvent>(OnPlayerDamaged);
//
//   // In WeaponSystem.cs — When enemy shoots player:
//   EventBus.Publish(new PlayerDamagedEvent { Damage = 25f, Source = "Pistol" });
//
//   // HealthSystem automatically receives this and reduces HP
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShadowCity.Core
{
    public static class EventBus
    {
        // Dictionary mapping event types to their subscriber lists
        private static readonly Dictionary<Type, List<Delegate>> _subscribers
            = new Dictionary<Type, List<Delegate>>();

        /// <summary>
        /// Subscribe to an event type. Your callback will be called whenever
        /// that event is published.
        /// </summary>
        public static void Subscribe<T>(Action<T> callback) where T : struct
        {
            Type eventType = typeof(T);

            if (!_subscribers.ContainsKey(eventType))
            {
                _subscribers[eventType] = new List<Delegate>();
            }

            _subscribers[eventType].Add(callback);
        }

        /// <summary>
        /// Unsubscribe from an event type. ALWAYS unsubscribe in OnDestroy()
        /// to prevent memory leaks.
        /// </summary>
        public static void Unsubscribe<T>(Action<T> callback) where T : struct
        {
            Type eventType = typeof(T);

            if (_subscribers.ContainsKey(eventType))
            {
                _subscribers[eventType].Remove(callback);
            }
        }

        /// <summary>
        /// Publish an event. All subscribers will receive it immediately.
        /// </summary>
        public static void Publish<T>(T eventData) where T : struct
        {
            Type eventType = typeof(T);

            if (!_subscribers.ContainsKey(eventType)) return;

            // Iterate backwards to safely handle unsubscriptions during iteration
            var subscriberList = _subscribers[eventType];
            for (int i = subscriberList.Count - 1; i >= 0; i--)
            {
                if (subscriberList[i] is Action<T> callback)
                {
                    try
                    {
                        callback.Invoke(eventData);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[EventBus] Error in subscriber for {eventType.Name}: {e.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Remove all subscribers. Call this when changing scenes to prevent
        /// stale references.
        /// </summary>
        public static void Clear()
        {
            _subscribers.Clear();
        }
    }

    // ========================================================================
    // EVENT DEFINITIONS
    // ========================================================================
    // All game events are defined as structs here for centralized reference.
    // Structs are value types (no garbage collection) — perfect for events.
    // ========================================================================

    public struct PlayerDamagedEvent
    {
        public float Damage;
        public Vector3 HitPoint;
        public string DamageSource;
    }

    public struct PlayerDeathEvent
    {
        public string CauseOfDeath;
        public Vector3 DeathPosition;
    }

    public struct PlayerHealedEvent
    {
        public float Amount;
    }

    public struct WantedLevelChangedEvent
    {
        public int OldLevel;
        public int NewLevel;
    }

    public struct MissionStartedEvent
    {
        public string MissionId;
        public string MissionName;
    }

    public struct MissionCompletedEvent
    {
        public string MissionId;
        public bool Success;
        public int MoneyReward;
    }

    public struct WeaponEquippedEvent
    {
        public string WeaponId;
        public int CurrentAmmo;
        public int MaxAmmo;
    }

    public struct WeaponFiredEvent
    {
        public string WeaponId;
        public Vector3 Origin;
        public Vector3 Direction;
    }

    public struct VehicleEnteredEvent
    {
        public string VehicleId;
        public string VehicleType;
    }

    public struct VehicleExitedEvent
    {
        public string VehicleId;
    }

    public struct MoneyChangedEvent
    {
        public int OldAmount;
        public int NewAmount;
    }

    public struct DialogueStartedEvent
    {
        public string DialogueId;
        public string SpeakerName;
    }

    public struct DialogueEndedEvent
    {
        public string DialogueId;
    }

    public struct WeatherChangedEvent
    {
        public string NewWeather;
        public float TransitionDuration;
    }

    public struct TimeOfDayChangedEvent
    {
        public float Hour;
        public bool IsNight;
    }

    public struct SaveGameEvent
    {
        public int SlotIndex;
    }

    public struct LoadGameEvent
    {
        public int SlotIndex;
    }
}
