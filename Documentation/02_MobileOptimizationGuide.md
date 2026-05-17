# Mobile Optimization Guide — Shadow City Chronicles

## Target Hardware
- **Minimum**: Android API 26, 4GB RAM, Adreno 506 / Mali-G71 equivalent
- **Recommended**: Android API 30+, 6GB RAM, Adreno 618+ / Mali-G77+
- **Target FPS**: 30 FPS stable (minimum), 60 FPS on recommended devices

---

## 1. Rendering Pipeline — Universal Render Pipeline (URP)

### Why URP?
URP is designed for mobile. It's a single-pass forward renderer that avoids the GPU overhead of deferred rendering.

### Settings
```
Render Scale: 0.75 (Low), 0.85 (Medium), 1.0 (High)
MSAA: Off (Low), 2x (Medium), 4x (High)
HDR: Off (Low/Medium), On (High)
Shadow Resolution: 512 (Low), 1024 (Medium), 2048 (High)
Shadow Distance: 30m (Low), 50m (Medium), 80m (High)
Shadow Cascades: 1 (Low), 2 (Medium), 4 (High)
Additional Lights: 2 (Low), 4 (Medium), 8 (High)
```

### Key URP Optimizations
1. **SRP Batcher**: Enable it. It batches shader variant properties into a single buffer. Massive draw call reduction.
2. **GPU Instancing**: Enable on all shared materials (buildings, trees, props).
3. **Light Layers**: Only calculate lighting for relevant layers (don't light underground areas with the sun).

---

## 2. Texture Optimization

### Compression Formats
| Platform | Format | Quality | Size Reduction |
|----------|--------|---------|----------------|
| Android | ASTC 6x6 | Best balance | ~6x smaller |
| Android | ETC2 | Good fallback | ~4x smaller |

### Texture Sizes by Asset Type
| Asset | Max Resolution | Mipmaps |
|-------|---------------|---------|
| Character (hero) | 1024x1024 | Yes |
| NPC | 512x512 | Yes |
| Building | 1024x1024 | Yes |
| Vehicle | 512x512 | Yes |
| Props | 256x256 | Yes |
| UI | 512x512 | No |
| Skybox | 1024x1024 | No |

### Texture Atlas
Combine related textures into atlases to reduce draw calls:
- All UI elements → 1 atlas (2048x2048)
- Building windows/doors → 1 atlas
- Vehicle parts → 1 atlas per vehicle class

### Mipmaps
ALWAYS enable mipmaps for 3D textures. They:
- Reduce aliasing at distance (visual quality)
- Reduce GPU memory bandwidth (performance)
- Cost 33% more memory but save far more in GPU time

---

## 3. Mesh and Geometry Optimization

### Polygon Budgets
| Asset | Triangle Limit | LOD Levels |
|-------|---------------|------------|
| Player character | 15,000 | 3 (15K / 5K / 1K) |
| NPC | 5,000 | 3 (5K / 2K / 500) |
| Vehicle | 8,000 | 3 (8K / 3K / 800) |
| Building | 3,000 | 3 (3K / 1K / 200) |
| Prop | 500 | 2 (500 / 100) |

### LOD (Level of Detail) System
```
LOD 0: 0-20m   — Full detail, all features
LOD 1: 20-50m  — Reduced geometry, simplified materials
LOD 2: 50-100m — Billboard or very low poly
Culled: 100m+  — Not rendered at all
```

### LOD Implementation Tips
1. Use Unity's LOD Group component on every visible object
2. Set aggressive LOD transitions — players rarely notice on mobile screens
3. For distant buildings, use billboard imposters (a flat image from each angle)
4. Trees beyond 30m can be 2D sprites

### Static Batching
- Mark all non-moving objects as `Static` in the inspector
- Unity combines them into fewer draw calls at build time
- Zero runtime cost — it's done during scene loading

### Dynamic Batching
- For small objects (<300 vertices) that share a material
- Happens automatically if enabled in Player Settings
- Don't rely on it for large meshes — it has CPU overhead

### GPU Instancing
- For objects that appear many times with the same mesh (trees, streetlights, traffic cones)
- Enable "GPU Instancing" checkbox on the material
- Works with URP shaders

---

## 4. Memory Management

### Memory Budget (4GB device)
```
OS + System: ~1.5 GB
Game Maximum: ~1.5 GB
  ├── Textures: 400 MB
  ├── Meshes: 200 MB
  ├── Audio: 100 MB
  ├── Scripts/Runtime: 200 MB
  ├── Shaders: 50 MB
  ├── Physics: 100 MB
  └── Buffer: 450 MB
```

### Object Pooling (CRITICAL)
Never use `Instantiate()` and `Destroy()` during gameplay. Every call creates garbage that triggers GC spikes.

Pool these objects:
- Bullets / projectiles
- Muzzle flash particles
- Impact effects
- Tire smoke
- Blood splatter
- NPC pedestrians
- Traffic vehicles
- UI popup elements
- Audio sources

### Garbage Collection Prevention
```csharp
// BAD — Creates garbage every frame
string status = "Health: " + health + "/" + maxHealth;

// GOOD — Use StringBuilder or cache
private StringBuilder _sb = new StringBuilder(32);
_sb.Clear();
_sb.Append("Health: ").Append(health).Append('/').Append(maxHealth);
string status = _sb.ToString();

// BAD — LINQ allocates on every call
var enemies = allNPCs.Where(n => n.IsEnemy).ToList();

// GOOD — Pre-allocated list, manual iteration
_enemyBuffer.Clear();
for (int i = 0; i < allNPCs.Count; i++)
{
    if (allNPCs[i].IsEnemy) _enemyBuffer.Add(allNPCs[i]);
}
```

### Addressables
Use Unity Addressables for asset loading:
- Assets load on demand (not all at startup)
- Assets unload when not needed
- Supports asset bundles for modular content
- Enables world streaming

---

## 5. Physics Optimization

### Layer-Based Collision Matrix
Only check collisions between layers that NEED to interact:
```
Player ↔ Environment ✓
Player ↔ Enemies ✓
Player ↔ Traffic ✓
Bullets ↔ Enemies ✓
Bullets ↔ Environment ✓
Traffic ↔ Traffic ✓
Enemies ↔ Enemies ✗ (they don't collide with each other)
Bullets ↔ Bullets ✗ (bullets don't collide with bullets)
```

### Rigidbody Sleep
- Rigidbodies that haven't moved recently "sleep" — they stop being calculated
- Don't wake them unnecessarily (avoid setting velocity to zero repeatedly)
- Increase sleep threshold for non-critical objects

### Simplified Colliders
- Use Box and Sphere colliders when possible (cheapest)
- Capsule colliders for characters
- NEVER use Mesh Colliders for moving objects
- Static Mesh Colliders are fine (pre-computed)

### Physics Update Rate
```
Fixed Timestep: 0.02 (50 Hz) — default, good for most cases
For vehicles: Keep at 0.02
For background NPCs: Can use 0.04 (25 Hz) by skipping frames
```

---

## 6. AI Optimization

### LOD for AI Behavior
```
0-20m:  Full AI — perception, pathfinding, combat, animations
20-50m: Reduced AI — simplified pathfinding, basic animations
50-100m: Minimal AI — follow waypoints only, no animations
100m+: Frozen — disabled completely
```

### NavMesh Optimization
- Bake NavMesh with appropriate agent radius (0.5m for pedestrians, 2m for vehicles)
- Use NavMesh Links for jumps and ladder connections
- Limit pathfinding recalculation frequency (every 0.5s, not every frame)
- Cache paths — don't recalculate if destination hasn't changed

### NPC Limits
| Device Tier | Pedestrians | Traffic | Police | Gang |
|-------------|-------------|---------|--------|------|
| Low (4GB) | 15 | 6 | 4 | 4 |
| Medium (6GB) | 25 | 10 | 6 | 6 |
| High (8GB+) | 40 | 15 | 8 | 8 |

---

## 7. Occlusion Culling

### Unity Built-in Occlusion Culling
1. Mark large static objects as "Occluder Static" (buildings, walls)
2. Mark smaller objects as "Occludee Static" (props, furniture)
3. Bake occlusion data (Window > Rendering > Occlusion Culling > Bake)
4. Objects behind buildings won't be rendered

### Manual Culling for Dynamic Objects
```csharp
// Check if NPC is visible before running expensive updates
private bool IsVisibleToCamera(Renderer renderer)
{
    Plane[] planes = GeometryUtility.CalculateFrustumPlanes(Camera.main);
    return GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
}
```

---

## 8. World Streaming

### Chunk System
- Divide city into 200m × 200m chunks
- Load 3×3 grid around player (9 chunks max)
- Async loading prevents frame drops
- Use Addressables for chunk assets

### Loading Priority
1. Chunk the player is currently in
2. Chunk the player is moving toward
3. Adjacent chunks
4. Background chunks (preloading)

### Unloading
- Unload chunks more than 2 chunks away
- Call `Resources.UnloadUnusedAssets()` after unloading
- Spread unloading over multiple frames

---

## 9. Shader Optimization

### Mobile Shader Rules
1. Use URP/Lit shader for most objects
2. Limit to 1 texture sample per material when possible
3. Avoid real-time reflections — use reflection probes baked at build time
4. Avoid alpha transparency when possible — opaque is much cheaper
5. Use shader LOD to switch to simpler shaders at distance

### Custom Shader Tips
- `half` precision instead of `float` (GPUs on mobile are optimized for half)
- Minimize `tex2D` calls (each texture sample costs GPU time)
- Avoid `discard` in fragment shaders (breaks early-Z rejection)
- Pre-compute values in vertex shader when possible

---

## 10. Audio Optimization

### Limits
- Maximum 16 simultaneous AudioSources
- Use AudioSource pooling (same as object pooling)
- Distant sounds (>50m) are culled

### Compression
| Audio Type | Format | Load Type | Quality |
|------------|--------|-----------|---------|
| Music | Vorbis | Streaming | Medium |
| SFX (short) | ADPCM | Decompress on Load | High |
| SFX (long) | Vorbis | Compressed in Memory | Medium |
| Ambient | Vorbis | Streaming | Low |
| Voice | Vorbis | Streaming | High |

### 3D Audio
- Only use 3D audio for nearby sounds (<30m)
- Distant sounds should be 2D (no spatial processing)
- Use Linear rolloff for predictable volume falloff

---

## 11. Build Settings

### IL2CPP Backend
Always use IL2CPP for release builds:
- Converts C# to C++ at build time
- 2-4x faster than Mono
- Smaller APK size
- Required for ARM64

### Player Settings
```
Scripting Backend: IL2CPP
API Compatibility: .NET Standard 2.1
Target Architecture: ARM64 (primary), ARMv7 (fallback)
Graphics API: Vulkan (primary), OpenGL ES 3.0 (fallback)
Minimum API Level: 26
Strip Engine Code: Yes
Managed Stripping Level: Medium
```

### APK Optimization
- Use Android App Bundle (AAB) for Play Store
- Enable asset compression (LZ4)
- Split APK by architecture (ARM64 / ARMv7)
- Use Play Asset Delivery for large assets

---

## 12. Profiling Tools

### Unity Profiler
- Use during development to find CPU/GPU bottlenecks
- Profile on actual device (not editor)
- Look for: GC.Alloc, Physics, Rendering time

### Frame Debugger
- Window > Analysis > Frame Debugger
- Shows every draw call and why it wasn't batched
- Goal: under 200 draw calls per frame on mobile

### Memory Profiler
- Package Manager > Memory Profiler
- Shows exactly where memory is used
- Take snapshots and compare

### Android GPU Tools
- Snapdragon Profiler (for Qualcomm devices)
- ARM Mobile Studio (for Mali GPUs)
- RenderDoc (general GPU debugging)

---

## Performance Targets Summary

| Metric | Low | Medium | High |
|--------|-----|--------|------|
| FPS | 30 | 30 | 60 |
| Draw Calls | <150 | <200 | <300 |
| Triangles | <200K | <400K | <800K |
| Textures | <300MB | <400MB | <600MB |
| Total RAM | <1GB | <1.5GB | <2GB |
| Physics Bodies | <50 | <100 | <150 |
| Audio Sources | <8 | <12 | <16 |
