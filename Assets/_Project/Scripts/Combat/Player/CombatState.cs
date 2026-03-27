using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// 전투 FSM 상태 기본 클래스.
    /// 모든 전투 상태는 이 클래스를 상속한다.
    /// </summary>
    public abstract class CombatState
    {
        protected PlayerCombatFSM fsm;
        protected CombatContext context;

        /// <summary>상태 이름 (디버깅/로깅용)</summary>
        public abstract string StateName { get; }

        /// <summary>초기화 (FSM에서 호출)</summary>
        public void Initialize(PlayerCombatFSM fsm, CombatContext context)
        {
            this.fsm = fsm;
            this.context = context;
        }

        /// <summary>상태 진입 시 호출</summary>
        public virtual void Enter()
        {
            context.ResetStateFrame();
            context.canCancel = false;

            // Kinematic 모드: velocity 리셋 (잔여 속도 제거)
            if (context.playerRigidbody != null)
                context.playerRigidbody.linearVelocity = Vector2.zero;

        }

        /// <summary>상태 이탈 시 호출</summary>
        public virtual void Exit()
        {
        }

        /// <summary>매 프레임 업데이트</summary>
        public virtual void Update(float deltaTime)
        {
            context.TickFrame(deltaTime);
        }

        /// <summary>FixedUpdate (물리)</summary>
        public virtual void FixedUpdate(float fixedDeltaTime) { }

        /// <summary>입력 처리</summary>
        public virtual void HandleInput(InputData input) { }

        /// <summary>피격 시 호출</summary>
        public virtual void OnHit(HitData hitData)
        {
            // 기본 동작: Hit 상태로 전환 (무적이 아닌 경우)
            if (!context.isInvulnerable)
            {
                fsm.TransitionTo<HitState>();
            }
        }

        // ─── 유틸리티 ───

        /// <summary>Rigidbody2D 위치를 정준 위치로 사용 (Transform 직접 읽기 금지)</summary>
        protected Vector2 GetPos()
        {
            return context.playerRigidbody != null
                ? context.playerRigidbody.position
                : (Vector2)context.playerTransform.position;
        }

        /// <summary>Kinematic 이동: Rigidbody2D.position 직접 설정 (순간이동 없음)</summary>
        protected void MoveTo(Vector2 newPos)
        {
            if (context.playerRigidbody != null)
                context.playerRigidbody.position = newPos;
            else
                context.playerTransform.position = new Vector3(newPos.x, newPos.y, context.playerTransform.position.z);
        }

        // ─── 충돌 검사 캐시 ───
        private static int enemyLayerMask = -1;
        private CapsuleCollider2D cachedCapsule;
        private bool capsuleCached;

        // ─── 관통 방지: 접촉 안정화 ───
        /// <summary>마지막으로 접촉/차단된 적 콜라이더 (진동 방지용)</summary>
        private Collider2D lastBlockedEnemy;
        /// <summary>차단 후 경과 프레임 (안정화 쿨다운)</summary>
        private int blockedCooldownFrames;
        /// <summary>안정화 쿨다운 프레임 수: 이 프레임 동안 같은 적 방향 이동 차단</summary>
        private const int StabilizationFrames = 3;

        /// <summary>겹침 검사용 정적 버퍼 (GC 할당 방지)</summary>
        private static readonly Collider2D[] overlapBuffer = new Collider2D[16];

        /// <summary>
        /// 수평 이동 시 Enemy 콜라이더 관통을 방지한다.
        /// Kinematic body는 rb.position 직접 대입 시 물리 충돌이 작동하지 않으므로
        /// CapsuleCast로 수동 검사하여 적 앞에서 멈춘다.
        ///
        /// ★ 관통 방지(v3):
        ///   - skinWidth 0.06으로 확대하여 경계 불일치 영역 제거
        ///   - 겹침 시 ALL 겹침 적의 **복합 바운드**로 탈출 방향 결정
        ///     → 적 중심 기준이 아닌, 적 그룹 전체의 좌/우 경계까지 거리 비교
        ///     → 가까운 쪽으로만 탈출 허용하여 연쇄 관통 원천 차단
        ///   - 안정화 쿨다운으로 접촉 후 재접근 진동 방지
        ///   - minMoveThreshold 이하 미세 이동 무시
        /// </summary>
        protected void MoveHorizontal(float dx)
        {
            if (Mathf.Approximately(dx, 0f)) return;

            // 레이어 마스크 캐시
            if (enemyLayerMask < 0)
                enemyLayerMask = LayerMask.GetMask("Enemy");

            // 캡슐 콜라이더 캐시
            if (!capsuleCached)
            {
                cachedCapsule = context.playerRigidbody != null
                    ? context.playerRigidbody.GetComponent<CapsuleCollider2D>()
                    : null;
                capsuleCached = true;
            }

            if (cachedCapsule == null)
            {
                Vector2 pos = GetPos();
                pos.x += dx;
                MoveTo(pos);
                return;
            }

            // ─── 안정화 쿨다운 틱 ───
            if (blockedCooldownFrames > 0)
                blockedCooldownFrames--;
            else
                lastBlockedEnemy = null;

            Vector2 origin = GetPos() + cachedCapsule.offset;
            Vector2 direction = dx > 0 ? Vector2.right : Vector2.left;
            float distance = Mathf.Abs(dx);

            const float skinWidth = 0.06f;
            const float minMoveThreshold = 0.01f;

            // ─── 0단계: 안정화 쿨다운 중 같은 적 방향 이동 차단 ───
            if (lastBlockedEnemy != null && blockedCooldownFrames > 0)
            {
                float enemyX = lastBlockedEnemy.bounds.center.x;
                float playerX = GetPos().x;
                float towardEnemy = Mathf.Sign(enemyX - playerX);

                if (Mathf.Sign(dx) == Mathf.Sign(towardEnemy))
                    return;
            }

            // ─── 1단계: 겹침 검사 — ALL 겹친 적의 복합 바운드 계산 ───
            var filter = new ContactFilter2D();
            filter.SetLayerMask(enemyLayerMask);
            filter.useLayerMask = true;
            filter.useTriggers = false;

            int overlapCount = Physics2D.OverlapCapsule(
                origin, cachedCapsule.size, cachedCapsule.direction, 0f,
                filter, overlapBuffer);

            if (overlapCount > 0)
            {
                // 모든 겹침 적의 복합(Composite) 바운드 계산
                float groupLeft = float.MaxValue;
                float groupRight = float.MinValue;
                for (int i = 0; i < overlapCount; i++)
                {
                    if (overlapBuffer[i] == null) continue;
                    groupLeft = Mathf.Min(groupLeft, overlapBuffer[i].bounds.min.x);
                    groupRight = Mathf.Max(groupRight, overlapBuffer[i].bounds.max.x);
                }

                float playerX = GetPos().x;
                float halfW = cachedCapsule.size.x * 0.5f;

                // 플레이어 캡슐이 그룹 바운드를 완전히 벗어나기까지 필요한 거리
                // clearLeft: 왼쪽으로 나가려면 player.rightEdge가 groupLeft보다 작아야 함
                // clearRight: 오른쪽으로 나가려면 player.leftEdge가 groupRight보다 커야 함
                float clearLeftDist = (playerX + halfW) - groupLeft;   // ≤0이면 이미 왼쪽으로 탈출 완료
                float clearRightDist = groupRight - (playerX - halfW);  // ≤0이면 이미 오른쪽으로 탈출 완료

                // 탈출 방향 결정: 더 가까운(짧은) 쪽으로만 이동 허용
                float escapeDir;
                if (clearLeftDist <= 0f)
                    escapeDir = -1f; // 이미 왼쪽 클리어
                else if (clearRightDist <= 0f)
                    escapeDir = 1f;  // 이미 오른쪽 클리어
                else if (clearLeftDist <= clearRightDist)
                    escapeDir = -1f; // 왼쪽이 더 가까움
                else
                    escapeDir = 1f;  // 오른쪽이 더 가까움

                bool movingTowardEscape = Mathf.Sign(dx) == Mathf.Sign(escapeDir);

                if (movingTowardEscape)
                {
                    // 탈출 방향 이동 허용
                    Vector2 pos = GetPos();
                    pos.x += dx;
                    MoveTo(pos);
                }
                else
                {
                    // 탈출 반대 방향 → 차단 + 안정화 쿨다운
                    lastBlockedEnemy = overlapBuffer[0];
                    blockedCooldownFrames = StabilizationFrames;
                }
                return;
            }

            // ─── 2단계: 겹침 아님 → CapsuleCast로 전방 충돌 검사 ───
            RaycastHit2D hit = Physics2D.CapsuleCast(
                origin,
                cachedCapsule.size,
                cachedCapsule.direction,
                0f,
                direction,
                distance + skinWidth,
                enemyLayerMask);

            if (hit.collider != null)
            {
                float safeDistance = hit.distance - skinWidth;

                if (safeDistance < minMoveThreshold)
                {
                    lastBlockedEnemy = hit.collider;
                    blockedCooldownFrames = StabilizationFrames;
                    return;
                }

                Vector2 pos = GetPos();
                pos.x += direction.x * safeDistance;
                MoveTo(pos);
            }
            else
            {
                Vector2 pos = GetPos();
                pos.x += dx;
                MoveTo(pos);
            }
        }

        /// <summary>현재 프레임이 캔슬 가능 구간인지 확인</summary>
        protected bool IsInCancelWindow(int cancelStartFrame)
        {
            return context.stateFrameCounter >= cancelStartFrame;
        }

        /// <summary>프레임 수를 초 단위로 변환</summary>
        protected float FramesToSeconds(int frames)
        {
            return frames * CombatConstants.FrameDuration;
        }
    }
}
