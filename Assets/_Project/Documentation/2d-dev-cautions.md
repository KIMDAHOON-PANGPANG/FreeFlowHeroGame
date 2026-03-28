# 2D Development Cautions

Lessons learned from real bugs encountered during development.
Each section describes the bug, root cause, and the fix applied.

---

## 1. Frame-Rate Dependent Combat Timing

**Symptom**: Attack animations and combo chains ran faster on high-FPS machines (120fps+) and slower on low-FPS machines (30fps). Button mashing made attacks unnaturally fast.

**Root Cause**: `stateFrameCounter++` was incremented once per `Update()` call. At 120fps this counted 120 frames/sec instead of the intended 60.

**Fix**: Time-accumulator based frame counter.
```
stateTimeAccumulator += deltaTime;
stateFrameCounter = FloorToInt(stateTimeAccumulator / FrameDuration);  // FrameDuration = 1/60
```
This produces the same frame count regardless of actual FPS. At 30fps, after 0.2 seconds: `stateFrameCounter = 12` (same as 60fps).

**Files**: `CombatContext.cs` — `TickFrame(float deltaTime)`

**Rule**: Never use `counter++` per Update for gameplay timing. Always derive frame counts from elapsed time.

---

## 2. Hit Flash Not Visible on 3D Meshes

**Symptom**: `HitFlash.Play()` was called and logged correctly, but 3D character models (EEJANAI/UnityToon shader) showed no visual flash.

**Root Cause (3 layers)**:

### 2a. Awake() Timing — Child Renderers Not Yet Added
HitFlash was on the root GameObject. `GetComponentsInChildren<Renderer>()` was called in `Awake()`, but the 3D model child (with `SkinnedMeshRenderer`) was added later by a separate editor setup step. Result: only the root `SpriteRenderer` was cached.

**Fix**: Lazy initialization — collect renderers on first `Play()` call, not `Awake()`.

### 2b. Global Mode Decision — Wrong Renderer Inspected
The code checked `targetRenderers[0].sharedMaterial.HasProperty("_FlashAmount")` to decide between MPB mode and material-swap mode. Since `targetRenderers[0]` was the root SpriteRenderer (with Sprite-Flash shader), it always chose MPB mode — which does nothing for SkinnedMeshRenderer with UnityToon shader.

**Fix**: Per-renderer mode decision. Each renderer independently checks if its shader supports `_FlashAmount`. Those that do get MPB; others get material swap.

### 2c. Color Property Lerp Doesn't Work on Multi-Property Shaders
Initial 3D fallback tried lerping `_Color` to white. UnityToon uses `_BaseColor`, `_1st_ShadeColor`, `_2nd_ShadeColor` etc. Changing only `_Color` produced no visible change.

**Fix**: Material swap approach — replace all materials with a white Unlit material for the flash duration, then restore originals.

**Files**: `HitFlash.cs`

**Rules**:
- Never cache renderers in `Awake()` if child objects may be added later. Use lazy init or `Start()`.
- When mixing 2D sprites and 3D meshes on the same hierarchy, decide flash mode **per-renderer**, not globally.
- For universal hit flash on arbitrary shaders, material swap is the most reliable approach.

---

## 3. Enemy Falling Through Ground

**Symptom**: Enemies fell below the ground collider during gameplay.

**Root Cause (2 layers)**:

### 3a. Ground Snap Miscalculation
`ApplyGravity()` snapped `pos.y = groundHit.point.y` (ground surface Y). But `pos.y` is the character's **pivot**, not the capsule bottom. This placed the capsule's center at ground level, sinking half the capsule underground. Next frame, the raycast origin was already below ground → no ground detected → freefall loop.

**Fix**: Account for capsule geometry when snapping:
```
targetPivotY = groundHit.point.y - capsuleOffsetY + capsuleHalfHeight;
```

### 3b. Knockback Y Component
`TakeHit()` knockback used full `KnockbackDirection` (including Y). Negative Y pushed enemies downward into the ground.

**Fix**: Zero out Y component — horizontal knockback only:
```
Vector2 knockDir = new Vector2(hitData.KnockbackDirection.x, 0f);
```

**Files**: `EnemyAIController.cs`, `DummyEnemyTarget.cs`

**Rules**:
- When snapping a character to ground, always account for collider offset and half-height.
- For 2D side-scrollers, knockback should be horizontal-only unless intentionally launching.

---

## 4. ContainsFrame Exclusive End — Off-by-One

**Symptom**: ROOT_MOTION notify stopped 1 frame early. Character movement ceased before the action ended.

**Root Cause**: `ContainsFrame()` uses `frame < endFrame` (exclusive). Setting `endFrame = 29` covers frames 0–28 only. If the action runs 30 frames (0–29), frame 29 has no ROOT_MOTION.

**Fix**: Set `endFrame = totalFrames + 1` when the notify should cover the entire action duration. For FBX sync, use the clip's actual frame count directly.

**Files**: `ActionTableEditorWindow.cs` (SyncRootMotionFromFBX), `PC_Hero.json`

**Rule**: When using half-open intervals `[start, end)`, remember that `endFrame` itself is excluded. If a notify must cover frame N, set `endFrame = N + 1`.

---

## 5. DontDestroyOnLoad Cleanup Warning

**Symptom**: "Some objects were not cleaned up when closing the scene" warning for `[ActionTableManager]`.

**Root Cause**: `OnDestroy()` didn't clear the static `instance` reference. Unity couldn't fully clean up the singleton.

**Fix**: Add `if (instance == this) instance = null;` in `OnDestroy()`.

**Rule**: Any MonoBehaviour singleton using `DontDestroyOnLoad` must null its static reference in `OnDestroy()`.
