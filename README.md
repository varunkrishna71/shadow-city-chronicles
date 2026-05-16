# Shadow City Chronicles

**A dark, cinematic open-world action-adventure game for Android**

---

## Overview

Shadow City Chronicles is a realistic 3D open-world crime drama set in the fictional metropolis of **Ashenmere** — a decaying coastal city strangled by corruption, gang warfare, and political betrayal. Players step into the shoes of **Marcus Vega**, a former military operative who returns to his hometown to find it consumed by violence and moral decay.

Inspired by the dark, gritty atmosphere of classic open-world crime games, Shadow City Chronicles delivers a console-like experience on mobile devices with:

- **Deep narrative storytelling** with multiple endings
- **Realistic driving and shooting mechanics**
- **A living, breathing open world** with dynamic NPCs, weather, and day/night cycles
- **Optimized for mid-range Android devices** (4GB RAM+)
- **Offline story mode** with optional online features

## Engine

**Unity 2022 LTS** with **C#** — chosen for:
- Superior mobile optimization pipeline (IL2CPP, Addressables, URP)
- Largest mobile game development ecosystem
- C# is beginner-friendly yet production-capable
- Extensive free/paid asset store for rapid prototyping
- Built-in profiling tools critical for mobile performance

## Project Structure

```
shadow-city-chronicles/
├── Documentation/          # Game design docs, roadmaps, guides
├── Assets/
│   ├── Scripts/            # All C# source code
│   │   ├── Core/           # Singletons, managers, utilities
│   │   ├── Player/         # Player movement, cover, interaction
│   │   ├── Camera/         # Third-person camera system
│   │   ├── Weapons/        # Weapon logic, projectiles, effects
│   │   ├── Vehicles/       # Driving physics, damage, bikes
│   │   ├── AI/             # All AI systems (civilian, police, gang, traffic)
│   │   ├── Systems/        # Health, wanted, economy, save/load, missions
│   │   ├── World/          # Weather, day/night, streaming, NPC spawning
│   │   ├── UI/             # HUD, menus, mobile controls, minimap
│   │   ├── Audio/          # Audio manager, radio, ambient sounds
│   │   └── Multiplayer/    # Future multiplayer architecture
│   ├── Prefabs/            # Reusable game object prefabs
│   ├── Materials/          # PBR materials and shaders
│   ├── Textures/           # Texture atlases and maps
│   ├── Models/             # 3D models (characters, vehicles, environment)
│   ├── Animations/         # Animation clips and controllers
│   ├── Audio/              # Sound effects, music, voice lines
│   ├── Scenes/             # Unity scenes (menu, world chunks, missions)
│   ├── Shaders/            # Custom mobile-optimized shaders
│   ├── ScriptableObjects/  # Data-driven configs (weapons, vehicles, missions)
│   └── Resources/          # Runtime-loaded configuration
└── .gitignore
```

## Getting Started

1. **Install Unity 2022.3 LTS** with Android Build Support
2. Clone this repository
3. Open the project in Unity
4. Switch platform to Android (File > Build Settings)
5. Read `Documentation/01_GameDesignDocument.md` for the full game vision
6. Start with the Core scripts to understand the architecture

## Development Roadmap

See `Documentation/12_DevelopmentRoadmap.md` for the complete 30-day, 6-month, and 1-year plans.

## License

This is an original creative work. All characters, story elements, city designs, and game systems are original creations. No copyrighted material from any existing game franchise is used.
