# 애니메이션 재생 안 됨 — Martial Art 폴더 이동으로 인한 경로 불일치 수정

## Context

사용자가 `Assets/Martial Art Animations Sample/` 폴더를 Unity 내에서 `Assets/Resouces/Martial Art Animations Sample/`로 이동했다. 이로 인해 AnimatorController 빌드, FBX Import, 런타임 클립 오버라이드 등에서 FBX 클립을 찾지 못해 **모든 애니메이션이 재생되지 않는** 상태.

## 수정 내용

모든 `"Assets/Martial Art Animations Sample/"` 경로를 `"Assets/Resouces/Martial Art Animations Sample/"`로 변경.

### 1. AnimatorControllerBuilder.cs

경로 상수 수정:
- `IdleFBX`: `"Assets/Martial Art Animations Sample/Animations/Fight_Idle.fbx"` → `"Assets/Resouces/Martial Art Animations Sample/Animations/Fight_Idle.fbx"`
- `MartialArtRoot`: `"Assets/Martial Art Animations Sample/Animations"` → `"Assets/Resouces/Martial Art Animations Sample/Animations"`
- 라인 328: Guard_Idle.fbx 경로도 MartialArtRoot 기반이므로 자동 수정됨
- 라인 371: GuardCounter2 `gc2FbxPath` — 이미 `"Assets/Resouces/"` 포함되어 있으므로 변경 불필요

### 2. EnemyAnimatorBuilder.cs

경로 상수 수정 (6개):
- `IdleFBX`, `WalkForwardFBX`, `WalkBackFBX`, `FlinchFBX`, `KnockdownFBX`, `GetUpFBX`, `AttackKickFBX`, `AttackPunchFBX`
- 모두 `"Assets/Martial Art..."` → `"Assets/Resouces/Martial Art..."`

### 3. FBXImportSetup.cs

- 라인 19: `MartialArtAnimFolder` 경로 수정
- 라인 108: `martialArtModel` Armature.fbx 경로 수정

### 4. ActionTableEditorWindow.cs

- 라인 3704: FBX 검색 경로 배열에서 `"Assets/Martial Art Animations Sample"` → `"Assets/Resouces/Martial Art Animations Sample"`

### 5. CombatConstants.cs

- `FlinchClipPath`, `KnockdownClipPath` 경로 수정

### 6. PC_Hero.json (clipPath 필드)

clipPath에 `"Assets/Martial Art Animations Sample/"` 포함된 항목 전부 수정:
- Fight_Idle.fbx, Atk_P_1.fbx, Atk_P_2.fbx, Atk_K_1.fbx, Dodge_B.fbx, Dodge_F.fbx, Guard_Idle.fbx
- GuardCounter2의 Atk_P_2.fbx는 이미 `"Assets/Resouces/"` 포함되어 OK

## 검증

1. Full Setup 실행 (REPLACED > Setup > 0. Full Setup)
2. Play 모드 진입 — Idle 애니메이션 재생 확인
3. J키 공격 — 콤보 애니메이션 재생 확인
4. Shift키 회피 — 회피 애니메이션 재생 확인
5. Console에 `[ClipOverrider] Override 적용 완료` 로그 + 클립 로드 실패 경고 없음 확인
