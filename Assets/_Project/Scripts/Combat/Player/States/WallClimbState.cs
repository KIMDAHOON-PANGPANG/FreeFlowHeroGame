using UnityEngine;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// 벽타기 상태 (프린스 오브 페르시아 스타일).
    /// Shift + 벽 접촉 시 진입. 벽에 달라붙어 상하 이동.
    /// Space → 월 점프 (벽 반대 방향으로 튕김).
    /// W/S → 등반/하강. 입력 없으면 매달려 정지.
    /// 벽 꼭대기 도달 시 자동으로 위로 올라감.
    /// </summary>
    public class WallClimbState : CombatState
    {
        public override string StateName => "WallClimb";

        // ★ 데이터 튜닝
        private const float ClimbSpeed = 4f;         // 등반 속도
        private const float WallJumpForceX = 8f;     // 월 점프 수평 힘
        private const float WallJumpForceY = 10f;    // 월 점프 수직 힘
        private const float WallDetectDist = 0.6f;   // 벽 감지 레이 거리
        private const float SlideSpeed = 1f;         // 벽 미끄러짐 속도 (0이면 미끄러짐 없음)
        private const float WallTopClimbOffset = 0.5f; // 벽 꼭대기 넘어가는 오프셋

        private int wallSide;       // -1=좌측벽, +1=우측벽
        private int wallLayerMask;
        private int groundLayerMask;
        private float wallTopY;     // 벽 상단 Y 좌표

        public override void Enter()
        {
            base.Enter();

            // 레이어 마스크 캐시
            int wl = LayerMask.NameToLayer("Wall");
            wallLayerMask = wl >= 0 ? (1 << wl) : 0;
            int gl = LayerMask.NameToLayer("Ground");
            groundLayerMask = gl >= 0 ? (1 << gl) : 0;

            // 벽 감지 — facing 방향으로 레이캐스트
            float facing = context.playerTransform.localScale.x >= 0 ? 1f : -1f;
            Vector2 origin = GetPos() + new Vector2(0f, 0.9f);
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.right * facing, WallDetectDist, wallLayerMask);

            if (hit.collider != null)
            {
                wallSide = (int)facing;
                context.wallClimbSide = wallSide;
                context.isWallClimbing = true;
                context.verticalVelocity = 0f;

                // 벽 상단 Y 계산
                wallTopY = hit.collider.bounds.max.y;

                // 벽에 밀착 위치 조정
                Vector2 pos = GetPos();
                float wallX = hit.point.x - wallSide * 0.3f; // 벽 표면에서 0.3m 떨어진 위치
                pos.x = wallX;
                MoveTo(pos);

                // facing 반전 (벽 반대 방향 바라봄 — 프린스 오브 페르시아 스타일)
                Vector3 scale = context.playerTransform.localScale;
                scale.x = Mathf.Abs(scale.x) * (-wallSide);
                context.playerTransform.localScale = scale;
            }
            else
            {
                // 벽 감지 실패 → 즉시 복귀
                context.isWallClimbing = false;
                fsm.TransitionTo<JumpState>();
            }
        }

        public override void Exit()
        {
            base.Exit();
            context.isWallClimbing = false;
            context.wallClimbSide = 0;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // 벽 감지 유지 확인
            Vector2 origin = GetPos() + new Vector2(0f, 0.9f);
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.right * wallSide, WallDetectDist, wallLayerMask);

            if (hit.collider == null)
            {
                // 벽 감지 실패 → 낙하
                context.isWallClimbing = false;
                fsm.TransitionTo<JumpState>();
                return;
            }

            // 벽 상단 갱신
            wallTopY = hit.collider.bounds.max.y;

            // 상하 이동
            float inputY = context.lastInputDirection.y;
            Vector2 pos = GetPos();

            if (inputY > 0.1f)
            {
                // 위로 등반
                pos.y += ClimbSpeed * deltaTime;

                // 벽 꼭대기 도달 → 위로 올라감
                if (pos.y + 0.9f >= wallTopY)
                {
                    pos.y = wallTopY + WallTopClimbOffset;
                    pos.x += wallSide * 0.5f; // 벽 위로 약간 이동
                    MoveTo(pos);
                    context.isWallClimbing = false;
                    context.verticalVelocity = 0f;
                    fsm.TransitionTo<IdleState>();
                    return;
                }
            }
            else if (inputY < -0.1f)
            {
                // 아래로 하강
                pos.y -= ClimbSpeed * deltaTime;

                // 바닥 도달 체크
                RaycastHit2D groundHit = Physics2D.Raycast(
                    pos + new Vector2(0f, 0.1f), Vector2.down, 0.3f, groundLayerMask);
                if (groundHit.collider != null)
                {
                    context.isWallClimbing = false;
                    context.verticalVelocity = 0f;
                    fsm.TransitionTo<IdleState>();
                    return;
                }
            }
            // else: 정지 (매달려 있음)

            MoveTo(pos);
        }

        public override void HandleInput(InputData input)
        {
            switch (input.Type)
            {
                case InputType.Jump: // Space → 월 점프
                    context.isWallClimbing = false;

                    // 벽 반대 방향으로 수평 밀어냄 + 상승
                    context.verticalVelocity = WallJumpForceY;

                    // facing 전환 (벽 반대 방향으로 날아가므로)
                    Vector3 scale = context.playerTransform.localScale;
                    scale.x = Mathf.Abs(scale.x) * (-wallSide); // 이미 반전된 상태이므로 벽 방향으로
                    context.playerTransform.localScale = scale;

                    // 수평 이동 적용
                    Vector2 pos = GetPos();
                    pos.x -= wallSide * 0.5f; // 벽에서 즉시 떨어짐
                    MoveTo(pos);

                    // JumpState로 전환 (수평 관성은 JumpState에서 처리)
                    // wallJumpHorizontalVelocity를 context에 저장하여 JumpState에서 사용
                    context.wallClimbSide = -wallSide; // 월 점프 방향 힌트
                    fsm.TransitionTo<JumpState>();
                    break;

                case InputType.Dodge: // Shift 다시 → 벽에서 놓기
                    context.isWallClimbing = false;
                    context.verticalVelocity = 0f;
                    fsm.TransitionTo<JumpState>();
                    break;
            }
        }
    }
}
