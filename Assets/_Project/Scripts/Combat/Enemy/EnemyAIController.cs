using UnityEngine;
using FreeFlowHero.Combat.Core;
using FreeFlowHero.Combat.Player;

namespace FreeFlowHero.Combat.Enemy
{
    /// <summary>
    /// 기본 적 AI 컨트롤러.
    /// 순찰 → 추적 → 텔레그래프 → 공격 → 대기 사이클.
    /// DummyEnemyTarget에 부착하여 적에게 기본 행동 패턴을 부여한다.
    ///
    /// ★ Phase 3 리팩토링:
    ///   - Dynamic → Kinematic Rigidbody2D 전환
    ///   - rb.linearVelocity → rb.position 직접 이동 (수동 충돌 검사)
    ///   - 수동 중력 (Ground 레이캐스트)
    ///   - CapsuleCast 기반 Player/Enemy 관통 방지
    /// </summary>
    [RequireComponent(typeof(DummyEnemyTarget))]
    public class EnemyAIController : MonoBehaviour, ITelegraphable
    {
        // ─── 설정 (Inspector) ───
        [Header("감지")]
        [Tooltip("플레이어 감지 거리")]
        [SerializeField] private float detectionRange = 8f;
        [Tooltip("공격 가능 거리")]
        [SerializeField] private float attackRange = 1.8f;

        [Header("순찰")]
        [Tooltip("순찰 이동 속도")]
        [SerializeField] private float patrolSpeed = 1.5f;
        [Tooltip("순찰 반경 (스폰 지점 기준)")]
        [SerializeField] private float patrolRadius = 3f;
        [Tooltip("순찰 대기 시간")]
        [SerializeField] private float patrolWaitTime = 1.5f;

        [Header("추적")]
        [Tooltip("추적 이동 속도")]
        [SerializeField] private float chaseSpeed = 2.5f;

        [Header("공격")]
        [Tooltip("텔레그래프 지속 시간 (초)")]
        [SerializeField] private float telegraphDuration = 0.4f;
        [Tooltip("공격 쿨다운 (초)")]
        [SerializeField] private float attackCooldown = 1.8f;
        [Tooltip("공격 데미지")]
        [SerializeField] private float attackDamage = 10f;
        [Tooltip("텔레그래프 타입 (Red=회피만, Yellow=카운터 가능)")]
        [SerializeField] private TelegraphType telegraphType = TelegraphType.Yellow_Counter;

        [Header("피격")]
        [Tooltip("피격 경직 시간")]
        [SerializeField] private float hitStunDuration = 0.35f;

        // ─── 상태 ───
        private enum AIState
        {
            Idle,
            Patrol,
            Chase,
            Telegraph,
            Attack,
            HitStun,
            Dead
        }

        private AIState currentState = AIState.Idle;
        private float stateTimer;
        private float cooldownTimer;

        // ─── 참조 ───
        private DummyEnemyTarget enemyTarget;
        private Rigidbody2D rb;
        private Animator animator;
        private SpriteRenderer spriteRenderer;
        private Color originalColor;
        private Transform playerTransform;
        private PlayerCombatFSM playerFSM;

        // ─── 순찰 ───
        private Vector2 spawnPos;
        private float patrolDir = 1f;  // 순찰 방향

        // ─── 텔레그래프 (ITelegraphable) ───
        private TelegraphType currentTelegraph;
        private int telegraphStartFrame;
        private bool isTelegraphing;

        // ─── ITelegraphable 구현 ───
        public TelegraphType CurrentTelegraph => currentTelegraph;
        public int TelegraphStartFrame => telegraphStartFrame;
        public bool IsTelegraphing => isTelegraphing;

        // ─── 시각 피드백 ───
        private static readonly Color TelegraphRedColor = new Color(1f, 0.2f, 0.2f, 1f);
        private static readonly Color TelegraphYellowColor = new Color(1f, 0.9f, 0.1f, 1f);
        private static readonly Color HitStunColor = new Color(1f, 1f, 1f, 0.6f);

        // ─── 피격 감지용 ───
        private float lastHP;

        // ─── Kinematic 이동 & 충돌 ───
        private CapsuleCollider2D cachedCapsule;
#pragma warning disable CS0414 // isGrounded is assigned but never used (intended for gravity system)
        private bool isGrounded;
#pragma warning restore CS0414
        private float verticalVelocity;
        private const float Gravity = -30f;         // 수동 중력 가속도
        private const float GroundCheckDist = 0.15f; // 지면 감지 레이 거리
        private const float SkinWidth = 0.06f;       // 충돌 여유 거리
        private const float MinMoveThreshold = 0.005f;

