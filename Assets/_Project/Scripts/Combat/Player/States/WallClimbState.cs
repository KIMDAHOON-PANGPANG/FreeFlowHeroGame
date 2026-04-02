using UnityEngine;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// 벽타기 상태 (프린스 오브 페르시아 스타일).
    /// Ground/플랫폼의 측면(절벽 면)에 달라붙어 등반.
    /// 별도 Wall 오브젝트가 아닌, Ground 레이어의 옆면을 감지한다.
    ///
    /// 진입: 공중에서 Shift + Ground 측면 감지
    /// W/S: 등반/하강
    /// Space: 월 점프 (반대 방향)
    /// Shift: 벽에서 놓기
    /// 꼭대기 도달: 자동으로 플랫폼 위로 올라감
    /// </summary>
    public class WallClimbState : CombatState
    {
        public override string StateName => "WallClimb";

        // ★ 데이터 튜닝
        private const float ClimbSpeed = 4f;
        private const float WallJumpForceY = 10f;
        private const float WallDetectDist = 0.8f;      // 측면 감지 레이 거리
        private const float WallTopClimbOffset = 0.3f;   // 꼭대기 올라갈 때 Y 오프셋
        private const float WallSnapOffset = 0.35f;      // 벽면에서 캐릭터 중심까지 거리
        private const float MaxClimbDuration = 2.0f;     // ★ 벽타기 최대 시간 (초)

        private int wallSide;       // -1=좌측벽, +1=우측벽
        private int climbMask;      // Ground + Wall 레이어
        private int groundLayerMask;
        private Collider2D climbSurface; // 현재 붙어있는 콜라이더
        private float climbTimer;   // 벽타기 경과 시간

        public override void Enter()
        {
            base.Enter();

            climbTimer = 0f;

            // 레이어 마스크: Ground + Wall 양쪽 측면 모두 클라이밍 가능
            int gl = LayerMask.NameToLayer("Ground");
            int wl = LayerMask.NameToLayer("Wall");
            groundLayerMask = gl >= 0 ? (1 << gl) : 0;
            climbMask = groundLayerMask;
            if (wl >= 0) climbMask |= (1 << wl);

            // 측면 감지 — facing 방향으로 레이캐스트
            float facing = context.playerTransform.localScale.x >= 0 ? 1f : -1f;

            // 여러 높이에서 레이캐스트 (발, 허리, 머리)
            RaycastHit2D hit = default;
            float[] heights = { 0.3f, 0.9f, 1.5f };
            foreach (float h in heights)
            {
                Vector2 origin = GetPos() + new Vector2(0f, h);
                hit = Physics2D.Raycast(origin, Vector2.right * facing, WallDetectDist, climbMask);
                if (hit.collider != null) break;
            }

            if (hit.collider != null)
            {
                wallSide = (int)facing;
                climbSurface = hit.collider;
                context.wallClimbSide = wallSide;
                context.isWallClimbing = true;
                context.verticalVelocity = 0f;

                // 벽면에 밀착
                Vector2 pos = GetPos();
                pos.x = hit.point.x - wallSide * WallSnapOffset;
                MoveTo(pos);

                // facing 반전 (벽 반대 방향 바라봄)
                Vector3 scale = context.playerTransform.localScale;
                scale.x = Mathf.Abs(scale.x) * (-wallSide);
                context.playerTransform.localScale = scale;
            }
            else
            {
                // 측면 감지 실패 → 복귀
                context.isWallClimbing = false;
                fsm.TransitionTo<JumpState>();
            }
        }

        public override void Exit()
        {
            base.Exit();
            context.isWallClimbing = false;
            context.wallClimbSide = 0;
            climbSurface = null;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // ★ 벽타기 시간 제한 (2초 후 자동 낙하)
            climbTimer += deltaTime;
            if (climbTimer >= MaxClimbDuration)
            {
                context.isWallClimbing = false;
                context.verticalVelocity = 0f;
                fsm.TransitionTo<JumpState>();
                return;
            }

            // 측면 감지 유지 확인 (허리 높이)
            Vector2 checkOrigin = GetPos() + new Vector2(0f, 0.9f);
            RaycastHit2D hit = Physics2D.Raycast(checkOrigin, Vector2.right * wallSide, WallDetectDist, climbMask);

            if (hit.collider == null)
            {
                // 측면 없음 → 꼭대기에 도달했거나 벽 끝
                // 위쪽으로 올라갈 수 있는지 체크
                if (TryClimbOver())
                    return;

                // 올라갈 곳 없음 → 낙하
                context.isWallClimbing = false;
                fsm.TransitionTo<JumpState>();
                return;
            }

            // X 위치 유지 (벽면에 밀착)
            Vector2 pos = GetPos();
            pos.x = hit.point.x - wallSide * WallSnapOffset;

            // 상하 이동
            float inputY = context.lastInputDirection.y;

            if (inputY > 0.1f)
            {
                pos.y += ClimbSpeed * deltaTime;
            }
            else if (inputY < -0.1f)
            {
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

            MoveTo(pos);
        }

        /// <summary>꼭대기 넘어가기: 위에 발판이 있으면 올라감</summary>
        private bool TryClimbOver()
        {
            Vector2 pos = GetPos();

            // 현재 위치에서 위+벽 방향으로 발판 체크
            Vector2 checkPos = pos + new Vector2(wallSide * 0.5f, 1.5f);
            RaycastHit2D downHit = Physics2D.Raycast(checkPos, Vector2.down, 2f, groundLayerMask);

            if (downHit.collider != null)
            {
                // 발판 위로 올라감
                pos.x += wallSide * 0.8f;
                pos.y = downHit.point.y + WallTopClimbOffset;
                MoveTo(pos);

                // facing을 벽 방향으로 복원
                Vector3 scale = context.playerTransform.localScale;
                scale.x = Mathf.Abs(scale.x) * wallSide;
                context.playerTransform.localScale = scale;

                context.isWallClimbing = false;
                context.verticalVelocity = 0f;
                fsm.TransitionTo<IdleState>();
                return true;
            }

            return false;
        }

        public override void HandleInput(InputData input)
        {
            switch (input.Type)
            {
                case InputType.Jump: // Space → 월 점프
                    context.isWallClimbing = false;
                    context.verticalVelocity = WallJumpForceY;

                    // 벽 반대 방향으로 밀어냄
                    Vector2 pos = GetPos();
                    pos.x -= wallSide * 0.8f;
                    MoveTo(pos);

                    // facing 전환 (벽 반대 방향)
                    Vector3 scale = context.playerTransform.localScale;
                    scale.x = Mathf.Abs(scale.x) * (-wallSide);
                    context.playerTransform.localScale = scale;

                    context.wallClimbSide = -wallSide;
                    fsm.TransitionTo<JumpState>();
                    break;

                case InputType.Dodge: // Shift → 벽에서 놓기
                    context.isWallClimbing = false;
                    context.verticalVelocity = 0f;
                    fsm.TransitionTo<JumpState>();
                    break;

                case InputType.Attack: // 공중 공격
                    context.isWallClimbing = false;
                    fsm.TransitionTo<StrikeState>();
                    break;
            }
        }
    }
}
