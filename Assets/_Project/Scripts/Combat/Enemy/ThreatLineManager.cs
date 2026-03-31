using System.Collections.Generic;
using UnityEngine;
using FreeFlowHero.Combat.Core;
using FreeFlowHero.Combat.Player;

namespace FreeFlowHero.Combat.Enemy
{
    /// <summary>
    /// Threat Line Manager — 2D 사이드스크롤 포위 시스템.
    /// 3D Threat Ring을 좌우 1D 라인으로 압축.
    /// PC를 중심으로 좌/우에 슬롯을 배치하고, 적들을 슬롯에 할당하여
    /// 좌우 밸런스를 유지하며 둘러싸는 대형을 관리한다.
    ///
    /// 메뉴: REPLACED > Setup에서 자동 생성됨.
    /// </summary>
    public class ThreatLineManager : MonoBehaviour
    {
        public static ThreatLineManager Instance { get; private set; }

        // ─── ★ 데이터 튜닝: 슬롯 배치 ───
        [Header("슬롯 배치")]
        [Tooltip("한쪽(좌 또는 우)의 최대 슬롯 수")]
        [SerializeField] private int slotsPerSide = 5;

        [Tooltip("근거리 레인 — PC로부터의 X 거리 (미터)")]
        [SerializeField] private float nearLaneDistance = 2.5f;

        [Tooltip("원거리 레인 — PC로부터의 X 거리 (미터)")]
        [SerializeField] private float farLaneDistance = 5.0f;

        [Tooltip("슬롯 간 X 간격 (미터)")]
        [SerializeField] private float slotSpacing = 1.2f;

        [Header("밸런스")]
        [Tooltip("좌우 최소 적 수 (한쪽이 이보다 적으면 리밸런스)")]
        [SerializeField] private int minPerSide = 1;

        [Tooltip("슬롯 이동 보간 속도 (초)")]
        [SerializeField] private float slotMoveSpeed = 4.0f;

        [Header("화면 범위")]
        [Tooltip("화면 밖 여유 거리 (이 범위 밖의 적은 토큰 불가)")]
        [SerializeField] private float screenMargin = 1.5f;

        // ─── 내부 데이터 ───

        /// <summary>슬롯 정보</summary>
        public class Slot
        {
            public int index;              // 0부터 시작
            public int side;               // -1=왼쪽, +1=오른쪽
            public float relativeX;        // PC 기준 상대 X (부호 포함)
            public EnemyAIController occupant; // 할당된 적 (null=비어있음)

            /// <summary>이 슬롯의 월드 X 좌표</summary>
            public float GetWorldX(float playerX) => playerX + relativeX;
        }

        private readonly List<Slot> allSlots = new List<Slot>();
        private readonly List<EnemyAIController> registeredEnemies = new List<EnemyAIController>();
        private Transform playerTransform;

        // ─── Public API ───

        /// <summary>등록된 전체 적 수</summary>
        public int RegisteredCount => registeredEnemies.Count;

        /// <summary>왼쪽 적 수</summary>
        public int LeftCount { get; private set; }

        /// <summary>오른쪽 적 수</summary>
        public int RightCount { get; private set; }

        /// <summary>적이 화면 내에 있는지 확인</summary>
        public bool IsOnScreen(Vector2 pos)
        {
            if (Camera.main == null) return true;
            Vector3 vp = Camera.main.WorldToViewportPoint(pos);
            return vp.x > -screenMargin / 10f && vp.x < 1f + screenMargin / 10f;
        }