        /// <summary>충돌 검사 대상: Player + Enemy 레이어</summary>
        private static int collisionMask = -1;
        private static int groundMask = -1;

        /// <summary>겹침 검사 버퍼 (GC 방지)</summary>
        private static readonly Collider2D[] overlapBuffer = new Collider2D[16];

        private void Awake()
        {
            enemyTarget = GetComponent<DummyEnemyTarget>();
            rb = GetComponent<Rigidbody2D>();
            animator = GetComponent<Animator>();
            cachedCapsule = GetComponent<CapsuleCollider2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
                originalColor = spriteRenderer.color;

            spawnPos = transform.position;

            // ★ Kinematic 강제 전환 (런타임 안전장치)
            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.useFullKinematicContacts = true;
            }
        }

        private void Start()
        {
            // 레이어 마스크 캐시
            if (collisionMask < 0)
                collisionMask = LayerMask.GetMask("Player", "Enemy");
            if (groundMask < 0)
                groundMask = LayerMask.GetMask("Ground");

            // 플레이어 검색 (지연 안전)
            playerFSM = FindAnyObjectByType<PlayerCombatFSM>();
            if (playerFSM != null)
                playerTransform = playerFSM.transform;

            lastHP = enemyTarget.CurrentHP;
            cooldownTimer = Random.Range(1f, attackCooldown); // 초기 랜덤 쿨다운
            TransitionTo(AIState.Patrol);
        }

        private void Update()
        {
            // 플레이어 지연 검색
            if (playerTransform == null)
            {
                playerFSM = FindAnyObjectByType<PlayerCombatFSM>();
                if (playerFSM != null) playerTransform = playerFSM.transform;
                else return;
            }

            // 사망 체크
            if (!enemyTarget.IsTargetable)
            {
                if (currentState != AIState.Dead)
                    TransitionTo(AIState.Dead);
                return;
            }

            // 피격 감지 (HP 감소 → HitStun)
            float curHP = enemyTarget.CurrentHP;
            if (curHP < lastHP && currentState != AIState.HitStun && currentState != AIState.Dead)
            {
                TransitionTo(AIState.HitStun);
            }
            lastHP = curHP;

            // ★ 수동 중력 적용
            ApplyGravity();

            // 쿨다운 틱
            if (cooldownTimer > 0f)
                cooldownTimer -= Time.deltaTime;

            // 상태별 업데이트
            stateTimer -= Time.deltaTime;

            switch (currentState)
            {
                case AIState.Idle:    UpdateIdle();    break;
                case AIState.Patrol:  UpdatePatrol();  break;
                case AIState.Chase:   UpdateChase();   break;
                case AIState.Telegraph: UpdateTelegraph(); break;
                case AIState.Attack:  UpdateAttack();  break;
                case AIState.HitStun: UpdateHitStun(); break;
                case AIState.Dead:    break;
            }
        }

        // ────────────────────────────
        //  수동 중력 & 지면 검사
        // ────────────────────────────

        private void ApplyGravity()
        {
            if (rb == null) return;

            Vector2 pos = rb.position;

            // 캡슐 형상 캐시
            float capsuleOffsetY  = cachedCapsule != null ? cachedCapsule.offset.y  : 0f;
            float capsuleHalfH    = cachedCapsule != null ? cachedCapsule.size.y * 0.5f : 0f;

            // 캡슐 하단 Y: 피벗 기준
            float capsuleBottomY = pos.y + capsuleOffsetY - capsuleHalfH;

            // 낙하 속도에 따라 레이 거리를 늘려 빠른 낙하 시 땅 관통 방지
            float dynamicDist = GroundCheckDist + Mathf.Max(0f, -verticalVelocity * Time.deltaTime) + 0.05f;
            Vector2 rayOrigin = new Vector2(pos.x, capsuleBottomY + 0.05f);

            RaycastHit2D groundHit = Physics2D.Raycast(rayOrigin, Vector2.down, dynamicDist, groundMask);

            if (groundHit.collider != null)
            {
                isGrounded = true;
                verticalVelocity = 0f;

                // ★ 피벗 스냅 위치 = 지면 표면 Y + 캡슐 반높이 - 캡슐 오프셋
                //   capsuleBottomY = pos.y + offsetY - halfH 이므로
                //   pos.y = groundY - offsetY + halfH 로 역산
                float targetPivotY = groundHit.point.y - capsuleOffsetY + capsuleHalfH;
                if (pos.y < targetPivotY || pos.y - targetPivotY > 0.1f)
                {
                    pos.y = targetPivotY;
                    rb.position = pos;
                }
            }
            else
            {
                isGrounded = false;
                verticalVelocity += Gravity * Time.deltaTime;
                pos.y += verticalVelocity * Time.deltaTime;
                rb.position = pos;
            }
        }

