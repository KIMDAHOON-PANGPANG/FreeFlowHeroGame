using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Enemy
{
    /// <summary>
    /// 대치 연출 컴포넌트.
    /// Circling 상태의 적에게 살아있는 느낌을 부여하는 행동을 관리한다.
    /// Feint(페인트), Taunt(도발), Guard Stance(방어 자세) 등을
    /// 가중치 랜덤으로 선택하여 실행한다.
    ///
    /// EnemyAIController에 부착하여 사용.
    /// </summary>
    public class IdleThreatBehavior : MonoBehaviour
    {
        // ─── ★ 데이터 튜닝: 행동 타이밍 ───
        [Header("행동 간격")]
        [Tooltip("행동 사이 최소 대기 시간 (초)")]
        [SerializeField] private float minActionInterval = 2.0f;

        [Tooltip("행동 사이 최대 대기 시간 (초)")]
        [SerializeField] private float maxActionInterval = 6.0f;

        [Header("페인트")]
        [Tooltip("페인트 전진 거리 (미터)")]
        [SerializeField] private float feintDistance = 0.5f;

        [Tooltip("페인트 전진 속도")]
        [SerializeField] private float feintSpeed = 4.0f;

        [Tooltip("페인트 총 시간 (초) — 전진 + 후퇴")]
        [SerializeField] private float feintDuration = 0.4f;

        [Header("가중치 (높을수록 자주 발생)")]
        [Tooltip("아무 것도 안 함")]
        [SerializeField] private float weightIdle = 3.0f;

        [Tooltip("페인트 (짧게 전진→후퇴)")]
        [SerializeField] private float weightFeint = 2.0f;

        [Tooltip("도발 (소리/모션)")]
        [SerializeField] private float weightTaunt = 1.0f;

        // ─── 상태 ───
        private enum ThreatAction
        {
            None,
            Feint,
            Taunt
        }

        private ThreatAction currentAction = ThreatAction.None;
        private float actionTimer;
        private float nextActionTimer;

        // 페인트 상태
        private float feintProgress;
        private float feintDir; // PC 방향 (+1 또는 -1)

        // 참조
        private EnemyAIController aiController;
        private Rigidbody2D rb;
        private Transform playerTransform;

        /// <summary>현재 대치 행동이 실행 중인지</summary>
        public bool IsActionActive => currentAction != ThreatAction.None;

        private void Awake()
        {
            aiController = GetComponent<EnemyAIController>();
            rb = GetComponent<Rigidbody2D>();
        }

        private void Start()
        {
            nextActionTimer = Random.Range(minActionInterval, maxActionInterval);

            // 플레이어 검색
            var playerFSM = FindAnyObjectByType<Player.PlayerCombatFSM>();
            if (playerFSM != null)
                playerTransform = playerFSM.transform;
        }

        /// <summary>
        /// Circling 상태에서 매 프레임 호출.
        /// 행동 타이머를 갱신하고, 실행 중인 행동을 업데이트한다.
        /// </summary>
        public void UpdateThreatBehavior(float deltaTime)
        {
            if (playerTransform == null)
            {
                var playerFSM = FindAnyObjectByType<Player.PlayerCombatFSM>();
                if (playerFSM != null) playerTransform = playerFSM.transform;
                else return;
            }

            // 현재 행동 진행 중이면 업데이트
            if (currentAction != ThreatAction.None)
            {
                actionTimer -= deltaTime;
                UpdateCurrentAction(deltaTime);

                if (actionTimer <= 0f)
                    EndAction();
                return;
            }

            // 다음 행동 타이머 감소
            nextActionTimer -= deltaTime;
            if (nextActionTimer <= 0f)
            {
                SelectAndStartAction();
                nextActionTimer = Random.Range(minActionInterval, maxActionInterval);
            }
        }

        /// <summary>행동 강제 중단 (피격 등)</summary>
        public void CancelAction()
        {
            currentAction = ThreatAction.None;
            actionTimer = 0f;
        }

        // ─── 행동 선택 ───

        private void SelectAndStartAction()
        {
            float totalWeight = weightIdle + weightFeint + weightTaunt;
            float roll = Random.Range(0f, totalWeight);

            if (roll < weightIdle)
            {
                // 아무것도 안 함
                return;
            }
            roll -= weightIdle;

            if (roll < weightFeint)
            {
                StartFeint();
                return;
            }
            roll -= weightFeint;

            StartTaunt();
        }

        // ─── 페인트 ───

        private void StartFeint()
        {
            currentAction = ThreatAction.Feint;
            actionTimer = feintDuration;
            feintProgress = 0f;

            // PC 방향으로 전진
            if (playerTransform != null)
                feintDir = Mathf.Sign(playerTransform.position.x - transform.position.x);
            else
                feintDir = 1f;
        }

        // ─── 도발 ───

        private void StartTaunt()
        {
            currentAction = ThreatAction.Taunt;
            actionTimer = 0.6f; // 도발 모션 시간

            // TODO: 도발 애니메이션 트리거 (Animator에 Taunt 상태 추가 후)
            // SafeSetTrigger("Taunt");
        }

        // ─── 행동 업데이트 ───

        private void UpdateCurrentAction(float deltaTime)
        {
            switch (currentAction)
            {
                case ThreatAction.Feint:
                    UpdateFeint(deltaTime);
                    break;
                case ThreatAction.Taunt:
                    // 도발은 타이머 소진만 대기
                    break;
            }
        }

        private void UpdateFeint(float deltaTime)
        {
            if (rb == null) return;

            float halfTime = feintDuration * 0.5f;
            feintProgress += deltaTime;

            Vector2 pos = rb.position;
            if (feintProgress < halfTime)
            {
                // 전반: PC 방향으로 전진
                pos.x += feintDir * feintSpeed * deltaTime;
            }
            else
            {
                // 후반: 후퇴
                pos.x -= feintDir * feintSpeed * deltaTime;
            }
            rb.position = pos;
        }

        private void EndAction()
        {
            currentAction = ThreatAction.None;
        }
    }
}
