# 계획 변경: Memory 통합 + 그룹 AI 제거 + 기본 AI만 유지

## Context
사용자가 계획을 변경 요청:
1. memory/ 피드백 MD 파일 4개를 CLAUDE.md에 통합 후 삭제
2. 프리플로우 그룹 AI (토큰 우선순위, ThreatLine, Aggression 등) 전부 제거
3. 기본적인 몬스터 AI만 유지 (대기, 추적, 공격 사거리→공격, 뒷걸음질)

---

## Task 1: Memory 피드백 파일 → CLAUDE.md 통합

### 작업
CLAUDE.md 하단에 `## 피드백 규칙` 섹션 추가, 아래 4개 항목 병합:

1. **커밋 후 자동 푸시** — `git push origin master` 자동 실행
2. **Inspector 변경은 에디터 스크립트로** — .meta 직접 수정 금지, C# 에디터 스크립트로
3. **기능 수정 시 툴체인 동기화** — 런타임 코드 변경 시 에디터/빌더/팩토리도 갱신 (이미 CLAUDE.md에 유사 내용 있으므로 중복 제거)
4. **툴팁 흰색 텍스트 유지** — ActionTableEditorWindow 툴팁은 흰색

### 삭제 대상
- `memory/feedback_auto_push.md`
- `memory/feedback_inspector_editor_script.md`
- `memory/feedback_toolchain_sync.md`
- `memory/feedback_tooltip_white.md`
- `memory/MEMORY.md` 내용 비우기 (항목 제거)

---

## Task 2: 그룹 AI 파일 삭제

### 삭제할 파일 (3개)
- `Assets/_Project/Scripts/Combat/Enemy/ThreatLineManager.cs` (+.meta)
- `Assets/_Project/Scripts/Combat/Enemy/AggressionSystem.cs` (+.meta)
- `Assets/_Project/Scripts/Combat/Enemy/IdleThreatBehavior.cs` (+.meta)

---

## Task 3: AttackCoordinator.cs 단순화

그룹 AI 관련 필드/메서드 제거, 기본 FIFO 방식만 유지.

### 제거 대상
- 필드: `alternateLeftRight`, `perEnemyCooldown`, `screenBoundCheck`, `registeredEnemies`, `candidates`, `enemyCooldowns`, `lastAttackSide`
- 메서드: `RegisterEnemy()`, `UnregisterEnemy()`, `ProcessCandidates()`, `CalculatePriority()`, `HasCandidateOnSide()`, `TickEnemyCooldowns()`
- `RequestAttackSlot()`에서 개별 쿨다운/화면 체크/좌우 교대 로직 제거
- `ReleaseAttackSlot()`에서 개별 쿨다운 설정 제거
- `Update()`에서 `ProcessCandidates()`, `TickEnemyCooldowns()` 호출 제거

### 유지 대상
- `maxSimultaneousAttackers`, `breathingTime` (기본 공격 스로틀링)
- `activeAttackers` 리스트, `globalCooldownTimer`
- 기본 `RequestAttackSlot()`: 슬롯 수 체크 + 글로벌 쿨다운만
- 기본 `ReleaseAttackSlot()`: 슬롯 해제 + 호흡 시간

---

## Task 4: EnemyAIController.cs — Circling 상태 제거, 기본 AI만

### 제거 대상
- `AIState.Circling` 열거값
- `UpdateCircling()` 메서드 전체
- `idleThreatBehavior` 필드 및 초기화
- `circlingShuffleTimer`, `circlingShuffleDir` 변수
- `TransitionTo(Circling)` 케이스
- Chase에서 ThreatLineManager 슬롯 체크/이동 로직
- ThreatLineManager.Register/Unregister 호출 전부
- 다른 상태(PostAttack, HitStun, Groggy, GetUp)에서 Circling으로 복귀하는 분기

### 유지하는 기본 AI 루프
```
Idle → (감지) → Chase → (사거리 진입) → RequestAttackSlot
→ Telegraph → Attack → PostAttack(뒷걸음질) → Chase
```

### 변경 사항
- PostAttack, HitStun, Groggy, GetUp 상태에서 Circling 대신 **Chase**로 복귀
- Chase에서 ThreatLineManager 없이 직접 플레이어 방향으로 이동
- 공격 사거리 도달 시 `AttackCoordinator.RequestAttackSlot()` → 성공하면 Telegraph

---

## Task 5: 에디터/툴체인 동기화

### CombatSceneSetup.cs
- ThreatLineManager, AggressionSystem 자동 생성 코드 제거

### PrefabFactory.cs
- IdleThreatBehavior 자동 부착 코드 제거

### CombatConstants.cs
- 그룹 AI 상수 제거: `SlotsPerSide`, `NearLaneDistance`, `FarLaneDistance`, `SlotSpacing`, `PerEnemyCooldown`, `TokenRefreshRate`, `BaseAggression`, `AggressionPerCombo`, `ComboBreakPenalty`

### EnemyAnimatorBuilder.cs
- Feint/Taunt/Shuffle 관련 애니메이션 상태 있으면 제거 (확인 필요)

### BattleSettings.cs / BattleSettingsEditor.cs
- 그룹 AI 관련 설정 필드 있으면 제거 (확인 필요)

---

## Task 6: CLAUDE.md 계획 업데이트

프리플로우 그룹 AI 관련 로드맵 항목을 제거하고, 기본 AI 상태 설명으로 대체.

---

## 검증
1. 코드 컴파일 확인 — 삭제된 클래스 참조 없는지
2. Quick Rebuild 실행 안내
3. 적이 기본 루프(대기→추적→공격→후퇴)로 동작하는지 확인
