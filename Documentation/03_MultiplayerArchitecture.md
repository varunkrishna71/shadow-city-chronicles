# Multiplayer Architecture — Shadow City Chronicles (Future Feature)

## Overview

This document outlines the architecture for OPTIONAL multiplayer support.
The single-player game is the priority. Multiplayer is designed to be added later
without rewriting core systems.

---

## 1. Multiplayer Modes (Planned)

### Free Roam (4-8 players)
- Shared open world
- Players can cooperate or compete
- Shared wanted system
- Optional PvP toggle

### Co-op Missions (2-4 players)
- Modified story missions for 2+ players
- One player hosts, others join
- Shared objectives, individual rewards

### Competitive Modes
- **Deathmatch**: 4-8 players, small arena areas
- **Race**: Vehicle races through the city
- **Turf War**: Teams capture and hold territory
- **Heist vs Heist**: Two teams compete to complete a heist first

---

## 2. Networking Architecture

### Client-Server Model (Recommended)
```
[Client A] ←→ [Dedicated Server] ←→ [Client B]
                    ↕
               [Client C]
```

**Why Client-Server?**
- Authoritative server prevents cheating
- Better consistency for game state
- Required for competitive modes
- More work to implement but more robust

### Networking Library: Unity Netcode for GameObjects
- Built-in Unity solution
- Supports client-server and relay
- NetworkVariable for state sync
- RPC (Remote Procedure Calls) for events
- Integrates with Unity Transport Layer

### Alternative: Mirror
- Open-source, battle-tested
- Simpler API
- Better documentation
- Larger community

---

## 3. Network Architecture Patterns

### State Synchronization
```csharp
// Example: Syncing player position with interpolation
public class NetworkPlayerController : NetworkBehaviour
{
    private NetworkVariable<Vector3> _networkPosition = new NetworkVariable<Vector3>();
    private NetworkVariable<Quaternion> _networkRotation = new NetworkVariable<Quaternion>();
    
    // Server sends position updates 10x/second
    // Clients interpolate between received positions
    // Result: smooth movement with minimal bandwidth
}
```

### Key Networking Concepts

#### Client-Side Prediction
- Client immediately moves when input is pressed (feels responsive)
- Server validates and corrects if needed
- Critical for mobile where latency is higher

#### Server Reconciliation
- If server says position is different, smoothly correct
- Don't teleport — lerp to corrected position over 100ms
- Player barely notices corrections if done right

#### Entity Interpolation
- Other players' positions are interpolated between server updates
- Buffer 2-3 server ticks for smooth rendering
- Trade-off: adds 60-100ms visual delay but looks smooth

---

## 4. Bandwidth Optimization (Critical for Mobile)

### Data Budget
- **Upload**: 1-2 KB/s per player
- **Download**: 3-5 KB/s per player
- Total: ~50 KB/s for 8-player session

### Optimization Techniques

1. **Delta Compression**: Only send values that changed
2. **Quantization**: Compress floats (position: 2 bytes per axis instead of 4)
3. **Interest Management**: Only sync entities relevant to each player
4. **Update Frequency Tiers**:
   - Player position: 10 Hz (every 100ms)
   - NPC position: 5 Hz (every 200ms)
   - Vehicle physics: 15 Hz (every 67ms)
   - World state: 1 Hz (every second)
   - UI/Stats: On change only

---

## 5. Multiplayer-Ready Code Patterns

### Abstracting Input
```csharp
// Single-player and multiplayer use the same interface
public interface IPlayerInput
{
    Vector2 MoveInput { get; }
    Vector2 LookInput { get; }
    bool FireInput { get; }
    // ... etc
}

// Single-player reads from touch/keyboard
public class LocalInput : IPlayerInput { ... }

// Multiplayer reads from network
public class NetworkInput : IPlayerInput { ... }
```

### Abstracting Authority
```csharp
// Check who controls this entity
if (IsOwner) // Only the owning client sends input
{
    ProcessInput();
}

if (IsServer) // Only the server validates actions
{
    ValidateAction();
    ApplyAction();
}
```

---

## 6. Backend Services

### Recommended: Unity Gaming Services (UGS)
- **Relay**: P2P connection without port forwarding
- **Lobby**: Matchmaking and room management
- **Multiplay**: Dedicated server hosting
- **Cloud Save**: Save data in the cloud
- **Economy**: Virtual currency management
- **Authentication**: Anonymous + platform login

### Alternative: PlayFab (Microsoft)
- Free tier generous for indie
- Matchmaking built-in
- Analytics included
- Leaderboards

---

## 7. Anti-Cheat Considerations

### Server Authority
- Server validates all actions (damage, money, position)
- Client sends INPUT, server calculates RESULT
- Never trust the client

### Speed Hack Detection
- Server tracks player velocity
- Flag impossible movement speeds
- Rubber-band cheaters back to valid position

### Damage Validation
- Server calculates if shot was possible (line of sight, range, fire rate)
- Reject impossible damage events

---

## 8. Implementation Timeline

### Phase 1: Foundation (Month 1)
- Add networking library
- Abstract input system
- Network player movement
- Basic lobby/connect

### Phase 2: Core (Month 2-3)
- Sync vehicles
- Sync weapons/combat
- Sync NPCs (server-controlled)
- Chat system

### Phase 3: Game Modes (Month 4-5)
- Free Roam mode
- Co-op missions
- Deathmatch

### Phase 4: Polish (Month 6)
- Anti-cheat
- Matchmaking
- Leaderboards
- Testing and optimization

---

## 9. Preparing Single-Player Code for Multiplayer

The existing code is designed with multiplayer in mind:

1. **Singleton managers** → Will become server-authoritative
2. **Event Bus** → Will route through network (local events vs network events)
3. **Object Pool** → Will sync pool IDs across clients
4. **State Machines** → AI runs on server only, clients receive state updates
5. **Save System** → Will have cloud save option alongside local

No major rewrites needed — the architecture supports the transition.
