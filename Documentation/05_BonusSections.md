# Bonus Sections — Shadow City Chronicles

## 1. Common Mistakes Beginner Game Developers Make

### Mistake #1: Starting Too Big
**The Problem**: Wanting to build a GTA-sized game as your first project.

**The Fix**: Build systems one at a time. Your first version should be:
- One city block (not a whole city)
- One weapon (not ten)
- One vehicle (not twenty)
- One mission (not thirty)
Get each system WORKING before expanding. A small polished game beats a large broken one.

### Mistake #2: Not Using Object Pooling
**The Problem**: Using `Instantiate()` and `Destroy()` for bullets, effects, NPCs.

**Why It's Bad**: Every `Instantiate()` allocates memory. Every `Destroy()` marks it for garbage collection. On mobile, GC can cause 200ms+ frame drops.

**The Fix**: Create all objects at startup, reuse them. See `ObjectPool.cs` in this project.

### Mistake #3: String Comparisons for Tags/Layers
```csharp
// BAD — String comparison is slow
if (other.gameObject.tag == "Player")

// GOOD — CompareTag is optimized (no garbage allocation)
if (other.CompareTag("Player"))
```

### Mistake #4: Using Update() for Everything
**The Problem**: Checking distances, searching for objects, updating UI every single frame.

**The Fix**: Use timers, events, and coroutines:
```csharp
// BAD — Runs 60 times per second
void Update()
{
    float dist = Vector3.Distance(transform.position, player.position);
    if (dist < 10f) { /* react */ }
}

// GOOD — Runs every 0.3 seconds
private float _checkTimer;
void Update()
{
    _checkTimer += Time.deltaTime;
    if (_checkTimer < 0.3f) return;
    _checkTimer = 0f;
    
    float dist = Vector3.Distance(transform.position, player.position);
    if (dist < 10f) { /* react */ }
}
```

### Mistake #5: Not Testing on Real Devices
**The Problem**: Game runs great in the editor, terrible on phone.

**The Fix**: Test on a real device early and often. The Unity Editor runs on a powerful PC with unlimited RAM. Your target phone has a fraction of that power.

### Mistake #6: Premature Optimization
**The Problem**: Spending weeks optimizing code that doesn't need it.

**The Fix**: Profile first, optimize second. Use Unity Profiler to find ACTUAL bottlenecks. The bottleneck is almost never where you think it is.

### Mistake #7: Not Using Version Control
**The Problem**: "My project broke and I can't undo it."

**The Fix**: Use Git from day one. Commit often. Branch for experiments.

### Mistake #8: Ignoring Mobile Input
**The Problem**: Designing controls for mouse/keyboard, then "porting" to mobile.

**The Fix**: Design for touch from the start. Thumbs are imprecise — buttons need to be big. Auto-aim is essential for mobile shooters.

### Mistake #9: Loading Everything at Once
**The Problem**: All city assets load at startup → 30-second load time, crashes on low-memory devices.

**The Fix**: Stream the world. Load chunks around the player. Use Addressables for on-demand loading.

### Mistake #10: Not Planning Architecture
**The Problem**: Spaghetti code where everything references everything else.

**The Fix**: Use patterns:
- **Singleton** for global managers
- **Event Bus** for decoupled communication
- **State Machine** for complex behavior
- **ScriptableObject** for data

---

## 2. How GTA-Like Games Are Optimized

### World Streaming
GTA games don't load the entire map. They stream 200-400m around the player and fake everything else:
- Distant buildings are low-poly imposters
- The skybox hides the world edge
- Assets load in the direction you're moving
- Areas behind you unload aggressively

### Level of Detail (LOD)
Every object has 3-4 versions:
- Up close: 10,000 triangles, detailed textures
- Medium: 3,000 triangles, simplified textures
- Far: 500 triangles, basic colors
- Very far: 2D billboard sprite or nothing

### Population Density Tricks
GTA doesn't simulate the entire city. It only simulates NPCs near the player:
- NPCs spawn 50-80m away (out of sight)
- NPCs despawn when 100-120m away
- Maximum 30-50 pedestrians and 15-20 cars at any time
- When you turn around, NPCs behind you are already gone

### Traffic Illusion
Traffic vehicles don't have full physics until they're close:
- Distant traffic: Simple spline-following (no physics)
- Nearby traffic: Basic physics (collisions, stopping)
- Very close: Full physics (can be crashed into)

### Texture Streaming
Not all textures are loaded at full resolution:
- Textures start blurry and sharpen as you approach
- This is called "texture streaming" or "mip streaming"
- Unity supports this natively

### Audio Tricks
- Only 16-32 sound sources active at once
- Distant sounds are culled
- Reverb zones fake indoor/outdoor acoustics
- Music crossfades seamlessly between states

---

## 3. How to Avoid Lag in Open-World Games

### The Big Three Causes of Lag

#### 1. CPU Bound (too much logic)
**Symptoms**: Low FPS even with simple graphics, Profiler shows CPU > 33ms

**Solutions**:
- Reduce AI complexity for distant NPCs
- Use object pooling (eliminate GC spikes)
- Cache component references (`GetComponent` is slow)
- Use faster data structures (arrays > Lists for iteration)
- Amortize expensive operations over multiple frames

#### 2. GPU Bound (too much rendering)
**Symptoms**: Lowering resolution helps, Profiler shows GPU > 33ms

**Solutions**:
- Reduce draw calls (batching, atlasing)
- Lower shadow resolution and distance
- Use simpler shaders for distant objects
- Reduce particle counts
- Lower render scale (0.75x)
- Disable unnecessary post-processing