        // ────────────────────────────
        //  상태 전환
        // ────────────────────────────

        private void TransitionTo(AIState newState)
        {
            // 이전 상태 정리
            switch (currentState)
            {
                case AIState.Telegraph:
                    isTelegraphing = false;
                    currentTelegraph = TelegraphType.None;
                    RestoreColor();
                    // 텔레그래프 중 피격 등으로 중단 시 슬롯 반환
                    if (AttackCoordinator.Instance != null)
                        AttackCoordinator.Instance.ReleaseAttackSlot(this);
                    break;
                case AIState.HitStun:
                    RestoreColor();
                    break;
                case AIState.Attack:
                    RestoreColor();
                    // 공격 중 강제 전환 시 슬롯 반환
                    if (AttackCoordinator.Instance != null)
                        AttackCoordinator.Instance.ReleaseAttackSlot(this);
                    break;
            }

            currentState = newState;

            switch (newState)
            {
                case AIState.Idle:
                    stateTimer = 0.5f;
                    break;

                case AIState.Patrol:
                    stateTimer = patrolWaitTime + Random.Range(0f, 1f);
                    break;

                case AIState.Chase:
                    stateTimer = 5f; // 최대 추적 시간
                    break;

                case AIState.Telegraph:
                    stateTimer = telegraphDuration;
                    isTelegraphing = true;
                    currentTelegraph = telegraphType;
                    telegraphStartFrame = Time.frameCount;

                    // 시각: 텔레그래프 색상
                    if (spriteRenderer != null)
                        spriteRenderer.color = telegraphType == TelegraphType.Red_Dodge
                            ? TelegraphRedColor : TelegraphYellowColor;

                    // 애니메이션
                    SafeSetTrigger("Telegraph");

                    // 이벤트 발행
                    CombatEventBus.Publish(new OnEnemyTelegraph
                    {
                        Enemy = enemyTarget,
                        Type = telegraphType,
                        TelegraphDuration = telegraphDuration
                    });


                    break;

                case AIState.Attack:
                    stateTimer = 0.3f; // 공격 지속
                    cooldownTimer = attackCooldown + Random.Range(-0.3f, 0.5f);

                    // 애니메이션: 랜덤 공격 인덱스 선택
                    SafeSetInteger("AttackIndex", Random.Range(0, 5));
                    SafeSetTrigger("Attack");

                    ExecuteAttack();
                    break;

                case AIState.HitStun:
                    stateTimer = hitStunDuration;
                    isTelegraphing = false;
                    currentTelegraph = TelegraphType.None;
                    if (spriteRenderer != null)
                        spriteRenderer.color = HitStunColor;

                    // 애니메이션
                    SafeSetTrigger("HitStun");
                    break;

                case AIState.Dead:
                    isTelegraphing = false;

                    // 애니메이션
                    SafeSetTrigger("Die");
                    break;
            }
        }

        // ────────────────────────────
        //  상태 업데이트
        // ────────────────────────────

        private void UpdateIdle()
        {
            float dist = GetDistToPlayer();
            if (dist < detectionRange)
            {
                TransitionTo(AIState.Chase);
                return;
            }
            if (stateTimer <= 0f)
                TransitionTo(AIState.Patrol);
        }

        private void UpdatePatrol()
        {
            float dist = GetDistToPlayer();
            if (dist < detectionRange)
            {
                TransitionTo(AIState.Chase);
                return;
            }

            // 순찰 이동
            Vector2 pos = GetPos();
            float nextX = pos.x + patrolDir * patrolSpeed * Time.deltaTime;

            // 순찰 범위 체크
            if (Mathf.Abs(nextX - spawnPos.x) > patrolRadius)
            {
                patrolDir *= -1f;
                stateTimer = patrolWaitTime;
                FaceDirection(patrolDir);
                return;
            }

            if (stateTimer > 0f)
            {
                // 대기 중
                return;
            }

            // 이동 (충돌 검사 포함)
            MoveHorizontal(patrolDir * patrolSpeed * Time.deltaTime);
            FaceDirection(patrolDir);
        }

