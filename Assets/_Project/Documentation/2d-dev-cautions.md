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

---

## 6. AnimatorController 재생성 시 프리팹 참조 끊김

**Symptom**: Full Setup 실행 후 적의 애니메이션이 전혀 재생되지 않음. 콘솔에 "Animator is not playing an AnimatorController" 에러.

**Root Cause**: `CombatSceneSetup.Execute()`에서 `EnemyAnimatorBuilder.Execute()`를 호출 → 기존 AnimatorController를 삭제하고 새로 생성 → 이미 `ModelSetup`에서 프리팹에 할당해둔 controller 참조가 무효화됨. FullSetup 순서상 `EnemyAnimatorBuilder` → `ModelSetup` 순으로 실행되므로, ModelSetup이 할당한 뒤 CombatSceneSetup이 다시 빌드하면서 참조를 깨뜨림.

**Fix**: `CombatSceneSetup.Execute()`에서 중복 `EnemyAnimatorBuilder.Execute()` 호출 제거. FullSetup이 이미 올바른 순서로 빌드하므로 추가 호출 불필요.

**Files**: `CombatSceneSetup.cs`, `EnemyAnimatorBuilder.cs`

**Rule**: AnimatorController를 Delete+Create 방식으로 재생성하는 빌더는 파이프라인에서 **한 번만** 호출할 것. 프리팹에 controller를 할당한 뒤 재빌드하면 참조가 끊어진다.

---

## 7. GetComponentInChildren이 잘못된 Animator를 잡는 문제

**Symptom**: `HitReactionHandler`에서 `SetTrigger("Flinch")`를 호출해도 모션이 재생되지 않음. `PlayFlinchAnim()`이 `runtimeAnimatorController == null` 체크에 걸려 조기 리턴.

**Root Cause**: 플레이어 오브젝트 구조가 [Root(Animator 비활성/컨트롤러 없음)] → [Child Model(Animator 활성/PlayerCombatAnimator 할당)]. `GetComponentInChildren<Animator>()`는 자기 자신부터 검색하므로 루트의 빈 Animator를 반환. 이 Animator는 runtimeAnimatorController가 null이라 트리거가 발사되지 않음.

**Fix**: `PlayerCombatFSM`과 동일한 패턴 적용 — `GetComponentsInChildren<Animator>(true)`로 전체 순회하며 `enabled && runtimeAnimatorController != null`인 Animator를 우선 선택.

```csharp
foreach (var anim in GetComponentsInChildren<Animator>(true))
{
    if (anim.enabled && anim.runtimeAnimatorController != null)
    {
        animator = anim;
        break;
    }
}
if (animator == null)
    animator = GetComponentInChildren<Animator>(); // 폴백
```

**Files**: `HitReactionHandler.cs`

**Rule**: 3D 모델을 자식으로 두는 구조에서 Animator를 찾을 때, 단순 `GetComponentInChildren<Animator>()` 사용 금지. 반드시 `enabled + runtimeAnimatorController != null` 조건으로 필터링할 것.

---

## 8. HitData.Reaction 미설정 — 피격 리액션 무반응

**Symptom**: 적이 플레이어를 공격해도 Flinch/Knockdown 모션이 전혀 재생되지 않음. HitState에 진입은 하지만 눈에 보이는 리액션 없음.

**Root Cause**: `EnemyAIController.ExecuteAttack()`에서 HitData를 생성할 때 `Reaction` 필드를 설정하지 않음. 구조체 기본값으로 `FlinchData`의 모든 값이 0 (pushDistance=0, freezeTime=0, hitStop=0). 결과적으로 밀림 없음, 경직 없음, 모션은 찰나만 재생되고 즉시 Idle 복귀.

**Fix**: `BattleSettings.GetFlinchPreset()` / `GetKnockdownPreset()`으로 프리셋 데이터를 가져와 `hitData.Reaction`에 할당.

