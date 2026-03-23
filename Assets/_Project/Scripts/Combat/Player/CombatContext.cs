using System.Collections.Generic;
using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// 플레이어 전투 공유 컨텍스트.
    /// FSM의 모든 상태가 이 데이터를 읽고/쓸 수 있다.
    /// </summary>
    [System.Serializable]
    public class CombatContext
    {
        // ─── 캔슬 타이밍 (Inspector에서 조절) ───
        [Header("★ 캔슬 타이밍 (초)")]
        [Tooltip("Strike 진입 후 몇 초 뒤에 다음 공격으로 캔슬 가능한지")]
        [Range(0.05f, 1.0f)]
        public float strikeCancelDelay = 0.3f;

        [Tooltip("Dodge 진입 후 캔슬 가능 시간")]
        [Range(0.05f, 1.0f)]
        public float dodgeCancelDelay = 0.3f;

        [Tooltip("Counter 진입 후 캔슬 가능 시간")]
        [Range(0.05f, 1.0f)]
        public float counterCancelDelay = 0.3f;

        [Tooltip("인풋 버퍼 유지 시간 (이 시간 안에 캔슬 윈도우가 열리면 소비됨)")]
        [Range(0.1f, 1.0f)]
        public float inputBufferDuration = 0.5f;

        // ─── 콤보 ───
        [Header("콤보")]
        public int comboCount;
        public float comboWindowTimer;
        public int comboChainIndex;         // 현재 콤보 체인 인덱스 (0~2)
        public int lastFinalComboCount;     // 마지막 콤보 끊겼을 때의 카운트

        // ─── 타겟 ───
        [Header("타겟")]
        public Transform currentTarget;
        public List<ICombatTarget> activeEnemies = new();

        // ─── 게이지 ───
        [Header("게이지")]
        public float huxleyGauge;

        // ─── 입력 ───
        [Header("입력")]
        public Vector2 lastInputDirection;   // 마지막 입력 방향 (Dodge 방향 결정 등)

        // ─── 상태 플래그 ───
        [Header("상태")]
        public bool isWarping;
        public bool isInvulnerable;
        public bool canCancel;

        // ─── 프레임 ───
        [Header("프레임")]
        public int stateFrameCounter;       // 현재 상태 진입 후 경과 프레임
        public int globalFrameCounter;      // 전체 프레임 카운터

        // ─── 참조 ───
        [Header("참조")]
        public Rigidbody2D playerRigidbody;
        public Animator playerAnimator;
        public Transform playerTransform;

        /// <summary>콤보 리셋</summary>
        public void ResetCombo()
        {
            lastFinalComboCount = comboCount;
            if (comboCount > 0)
            {
                CombatEventBus.Publish(new OnComboBreak { FinalComboCount = comboCount });
            }
            comboCount = 0;
            comboChainIndex = 0;
            comboWindowTimer = 0f;
        }

        /// <summary>콤보 증가</summary>
        public void IncrementCombo(int amount = 1)
        {
            int prev = comboCount;
            comboCount = Mathf.Min(comboCount + amount, CombatConstants.MaxComboCount);
            comboWindowTimer = CombatConstants.ComboWindowDuration;

            CombatEventBus.Publish(new OnComboChanged
            {
                ComboCount = comboCount,
                PreviousCount = prev
            });
        }

        /// <summary>헉슬리 게이지 충전</summary>
        public void ChargeHuxley(float amount)
        {
            huxleyGauge = Mathf.Clamp(huxleyGauge + amount, 0f, CombatConstants.HuxleyMaxCharge);
            CombatEventBus.Publish(new OnHuxleyChargeChanged
            {
                ChargePercent = huxleyGauge / CombatConstants.HuxleyMaxCharge
            });
        }

        /// <summary>콤보 윈도우 틱 (매 프레임 호출)</summary>
        public void UpdateComboWindow(float deltaTime)
        {
            if (comboWindowTimer > 0f)
            {
                comboWindowTimer -= deltaTime;
                if (comboWindowTimer <= 0f && comboCount > 0)
                {
                    ResetCombo();
                }
            }
        }

        /// <summary>상태 프레임 카운터 리셋</summary>
        public void ResetStateFrame()
        {
            stateFrameCounter = 0;
        }

        /// <summary>글로벌 프레임 틱</summary>
        public void TickFrame()
        {
            stateFrameCounter++;
            globalFrameCounter++;
        }
    }
}