        private void UpdateChase()
        {
            float dist = GetDistToPlayer();
            float dir = Mathf.Sign(playerTransform.position.x - transform.position.x);

            // 감지 범위 크게 벗어나면 → Idle (넉백으로 멀리 갔어도 재추적 유지)
            if (dist > detectionRange * 2f)
            {
                TransitionTo(AIState.Idle);
                return;
            }

            // 공격 범위 안 + 쿨다운 완료 → AttackCoordinator에 슬롯 요청 → 텔레그래프
            if (dist <= attackRange && cooldownTimer <= 0f)
            {
                // AttackCoordinator가 있으면 슬롯 요청, 없으면 바로 진행
                if (AttackCoordinator.Instance == null
                    || AttackCoordinator.Instance.RequestAttackSlot(this))
                {
                    TransitionTo(AIState.Telegraph);
                }
                // else: 슬롯 거부됨 → 대기열에 등록됨, 다음 프레임에 재시도
                return;
            }

            // 항상 플레이어 바라보기
            FaceDirection(dir);

            // 공격 범위 밖 → 추적 이동
            if (dist > attackRange)
            {
                MoveHorizontal(dir * chaseSpeed * Time.deltaTime);
            }
            else if (cooldownTimer > 0f)
            {
                // 쿨다운 대기 중: 너무 가까우면 살짝 후퇴, 적당하면 제자리
                if (dist < attackRange * 0.5f)
                {
                    MoveHorizontal(-dir * patrolSpeed * 0.6f * Time.deltaTime);
                }
                // else: 제자리 대기 (이동 없음)
            }
            else
            {
                // 범위 안 + 쿨다운 없음 → 접근
                MoveHorizontal(dir * chaseSpeed * 0.5f * Time.deltaTime);
            }
        }

        private void UpdateTelegraph()
        {
            // 텔레그래프 완료 → 공격
            if (stateTimer <= 0f)
            {
                TransitionTo(AIState.Attack);
            }
        }

        private void UpdateAttack()
        {
            if (stateTimer <= 0f)
            {
                // 공격 슬롯 반환
                if (AttackCoordinator.Instance != null)
                    AttackCoordinator.Instance.ReleaseAttackSlot(this);
                TransitionTo(AIState.Chase);
            }
        }

        private void UpdateHitStun()
        {
            if (stateTimer <= 0f)
                TransitionTo(AIState.Chase);
        }

        // ────────────────────────────
        //  공격 실행
        // ────────────────────────────

        private void ExecuteAttack()
        {
            if (playerFSM == null || playerTransform == null) return;

            float dist = GetDistToPlayer();
            if (dist > attackRange * 1.5f)
            {

                return;
            }

            // HitData 생성
            Vector2 myPos = transform.position;
            Vector2 playerPos = playerTransform.position;
            Vector2 knockDir = (playerPos - myPos).normalized;

            var hitData = new HitData
            {
                AttackType = AttackType.Light,
                AttackerTeam = CombatTeam.Enemy,
                BaseDamage = attackDamage,
                KnockbackDirection = knockDir,
                IsComboAttack = false,
                ComboCount = 0,
                IsExecutionKill = false,
                IsLaunchAttack = false,
                IsKnockdown = false,
                AttackerPosition = myPos,
                ContactPoint = Vector2.Lerp(myPos, playerPos, 0.7f)
            };

            // 플레이어 피격
            playerFSM.OnPlayerHit(hitData);

            // 이벤트 발행
            CombatEventBus.Publish(new OnPlayerHit { HitData = hitData });


        }

        // ────────────────────────────
        //  Kinematic 이동 + 충돌 방지
        // ────────────────────────────

        /// <summary>현재 위치 (Rigidbody2D 기준)</summary>
        private Vector2 GetPos()
        {
            return rb != null ? rb.position : (Vector2)transform.position;
        }

        /// <summary>위치 설정 (Kinematic)</summary>
        private void MoveTo(Vector2 newPos)
        {
            if (rb != null)
                rb.position = newPos;
            else
                transform.position = new Vector3(newPos.x, newPos.y, transform.position.z);
        }