#### 3. Memory Bound (running out of RAM)
**Symptoms**: Stuttering, crashes on low-memory devices, long GC pauses

**Solutions**:
- Compress textures (ASTC on Android)
- Use texture streaming
- Unload assets not in use
- Object pooling
- Limit audio in memory (stream long clips)

### The Golden Rules
1. **Profile before optimizing** — Don't guess, measure
2. **Test on target hardware** — Editor performance is misleading
3. **Optimize the hot path** — 90% of CPU time is in 10% of code
4. **Budget everything** — Triangles, draw calls, memory, audio
5. **Amortize over frames** — Spread expensive work across time

---

## 4. How AAA Studios Structure Projects

### Team Structure (Large Studio)
```
Game Director
├── Programming Lead
│   ├── Gameplay Programmers (3-5)
│   ├── AI Programmers (2-3)
│   ├── Engine/Tools Programmers (2-3)
│   ├── Network Programmers (2)
│   └── UI Programmers (1-2)
├── Art Director
│   ├── Environment Artists (5-10)
│   ├── Character Artists (3-5)
│   ├── Vehicle Artists (2-3)
│   ├── VFX Artists (2-3)
│   ├── UI Artists (1-2)
│   └── Technical Artists (2-3)
├── Design Lead
│   ├── Level Designers (3-5)
│   ├── Mission Designers (2-3)
│   ├── Systems Designers (2)
│   └── Narrative Designers (1-2)
├── Audio Director
│   ├── Sound Designers (2-3)
│   ├── Music Composers (1-2)
│   └── Voice Director (1)
├── QA Lead
│   ├── QA Testers (5-10)
│   └── Automation Engineers (1-2)
└── Production
    ├── Producers (2-3)
    └── Project Managers (1-2)
```

### Code Organization Principles

#### Separation of Concerns
Each system owns its own data and logic. Systems communicate through events, not direct references.

```
PlayerController → Only handles movement and input
HealthSystem → Only handles HP, damage, death
WeaponSystem → Only handles shooting, reloading
```

#### Data-Driven Design
Game content is separated from code:
- Weapons defined in ScriptableObjects (change stats without recompiling)
- Missions defined in data files (designers edit without programmer)
- AI behavior defined in visual tools (behavior tree editors)
- UI layouts defined in prefabs (artists edit without code)

#### Version Control Workflow
```
main (production) ← Only tested, approved builds
  └── develop ← Integration branch
       ├── feature/weapon-system ← Individual features
       ├── feature/vehicle-physics
       ├── bugfix/ai-pathfinding
       └── hotfix/crash-on-load
```

**Rules**:
- Never commit directly to main
- Feature branches for all work
- Code review before merge
- Automated tests run on every commit
- Daily builds tested by QA

#### Build Pipeline
```
1. Code committed to branch
2. CI server builds the project automatically
3. Automated tests run (unit tests, integration tests)
4. If tests pass, build is deployed to test devices
5. QA team tests the build
6. If approved, merged to develop
7. Weekly builds from develop → main candidate
8. Release candidate tested for 1-2 weeks
9. Final build signed and submitted to store
```

### Sprint Cycle (Agile/Scrum)
```
2-week sprints:
Week 1:
  Mon: Sprint planning (decide what to build)
  Tue-Fri: Development
  
Week 2:
  Mon-Thu: Development + Testing
  Fri: Sprint review (demo) + Retrospective (what to improve)
```

### Documentation
AAA studios maintain:
1. **GDD (Game Design Document)**: What the game IS
2. **TDD (Technical Design Document)**: How the game is BUILT
3. **Art Bible**: Visual style guide
4. **Audio Bible**: Sound design guide
5. **Production Schedule**: Timeline and milestones
6. **Bug Database**: All known issues (Jira, Linear, etc.)
7. **Wiki**: Internal knowledge base

---

## 5. Quick Reference — Unity Performance Cheat Sheet

```
| Action                        | Cost      | Alternative              |
|-------------------------------|-----------|--------------------------|
| Instantiate()                 | HIGH      | Object Pool              |
| Destroy()                     | HIGH      | Return to pool           |
| GetComponent<T>() per frame   | MEDIUM    | Cache in Awake()         |
| FindGameObjectWithTag()       | HIGH      | Cache reference           |
| string concatenation          | MEDIUM    | StringBuilder            |
| LINQ queries                  | HIGH      | Manual loops             |
| Physics.RaycastAll()          | HIGH      | Physics.RaycastNonAlloc()|
| foreach on List               | LOW-MED   | for loop with index      |
| Debug.Log() in builds         | MEDIUM    | Strip with #if UNITY_EDITOR |
| Camera.main                   | LOW       | Cache reference          |
| Transform.position set        | LOW       | Fine, use freely         |
| Mathf.Sqrt()                  | LOW       | sqrMagnitude for comparison |
```

### Memory Tips
```
1 million vertices × 12 bytes = 12 MB
1024×1024 RGBA texture = 4 MB (uncompressed) → 0.7 MB (ASTC 6×6)
1 minute of audio (44.1kHz stereo) = 10 MB → 1 MB (Vorbis)
```

### Draw Call Reduction Checklist
- [ ] Enable SRP Batcher
- [ ] Mark static objects as Static
- [ ] Use material atlases (shared materials)
- [ ] Enable GPU Instancing on shared materials
- [ ] Use LOD Groups on all objects
- [ ] Bake occlusion culling
- [ ] Set camera far clip plane as low as possible
- [ ] Use fog to hide pop-in