```csharp
// Punch → Flinch
var flinchData = BattleSettings.GetFlinchPreset(HitPreset.Light);
hitData.Reaction = HitReactionData.CreateFlinch(flinchData);

// Kick → Knockdown
var knockdownData = BattleSettings.GetKnockdownPreset(HitPreset.Light);
hitData.Reaction = HitReactionData.CreateKnockdown(knockdownData);
```

**Files**: `EnemyAIController.cs`

**Rule**: HitData를 생성할 때 `Reaction` 필드를 반드시 설정할 것. 구조체 기본값(all zero)은 유효한 리액션이 아니다.

---

## 9. 적 공격 애니메이션 땅 파묻힘

**Symptom**: 적이 공격 모션 재생 시 메쉬가 땅 아래로 내려갔다가 모션 끝나면 원위치로 복귀. "땅에 박혔다가 올라오는" 현상.

**Root Cause (3 layers)**:

### 9a. applyRootMotion = true
모델 Animator의 `applyRootMotion`이 true인 상태에서 3D FBX 애니메이션의 루트 모션이 Y축 이동을 포함 → 메쉬가 아래로 이동.

**Fix**: `ModelSetup`과 `HitReactionHandler.Awake()`에서 `animator.applyRootMotion = false` 강제 설정.

### 9b. FBX Root Transform Position 미베이크
FBX 임포트 시 `lockRootHeightY`, `lockRootPositionXZ`, `lockRootRotation`이 false → 루트 모션 데이터가 클립에 남아있음.

**Fix**: `FBXImportSetup.cs`에 `BakeRootMotionIntoPose()` 추가. 모든 애니메이션 FBX의 클립에 대해 자동으로 Bake Into Pose 설정.

### 9c. ApplyGravity()가 공격 중에도 실행
EnemyAIController의 `ApplyGravity()`가 Attack/Telegraph 상태에서도 계속 실행 → 수동 중력이 공격 모션 중 Y 위치를 강제로 끌어내림.

**Fix**: Attack, Telegraph, Knockdown 상태에서는 `ApplyGravity()` 스킵.
```csharp
if (currentState != AIState.Knockdown
    && currentState != AIState.Attack
    && currentState != AIState.Telegraph)
    ApplyGravity();
```

**Files**: `EnemyAIController.cs`, `ModelSetup.cs`, `HitReactionHandler.cs`, `FBXImportSetup.cs`

**Rules**:
- 2D 게임에서 3D 모델 사용 시 `applyRootMotion = false`는 필수. 런타임 안전장치로도 설정할 것.
- FBX 임포트 시 Root Transform Position/Rotation을 Bake Into Pose로 고정할 것.
- Kinematic 캐릭터의 수동 중력은 공격/특수 상태에서 스킵해야 한다.

---

## 10. Knockdown 후 Idle 복귀 실패 — 적이 이상한 포즈로 멈춤

**Symptom**: 적이 넉다운 당한 후 일어나지 못하고 넉다운 포즈로 영구 정지.

**Root Cause**: Knockdown → HitStun 전환 시 `stateTimer`가 `reactionHandler.FreezeTimeRemaining`을 사용. 넉다운이 끝나면 flinchTimer가 이미 0으로 클리어된 상태 → `stateTimer = 0` → 즉시 HitStun 종료 → Chase 전환은 되지만 Animator가 넉다운 포즈에서 복귀하지 못함.

**Fix**: `stateTimer = Mathf.Max(freezeRemaining, hitStunDuration)` — 최소 hitStunDuration(0.35초) 보장. Chase 전환 시 `SafeSetTrigger("Idle")`로 Animator 상태 강제 복귀.

**Files**: `EnemyAIController.cs`

**Rule**: 상태 전환 시 타이머를 외부 소스에서 가져올 때, 반드시 최소값(fallback)을 보장할 것. `Mathf.Max(externalValue, minimumDuration)` 패턴 사용.