        /// <summary>
        /// Kinematic 수평 이동 + Player/Enemy 충돌 방지.
        /// CapsuleCast로 전방 충돌 검사 → 충돌 시 안전 거리에서 정지.
        /// 이미 겹쳐 있으면 탈출 방향으로만 이동 허용.
        /// </summary>
        private void MoveHorizontal(float dx)
        {
            if (Mathf.Approximately(dx, 0f)) return;
            if (cachedCapsule == null)
            {
                Vector2 pos = GetPos();
                pos.x += dx;
                MoveTo(pos);
                return;
            }

            Vector2 origin = GetPos() + cachedCapsule.offset;
            Vector2 direction = dx > 0 ? Vector2.right : Vector2.left;
            float distance = Mathf.Abs(dx);

            // ─── 1단계: 겹침 검사 (이미 관통된 경우) ───
            var filter = new ContactFilter2D();
            filter.SetLayerMask(collisionMask);
            filter.useLayerMask = true;
            filter.useTriggers = false;

            int overlapCount = Physics2D.OverlapCapsule(
                origin, cachedCapsule.size, cachedCapsule.direction, 0f,
                filter, overlapBuffer);

            // 자기 자신 제외
            int actualOverlap = 0;
            for (int i = 0; i < overlapCount; i++)
            {
                if (overlapBuffer[i] == null) continue;
                if (overlapBuffer[i].gameObject == gameObject) continue;
                // 자기 자신의 자식(Hurtbox 등)도 제외
                if (overlapBuffer[i].transform.IsChildOf(transform)) continue;
                overlapBuffer[actualOverlap++] = overlapBuffer[i];
            }

            if (actualOverlap > 0)
            {
                // 복합 바운드 계산
                float groupLeft = float.MaxValue;
                float groupRight = float.MinValue;
                for (int i = 0; i < actualOverlap; i++)
                {
                    groupLeft = Mathf.Min(groupLeft, overlapBuffer[i].bounds.min.x);
                    groupRight = Mathf.Max(groupRight, overlapBuffer[i].bounds.max.x);
                }

                float myX = GetPos().x;
                float halfW = cachedCapsule.size.x * 0.5f;
                float clearLeftDist = (myX + halfW) - groupLeft;
                float clearRightDist = groupRight - (myX - halfW);

                float escapeDir;
                if (clearLeftDist <= 0f)
                    escapeDir = -1f;
                else if (clearRightDist <= 0f)
                    escapeDir = 1f;
                else if (clearLeftDist <= clearRightDist)
                    escapeDir = -1f;
                else
                    escapeDir = 1f;

                bool movingTowardEscape = Mathf.Sign(dx) == Mathf.Sign(escapeDir);
                if (movingTowardEscape)
                {
                    Vector2 pos = GetPos();
                    pos.x += dx;
                    MoveTo(pos);
                }
                // else: 탈출 반대 방향 → 이동 차단
                return;
            }

            // ─── 2단계: CapsuleCast 전방 충돌 ───
            RaycastHit2D hit = Physics2D.CapsuleCast(
                origin,
                cachedCapsule.size,
                cachedCapsule.direction,
                0f,
                direction,
                distance + SkinWidth,
                collisionMask);

            // 자기 자신 히트 무시 (희귀하지만 안전)
            if (hit.collider != null && (hit.collider.gameObject == gameObject
                || hit.collider.transform.IsChildOf(transform)))
            {
                hit = default;
            }

            if (hit.collider != null)
            {
                float safeDistance = hit.distance - SkinWidth;
                if (safeDistance < MinMoveThreshold)
                    return; // 너무 가까움 → 이동 불가

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

        private float GetDistToPlayer()
        {
            if (playerTransform == null) return float.MaxValue;
            return Mathf.Abs(playerTransform.position.x - transform.position.x);
        }

        private void FaceDirection(float dir)
        {
            if (Mathf.Approximately(dir, 0f)) return;
            Vector3 scale = transform.localScale;
            scale.x = Mathf.Abs(scale.x) * (dir >= 0 ? 1f : -1f);
            transform.localScale = scale;
        }

        private void RestoreColor()
        {
            if (spriteRenderer != null)
                spriteRenderer.color = originalColor;
        }

        // ─── Animator 안전 호출 ───

        private void SafeSetTrigger(string triggerName)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            try { animator.SetTrigger(triggerName); }
            catch (System.Exception e) { Debug.LogWarning($"[EnemyAI] Animator 오류 무시: {e.Message}"); }
        }

        private void SafeSetInteger(string paramName, int value)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            try { animator.SetInteger(paramName, value); }
            catch (System.Exception e) { Debug.LogWarning($"[EnemyAI] Animator 오류 무시: {e.Message}"); }
        }

        // ─── 디버그 ───
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 pos = Application.isPlaying ? (Vector3)(Vector2)transform.position : transform.position;

            // 감지 범위 (파란)
            Gizmos.color = new Color(0.3f, 0.5f, 1f, 0.3f);
            Gizmos.DrawWireSphere(pos, detectionRange);

            // 공격 범위 (빨강)
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(pos, attackRange);

            // 순찰 범위 (초록)
            Vector3 sp = Application.isPlaying ? (Vector3)spawnPos : pos;
            Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.3f);
            Gizmos.DrawLine(sp + Vector3.left * patrolRadius, sp + Vector3.right * patrolRadius);
        }
#endif
    }
}
