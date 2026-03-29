using System;
using System.Collections.Generic;
using UnityEngine;
using FreeFlowHero.Combat.Core;
using FreeFlowHero.Combat.HitReaction;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// 플레이어 전투 유한 상태 머신.
    /// 모든 전투 상태의 전환과 업데이트를 관리한다.
    /// </summary>
    public class PlayerCombatFSM : MonoBehaviour
    {
        [SerializeField] private CombatContext context = new();

        // ─── 상태 레지스트리 ───
        private Dictionary<Type, CombatState> states = new();
        private CombatState currentState;
        private InputBuffer inputBuffer = new();

        // ─── 프리플로우: 타겟 선택기 ───
        private TargetSelector targetSelector = new();

        // ─── 프로퍼티 ───
        public CombatState CurrentState => currentState;
        public string CurrentStateName => currentState?.StateName ?? "None";
        public CombatContext Context => context;
        public InputBuffer InputBuffer => inputBuffer;
        public TargetSelector TargetSelector => targetSelector;

        private void Awake()
        {
            // 컴포넌트 참조 설정
            context.playerTransform = transform;
            context.playerRigidbody = GetComponent<Rigidbody2D>();

            // ★ 순간이동 버그 근본 수정: Kinematic 강제 설정
            // 프리플로우 전투는 모든 이동이 스크립트 제어 → 물리 엔진 간섭 제거
            // Dynamic + MovePosition(Update)이 원인이었음
            if (context.playerRigidbody != null)
            {
                context.playerRigidbody.bodyType = RigidbodyType2D.Kinematic;
                context.playerRigidbody.useFullKinematicContacts = true; // 트리거 감지 유지
            }
            // Animator: 루트에 없으면 자식(3D 모델)에서 검색
            // 주의: GetComponentInChildren은 루트도 포함하므로, 비활성 루트 Animator를 건너뛰어야 함
            context.playerAnimator = GetComponent<Animator>();
            if (context.playerAnimator == null || !context.playerAnimator.enabled)
            {
                Animator found = null;
                foreach (var anim in GetComponentsInChildren<Animator>(true))
                {
                    if (anim.enabled && anim.runtimeAnimatorController != null)
                    {
                        found = anim;
                        break;
                    }
                }
                // Controller 없어도 활성화된 자식 Animator라면 사용
                if (found == null)
                {
                    foreach (var anim in GetComponentsInChildren<Animator>(true))
                    {
                        if (anim.enabled)
                        {
                            found = anim;
                            break;
                        }
                    }
                }
                if (found != null)
                    context.playerAnimator = found;


            }

            // ★ 루트모션 추출 모드: applyRootMotion=true + RootMotionCanceller(빈 OnAnimatorMove)
            //   applyRootMotion=false면 루트 본 위치가 애니메이션대로 이동하여 SkinnedMesh가 이탈함.
            //   true + 빈 OnAnimatorMove로 루트 본을 원점에 고정, 이동은 스크립트(rb.position) 전담.
            if (context.playerAnimator != null)
            {
                context.playerAnimator.applyRootMotion = true;
                if (context.playerAnimator.gameObject != gameObject)
                {
                    if (context.playerAnimator.gameObject.GetComponent<HitReaction.RootMotionCanceller>() == null)
                        context.playerAnimator.gameObject.AddComponent<HitReaction.RootMotionCanceller>();
                }
            }

            // ★ HitFlash 참조 캐시
            context.hitFlash = GetComponent<HitFlash>();
            if (context.hitFlash == null)
                context.hitFlash = GetComponentInChildren<HitFlash>();

            // ★ HitReactionHandler 참조 캐시 (플레이어 피격 리액션)
            context.hitReactionHandler = GetComponent<HitReaction.HitReactionHandler>();
            if (context.hitReactionHandler == null)
                context.hitReactionHandler = GetComponentInChildren<HitReaction.HitReactionHandler>();

            // 상태 등록 — Phase 1
            RegisterState(new IdleState());
            RegisterState(new StrikeState());
            RegisterState(new HitState());
            // 상태 등록 — Phase 2: 프리플로우 코어
            // WarpState 제거됨: 워핑은 StrikeState 내부 WARP 노티파이로 처리
            RegisterState(new DodgeState());
            // 상태 등록 — Phase 4: 가드 + 처형
            RegisterState(new GuardState());
            RegisterState(new ExecutionState());
            // 상태 등록 — Hard Hit 기상 흐름 (Down → GetUp)
            RegisterState(new DownState());
            RegisterState(new GetUpState());
        }

        // ─── Kinematic 중력 시뮬레이션 ───
        private const float KinematicGravity = 30f;  // 중력 가속도 (빠른 낙하)
        private float fallSpeed;
        private bool isGrounded;
        private float capsuleBottomOffset; // 캡슐 콜라이더 하단의 로컬 Y 오프셋
        private int groundLayerMask;

        /// <summary>Grounded 여부 (외부에서 참조 가능)</summary>
        public bool IsGrounded => isGrounded;

        private void Start()
        {
            // 캡슐 콜라이더 하단 오프셋 계산
            var capsule = GetComponent<CapsuleCollider2D>();
            if (capsule != null)
                capsuleBottomOffset = capsule.offset.y - capsule.size.y * 0.5f;
            else
                capsuleBottomOffset = 0f;

            // Ground 레이어 마스크
            int groundLayer = LayerMask.NameToLayer("Ground");
            groundLayerMask = groundLayer >= 0 ? (1 << groundLayer) : ~0;

            // InputBuffer 유지 시간 연동 (Inspector에서 context.inputBufferDuration 조절)
            inputBuffer.Duration = context.inputBufferDuration;

            // 초기 상태: Idle
            TransitionTo<IdleState>();

            // 초기 바닥 스냅
            SnapToGround();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // Kinematic 중력 + 바닥 감지
            ApplyKinematicGravity(dt);

            // 인풋 버퍼 틱 (Inspector 실시간 반영)
            inputBuffer.Duration = context.inputBufferDuration;
            inputBuffer.Update(dt);

            // 콤보 윈도우 틱
            context.UpdateComboWindow(dt);

            // 활성 적 목록 갱신 (Phase 2: 씬 내 모든 ICombatTarget 수집)
            RefreshActiveEnemies();

            // 현재 상태 업데이트
            currentState?.Update(dt);
        }

        private void FixedUpdate()
        {
            currentState?.FixedUpdate(Time.fixedDeltaTime);
        }

        /// <summary>
        /// Kinematic 중력: Raycast로 바닥 감지 + 중력 낙하.
        /// CapsuleCollider2D 하단이 Ground 콜라이더 상단에 닿도록 위치를 조정한다.
        /// </summary>
        private void ApplyKinematicGravity(float dt)
        {
            if (context.playerRigidbody == null) return;

            // 워핑 중이면 중력 스킵 (워프가 Y 위치를 직접 제어)
            if (context.isWarping) return;

            // ★ 넉다운 체공 중: HitReactionHandler가 Y 위치 직접 제어 → 중력 스킵
            // 이 스킵 없이는 바닥 스냅 로직이 체공 포물선과 충돌하여 캐릭터가 날지 못하는 버그 발생
            if (context.hitReactionHandler != null && context.hitReactionHandler.IsKnockdownActive) return;

            Vector2 pos = context.playerRigidbody.position;

            // 캡슐 하단 위치에서 아래로 Raycast
            Vector2 rayOrigin = pos + Vector2.up * (capsuleBottomOffset + 0.05f);
            float rayLength = isGrounded ? 0.2f : 5f; // 바닥에 있으면 짧은 레이, 공중이면 긴 레이

            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, rayLength, groundLayerMask);

            if (hit.collider != null)
            {
                // 바닥 표면 Y: 캡슐 하단이 여기에 닿아야 함
                float groundSurfaceY = hit.point.y;
                float targetY = groundSurfaceY - capsuleBottomOffset;

                if (pos.y > targetY + 0.01f)
                {
                    // 바닥 위에 떠 있음 → 중력 적용
                    fallSpeed += KinematicGravity * dt;
                    pos.y -= fallSpeed * dt;

                    // 바닥을 뚫지 않도록 클램프
                    if (pos.y <= targetY)
                    {
                        pos.y = targetY;
                        fallSpeed = 0f;
                        isGrounded = true;
                    }
                }
                else
                {
                    // 바닥에 붙어 있음
                    pos.y = targetY;
                    fallSpeed = 0f;
                    isGrounded = true;
                }
            }
            else
            {
                // 바닥 없음 → 자유 낙하
                isGrounded = false;
                fallSpeed += KinematicGravity * dt;
                pos.y -= fallSpeed * dt;
            }

            context.playerRigidbody.position = pos;
        }

        /// <summary>즉시 바닥에 스냅 (초기화 시 사용)</summary>
        private void SnapToGround()
        {
            if (context.playerRigidbody == null) return;

            Vector2 pos = context.playerRigidbody.position;
            Vector2 rayOrigin = pos + Vector2.up * (capsuleBottomOffset + 0.05f);

            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, 10f, groundLayerMask);
            if (hit.collider != null)
            {
                float groundSurfaceY = hit.point.y;
                pos.y = groundSurfaceY - capsuleBottomOffset;
                context.playerRigidbody.position = pos;
                isGrounded = true;
                fallSpeed = 0f;

            }
        }

        // ─── 상태 관리 ───

        /// <summary>상태 등록</summary>
        private void RegisterState(CombatState state)
        {
            state.Initialize(this, context);
            states[state.GetType()] = state;
        }

        /// <summary>상태 전환</summary>
        public void TransitionTo<T>() where T : CombatState
        {
            var type = typeof(T);
            if (!states.ContainsKey(type))
            {
                Debug.LogError($"[FSM] State not registered: {type.Name}");
                return;
            }

            currentState?.Exit();
            currentState = states[type];
            currentState.Enter();
        }

        /// <summary>현재 상태가 특정 타입인지 확인</summary>
        public bool IsInState<T>() where T : CombatState
        {
            return currentState != null && currentState.GetType() == typeof(T);
        }

        // ─── 입력 전달 ───

        /// <summary>전투 입력 수신 (InputSystem에서 호출)</summary>
        public void OnCombatInput(InputData input)
        {
            // 입력 방향 기록 (DodgeState 등에서 참조)
            if (input.Direction.sqrMagnitude > 0.01f)
                context.lastInputDirection = input.Direction;

            // 현재 상태에 입력 전달
            currentState?.HandleInput(input);
        }

        /// <summary>피격 수신</summary>
        public void OnPlayerHit(HitData hitData)
        {
            currentState?.OnHit(hitData);
        }

        // ─── 활성 적 스캔 ───

        private float enemyScanTimer;
        private const float EnemyScanInterval = 0.2f; // 0.2초마다 스캔

        /// <summary>씬 내 적 목록 갱신</summary>
        private void RefreshActiveEnemies()
        {
            enemyScanTimer -= Time.deltaTime;
            if (enemyScanTimer > 0f) return;
            enemyScanTimer = EnemyScanInterval;

            context.activeEnemies.Clear();
            // DummyEnemyTarget 기준으로 검색 (Phase 3에서 EnemyAI 통합 시 변경)
            var enemies = FindObjectsByType<FreeFlowHero.Combat.Enemy.DummyEnemyTarget>();
            foreach (var enemy in enemies)
            {
                if (enemy.IsTargetable)
                    context.activeEnemies.Add(enemy);
            }
        }

        // ─── 현재 액션 이름 (디버그 UI용) ───
        /// <summary>현재 실행 중인 액션 ID (Strike 상태에서만 유효)</summary>
        public string CurrentActionId
        {
            get
            {
                if (currentState is StrikeState strike)
                    return strike.CurrentActionId;
                return CurrentStateName;
            }
        }

        // ─── 디버그 OnGUI (빌드에서도 표시) ───

        private GUIStyle debugBoxStyle;
        private GUIStyle debugLabelStyle;
        private GUIStyle debugTitleStyle;

        private void OnGUI()
        {
            // 스타일 초기화 (한 번만)
            if (debugBoxStyle == null)
            {
                debugBoxStyle = new GUIStyle(GUI.skin.box);
                debugBoxStyle.normal.background = MakeTexture(2, 2, new Color(0, 0, 0, 0.7f));

                debugLabelStyle = new GUIStyle(GUI.skin.label);
                debugLabelStyle.fontSize = 16;
                debugLabelStyle.normal.textColor = Color.white;
                debugLabelStyle.fontStyle = FontStyle.Bold;
                debugLabelStyle.richText = true;

                debugTitleStyle = new GUIStyle(debugLabelStyle);
                debugTitleStyle.fontSize = 20;
                debugTitleStyle.normal.textColor = Color.cyan;
                debugTitleStyle.richText = true;
            }

            float boxW = 420, boxH = 200;
            Rect box = new Rect(10, 10, boxW, boxH);
            GUI.Box(box, "", debugBoxStyle);

            float y = 18;
            float x = 20;
            float lineH = 24;

            // 타이틀
            GUI.Label(new Rect(x, y, boxW, lineH), "REPLACED Combat Debug [Phase 2]", debugTitleStyle);
            y += lineH + 4;

            // 상태 (Phase 2 상태 색상 추가)
            string stateColor;
            switch (CurrentStateName)
            {
                case "Strike":       stateColor = "<color=yellow>"; break;
                case "Hit":          stateColor = "<color=red>"; break;
                case "Dodge":        stateColor = "<color=#44FF44>"; break;
                case "Walk":         stateColor = "<color=#88CCFF>"; break;
                case "Guard":        stateColor = "<color=#4488FF>"; break;
                case "GuardCounter": stateColor = "<color=#FFAA00>"; break;
                case "Execution":    stateColor = "<color=#FF00FF>"; break;
                default:             stateColor = "<color=lime>"; break;
            }
            GUI.Label(new Rect(x, y, boxW, lineH),
                $"State: {stateColor}{CurrentStateName}</color>  |  Frame: {context.stateFrameCounter}", debugLabelStyle);
            y += lineH;

            // 콤보
            string comboColor = context.comboCount > 0 ? "<color=orange>" : "<color=white>";
            GUI.Label(new Rect(x, y, boxW, lineH),
                $"Combo: {comboColor}{context.comboCount}</color>  |  Chain: {context.comboChainIndex}  |  Invuln: {context.isInvulnerable}", debugLabelStyle);
            y += lineH;

            // 헉슬리 & 버퍼
            GUI.Label(new Rect(x, y, boxW, lineH),
                $"Huxley: {context.huxleyGauge:F0}%  |  Buffer: {(inputBuffer.HasInput ? inputBuffer.Peek.Type.ToString() : "—")}", debugLabelStyle);
            y += lineH;

            // 타겟 & 적 수
            string targetName = targetSelector.CurrentTarget != null
                ? targetSelector.CurrentTarget.GetTransform().name : "None";
            GUI.Label(new Rect(x, y, boxW, lineH),
                $"Target: <color=cyan>{targetName}</color>  |  Enemies: {context.activeEnemies.Count}", debugLabelStyle);
            y += lineH;

            // 조작법
            var helpStyle = new GUIStyle(debugLabelStyle);
            helpStyle.fontSize = 13;
            helpStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(new Rect(x, y, boxW, lineH),
                "LClick: Attack | RClick: Heavy | Space: Dodge | L: Counter | U: Huxley", helpStyle);

            // ─── PC 머리 위 디버그 텍스트: 현재 액션 이름 ───
            DrawWorldActionLabel();
        }

        /// <summary>PC 머리 위에 현재 액션 이름을 월드 좌표 기준으로 표시</summary>
        private GUIStyle actionLabelStyle;
        private Texture2D actionLabelBgTex;

        private void DrawWorldActionLabel()
        {
            if (context.playerTransform == null) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            // 캐릭터 머리 위 위치 (Y + 2.0 오프셋)
            Vector3 worldPos = context.playerTransform.position + Vector3.up * 2.0f;
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

            // 카메라 뒤에 있으면 표시 안 함
            if (screenPos.z < 0) return;

            // Unity GUI 좌표계 변환 (Y축 반전)
            float guiY = Screen.height - screenPos.y;

            // 스타일 캐시
            if (actionLabelStyle == null)
            {
                actionLabelStyle = new GUIStyle(GUI.skin.label);
                actionLabelStyle.fontSize = 22;
                actionLabelStyle.fontStyle = FontStyle.Bold;
                actionLabelStyle.alignment = TextAnchor.MiddleCenter;
                actionLabelStyle.richText = true;
            }
            if (actionLabelBgTex == null)
                actionLabelBgTex = MakeTexture(2, 2, new Color(0, 0, 0, 0.6f));

            // 상태별 색상
            string actionText = CurrentActionId;
            string colorHex;
            switch (CurrentStateName)
            {
                case "Strike":  colorHex = "FFFF00"; break; // 노랑
                case "Dodge":   colorHex = "44FF44"; break; // 초록
                case "Counter": colorHex = "FFAA00"; break; // 주황
                case "Hit":     colorHex = "FF4444"; break; // 빨강
                case "Walk":    colorHex = "88CCFF"; break; // 하늘색
                default:        colorHex = "FFFFFF"; break; // 흰색
            }

            // 경과 시간 (프레임 → 초)
            float elapsed = context.stateFrameCounter * CombatConstants.FrameDuration;
            string label = $"<color=#{colorHex}>{actionText}  {elapsed:F2}s  (f:{context.stateFrameCounter})</color>";

            // 배경 박스
            float labelW = 300;
            float labelH = 30;
            Rect labelRect = new Rect(screenPos.x - labelW * 0.5f, guiY - labelH, labelW, labelH);

            GUI.DrawTexture(labelRect, actionLabelBgTex);
            GUI.Label(labelRect, label, actionLabelStyle);
        }

        /// <summary>단색 텍스처 생성 (OnGUI 배경용)</summary>
        private static Texture2D MakeTexture(int w, int h, Color color)
        {
            Color[] pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            Texture2D tex = new Texture2D(w, h);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