        /// <summary>적이 PC 왼쪽에 있으면 -1, 오른쪽이면 +1</summary>
        public int GetSide(EnemyAIController enemy)
        {
            if (playerTransform == null) return 1;
            return enemy.transform.position.x < playerTransform.position.x ? -1 : 1;
        }

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }
        }

        private void Start()
        {
            // 플레이어 검색
            var playerFSM = FindAnyObjectByType<PlayerCombatFSM>();
            if (playerFSM != null)
                playerTransform = playerFSM.transform;

            BuildSlots();
        }

        private void Update()
        {
            if (playerTransform == null)
            {
                var playerFSM = FindAnyObjectByType<PlayerCombatFSM>();
                if (playerFSM != null) playerTransform = playerFSM.transform;
                else return;
            }

            // 죽은 적 정리
            CleanupDead();

            // 좌우 카운트 갱신
            UpdateSideCounts();

            // 좌우 밸런스 체크 및 리밸런스
            if (registeredEnemies.Count >= 2)
                RebalanceSides();
        }

        // ─── 등록 / 해제 ───

        /// <summary>적을 Threat Line에 등록하고 슬롯을 할당한다.</summary>
        public void Register(EnemyAIController enemy)
        {
            if (enemy == null || registeredEnemies.Contains(enemy)) return;
            registeredEnemies.Add(enemy);
            AssignSlot(enemy);
            Debug.Log($"[ThreatLine] 등록: {enemy.name} → 슬롯 할당 완료 (총 {registeredEnemies.Count}명)");
        }

        /// <summary>적을 Threat Line에서 해제한다.</summary>
        public void Unregister(EnemyAIController enemy)
        {
            if (enemy == null) return;
            // 슬롯 해제
            foreach (var slot in allSlots)
            {
                if (slot.occupant == enemy)
                {
                    slot.occupant = null;
                    break;
                }
            }
            registeredEnemies.Remove(enemy);
        }

        /// <summary>적에게 할당된 슬롯의 월드 위치를 반환한다. 슬롯이 없으면 null.</summary>
        public Vector2? GetSlotPosition(EnemyAIController enemy)
        {
            if (playerTransform == null) return null;
            foreach (var slot in allSlots)
            {
                if (slot.occupant == enemy)
                {
                    float worldX = slot.GetWorldX(playerTransform.position.x);
                    return new Vector2(worldX, playerTransform.position.y);
                }
            }
            return null;
        }

        /// <summary>적의 슬롯 사이드를 반환 (-1=왼쪽, +1=오른쪽, 0=미할당)</summary>
        public int GetAssignedSide(EnemyAIController enemy)
        {
            foreach (var slot in allSlots)
            {
                if (slot.occupant == enemy)
                    return slot.side;
            }
            return 0;
        }

        // ─── 슬롯 빌드 ───

        private void BuildSlots()
        {
            allSlots.Clear();
            int idx = 0;

            for (int side = -1; side <= 1; side += 2) // -1=왼쪽, +1=오른쪽
            {
                for (int i = 0; i < slotsPerSide; i++)
                {
                    float dist = Mathf.Lerp(nearLaneDistance, farLaneDistance,
                        (float)i / Mathf.Max(1, slotsPerSide - 1));
                    allSlots.Add(new Slot
                    {
                        index = idx++,
                        side = side,
                        relativeX = side * dist,
                        occupant = null
                    });
                }
            }
        }

        // ─── 슬롯 할당 ───

        private void AssignSlot(EnemyAIController enemy)
        {
            if (playerTransform == null) return;

            float playerX = playerTransform.position.x;
            float enemyX = enemy.transform.position.x;

            // 1. 좌우 밸런스 기반 선호 사이드 결정
            int preferredSide = GetPreferredSide(enemyX, playerX);

            // 2. 선호 사이드에서 가장 가까운 빈 슬롯 찾기
            Slot bestSlot = FindNearestEmptySlot(enemyX, playerX, preferredSide);

            // 3. 선호 사이드에 빈 슬롯 없으면 반대편
            if (bestSlot == null)
                bestSlot = FindNearestEmptySlot(enemyX, playerX, -preferredSide);

            if (bestSlot != null)
                bestSlot.occupant = enemy;
        }

        private int GetPreferredSide(float enemyX, float playerX)
        {
            int leftCount = 0, rightCount = 0;
            foreach (var slot in allSlots)
            {
                if (slot.occupant == null) continue;
                if (slot.side < 0) leftCount++;
                else rightCount++;
            }

            // 한쪽이 비어있으면 그쪽 우선
            if (leftCount == 0 && rightCount > 0) return -1;
            if (rightCount == 0 && leftCount > 0) return 1;

            // 적은 쪽 우선
            if (leftCount < rightCount) return -1;
            if (rightCount < leftCount) return 1;

            // 같으면 적의 현재 위치 기준
            return enemyX < playerX ? -1 : 1;
        }

        private Slot FindNearestEmptySlot(float enemyX, float playerX, int side)
        {
            Slot best = null;
            float bestDist = float.MaxValue;

            foreach (var slot in allSlots)
            {
                if (slot.occupant != null || slot.side != side) continue;
                float slotWorldX = slot.GetWorldX(playerX);
                float dist = Mathf.Abs(enemyX - slotWorldX);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = slot;
                }
            }
            return best;
        }

        // ─── 리밸런스 ───

        private void UpdateSideCounts()
        {
            LeftCount = 0;
            RightCount = 0;
            foreach (var slot in allSlots)
            {
                if (slot.occupant == null) continue;
                if (slot.side < 0) LeftCount++;
                else RightCount++;
            }
        }

        private void RebalanceSides()
        {
            // 한쪽에 0명이면 반대편에서 1명 이동
            if (LeftCount == 0 && RightCount >= 2)
                MoveOneEnemy(fromSide: 1, toSide: -1);
            else if (RightCount == 0 && LeftCount >= 2)
                MoveOneEnemy(fromSide: -1, toSide: 1);
        }

        private void MoveOneEnemy(int fromSide, int toSide)
        {
            if (playerTransform == null) return;
            float playerX = playerTransform.position.x;

            // fromSide에서 가장 먼 적을 선택 (PC에서 멀리 있는 적이 이동)
            Slot furthestSlot = null;
            float furthestDist = 0f;

            foreach (var slot in allSlots)
            {
                if (slot.occupant == null || slot.side != fromSide) continue;
                float dist = Mathf.Abs(slot.GetWorldX(playerX) - playerX);
                if (dist > furthestDist)
                {
                    furthestDist = dist;
                    furthestSlot = slot;
                }
            }

            if (furthestSlot == null) return;

            // toSide의 가장 가까운 빈 슬롯으로 이동
            var enemy = furthestSlot.occupant;
            Slot targetSlot = FindNearestEmptySlot(enemy.transform.position.x, playerX, toSide);
            if (targetSlot == null) return;

            furthestSlot.occupant = null;
            targetSlot.occupant = enemy;

            Debug.Log($"[ThreatLine] 리밸런스: {enemy.name} " +
                $"({(fromSide < 0 ? "좌" : "우")} → {(toSide < 0 ? "좌" : "우")})");
        }

        // ─── 정리 ───

        private void CleanupDead()
        {
            for (int i = registeredEnemies.Count - 1; i >= 0; i--)
            {
                var enemy = registeredEnemies[i];
                if (enemy == null || !enemy.gameObject.activeInHierarchy)
                {
                    Unregister(enemy);
                    continue;
                }

                // DummyEnemyTarget이 있으면 HP 체크
                var target = enemy.GetComponent<DummyEnemyTarget>();
                if (target != null && !target.IsTargetable)
                    Unregister(enemy);
            }
        }

        // ─── Gizmos ───

        private void OnDrawGizmos()
        {
            if (playerTransform == null) return;
            float px = playerTransform.position.x;
            float py = playerTransform.position.y;

            foreach (var slot in allSlots)
            {
                float wx = slot.GetWorldX(px);
                bool occupied = slot.occupant != null;
                Gizmos.color = occupied
                    ? (slot.side < 0 ? Color.red : Color.blue)
                    : new Color(0.5f, 0.5f, 0.5f, 0.3f);
                Gizmos.DrawWireSphere(new Vector3(wx, py, 0f), 0.2f);
            }
        }
    }
}
