# Development Roadmap — Shadow City Chronicles

## Learning Path for Beginner Game Developers

### What to Learn First (Priority Order)

1. **C# Programming** (2-4 weeks)
   - Variables, loops, conditions, functions
   - Classes, objects, inheritance, interfaces
   - Collections (List, Dictionary, Queue)
   - Events and delegates
   - Resource: [Microsoft C# Guide](https://learn.microsoft.com/en-us/dotnet/csharp/)

2. **Unity Basics** (2-4 weeks)
   - Unity Editor (Scene, Game, Inspector, Hierarchy)
   - GameObjects and Components
   - Transforms (position, rotation, scale)
   - Prefabs and instantiation
   - Physics (Rigidbody, Colliders)
   - Input system
   - Resource: Unity Learn (learn.unity.com)

3. **3D Math** (1-2 weeks)
   - Vectors (Vector3, direction, magnitude, normalization)
   - Quaternions (rotation, Slerp, LookRotation)
   - Raycasting (Physics.Raycast)
   - Dot product (angle between vectors)
   - Cross product (perpendicular direction)

4. **Game Architecture** (2 weeks)
   - Singleton pattern
   - Event-driven design
   - State machines
   - Object pooling
   - ScriptableObjects

5. **3D Modeling with Blender** (2-4 weeks)
   - Basic modeling (box modeling, extrusion)
   - UV unwrapping
   - Texturing basics
   - Low-poly optimization for mobile
   - Export to Unity (.fbx format)

6. **Animation** (2 weeks)
   - Unity Animator Controller
   - Animation states and transitions
   - Blend trees
   - Root motion vs in-place
   - Mixamo for free character animations

7. **AI Basics** (2 weeks)
   - NavMesh navigation
   - State machines for AI
   - Patrol, chase, flee behaviors
   - Sight and hearing perception

---

## 30-Day Quick Start Plan

### Week 1: Foundation
| Day | Task |
|-----|------|
| 1 | Install Unity 2022.3 LTS, create project with URP template |
| 2 | Unity Editor tour: Scene view, Game view, Inspector, Hierarchy |
| 3 | Create a flat plane, add a capsule (player), move with WASD |
| 4 | Add CharacterController, implement basic movement script |
| 5 | Implement third-person camera (orbit around player) |
| 6 | Add sprint and crouch mechanics |
| 7 | Review and polish. Create a test scene with some ProBuilder shapes. |

### Week 2: Combat
| Day | Task |
|-----|------|
| 8 | Implement raycast shooting (Pistol) |
| 9 | Add weapon switching (2 weapons) |
| 10 | Add crosshair UI and ammo counter |
| 11 | Create a simple enemy (capsule NPC with health) |
| 12 | Enemy takes damage, shows health bar, dies |
| 13 | Add object pooling for bullet impacts |
| 14 | Cover system prototype (snap to walls) |

### Week 3: Vehicles
| Day | Task |
|-----|------|
| 15 | Create a box vehicle with WheelColliders |
| 16 | Implement basic driving controls |
| 17 | Enter/exit vehicle system |
| 18 | Add vehicle camera (wider angle, follows behind) |
| 19 | Traffic AI — simple vehicle following spline path |
| 20 | Add 3 different vehicle types (speed, handling variations) |
| 21 | Vehicle damage (health bar, smoke at low health) |

### Week 4: World Building
| Day | Task |
|-----|------|
| 22 | Create a small city block (ProBuilder or free assets) |
| 23 | Add day/night cycle (rotating directional light) |
| 24 | Spawn civilian NPCs that walk between waypoints |
| 25 | Implement basic wanted system (1-2 stars) |
| 26 | Add minimap (overhead camera + render texture) |
| 27 | Create a simple mission (go to point, kill enemy, return) |
| 28 | Mobile touch controls (virtual joystick) |
| 29 | Build to Android device, test and optimize |
| 30 | Review everything, write down what works and what needs improvement |

---

## 6-Month Development Plan

### Month 1: Core Prototype
**Goal**: Playable prototype with movement, shooting, driving, basic city

- [ ] Player controller with full movement
- [ ] Third-person camera with all modes
- [ ] Cover system
- [ ] Weapon system (3 weapons)
- [ ] Vehicle physics (car + motorcycle)
- [ ] Basic city block (1 district)
- [ ] Day/night cycle
- [ ] Mobile controls
- [ ] Build and test on Android

**Milestone**: Walk around a city block, shoot enemies, drive a car

### Month 2: Systems
**Goal**: All gameplay systems functional

- [ ] Full weapon system (8 weapons)
- [ ] Health and armor system
- [ ] Wanted system (5 levels)
- [ ] Inventory system
- [ ] Economy (money, shops)
- [ ] Save/load system
- [ ] Dialogue system
- [ ] Mission system (scripted missions)
- [ ] NPC spawning and pooling

**Milestone**: Complete a simple mission, save progress, buy a weapon

### Month 3: AI & World
**Goal**: Living city with intelligent NPCs

- [ ] Civilian AI (walk, react, flee, call police)
- [ ] Police AI (patrol, pursue, arrest, combat)
- [ ] Gang AI (territory, combat, flanking)
- [ ] Traffic AI (follow roads, obey lights)
- [ ] Combat AI (cover, suppression, flanking)
- [ ] Expand city to 3 districts
- [ ] World streaming system
- [ ] Weather system

**Milestone**: City feels alive with NPCs going about daily routines

### Month 4: Content
**Goal**: Story missions and content

- [ ] Write and implement Act 1 missions (10 missions)
- [ ] Create cutscene system
- [ ] Voice line integration (text-to-speech or recorded)
- [ ] Side missions (taxi, races, collections)
- [ ] Expand city to 6 districts
- [ ] Add interiors (safe houses, shops, garages)
- [ ] Phone system (receive calls, texts)
- [ ] Radio system with music

**Milestone**: Play through Act 1 of the story

### Month 5: Polish & Content
**Goal**: Full story and visual polish

- [ ] Complete Act 2 and Act 3 missions
- [ ] Multiple endings implementation
- [ ] All 10 city districts
- [ ] Visual polish (lighting, effects, particles)
- [ ] Audio polish (ambient, music, SFX)
- [ ] UI polish (menus, HUD, map)
- [ ] Performance optimization pass
- [ ] Bug fixing

**Milestone**: Complete story mode playable from start to finish

### Month 6: Release Preparation
**Goal**: Release-ready build

- [ ] Comprehensive testing on 5+ devices
- [ ] Memory optimization (target: <1.5GB on 4GB devices)
- [ ] FPS optimization (stable 30+ FPS)
- [ ] Battery consumption optimization
- [ ] Loading time optimization
- [ ] Tutorial / onboarding flow
- [ ] Settings menu (quality, controls, audio)
- [ ] Google Play Store listing preparation
- [ ] Privacy policy, age rating
- [ ] Beta testing with real users

**Milestone**: Published on Google Play Store

---

## 1-Year Extended Plan

### Months 7-8: Post-Launch Support
- Bug fixes from player feedback
- Performance patches
- Balance adjustments
- Minor content updates (new side missions)

### Months 9-10: DLC / Content Update
- New district expansion
- New vehicle types
- New weapons
- Additional side missions and random events
- Seasonal events

### Months 11-12: Multiplayer
- Implement basic multiplayer (free roam, 4 players)
- Co-op missions
- Competitive modes
- Leaderboards
- Anti-cheat

---

## Asset Resources

### Free Assets for Prototyping
| Resource | Type | Link |
|----------|------|------|
| Mixamo | Character animations | mixamo.com |
| Kenney Assets | Low-poly 3D models | kenney.nl |
| Unity Asset Store | Various free assets | assetstore.unity.com |
| OpenGameArt | Textures, models, sounds | opengameart.org |
| Freesound | Sound effects | freesound.org |
| Poly Pizza | Low-poly 3D models | poly.pizza |
| Sketchfab | 3D models (some free) | sketchfab.com |
| Ambientcg | PBR textures | ambientcg.com |

### Tools
| Tool | Purpose | Cost |
|------|---------|------|
| Unity 2022.3 LTS | Game engine | Free (Personal) |
| Blender | 3D modeling | Free |
| GIMP | Texture editing | Free |
| Audacity | Sound editing | Free |
| VS Code | Code editor | Free |
| ProBuilder | In-editor modeling | Free (Unity package) |
| Shader Graph | Visual shader editor | Free (URP included) |
| Cinemachine | Camera system | Free (Unity package) |

---

## Team Scaling Guide

### Solo Developer
- Focus on gameplay first, art later
- Use free/cheap assets for prototyping
- Replace placeholder art gradually
- 6-12 months for a basic version

### Small Team (2-3 people)
- 1 Programmer + 1 Artist + 1 Designer
- 6-8 months for a full version
- Can achieve higher visual quality

### Indie Studio (5-8 people)
- 2 Programmers + 2 Artists + 1 Designer + 1 Audio + 1 QA + 1 PM
- 4-6 months for a polished version
- Can include voice acting and custom music
