using UnityEngine;
using UnityEngine.InputSystem;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// 전투 입력 핸들러 (New Input System 사용).
    /// 마우스 + 키보드 폴링 방식으로 PlayerCombatFSM에 전달한다.
    ///
    /// 조작 매핑:
    ///   기본 공격: Mouse LB (왼쪽 클릭)
    ///   강공격:    Mouse RB (오른쪽 클릭)
    ///   회피:      Keyboard Shift
    ///   카운터:    Keyboard L
    ///   헉슬리:    Keyboard U
    ///   이동:      Keyboard WASD·방향키
    /// </summary>
    [RequireComponent(typeof(PlayerCombatFSM))]
    public class CombatInputHandler : MonoBehaviour
    {
        private PlayerCombatFSM fsm;

        // ─── 디버그 ───
        private string lastInput = "—";
        private float inputIndicatorTimer;
        private int inputCount;

        private void Awake()
        {
            fsm = GetComponent<PlayerCombatFSM>();

        }

        private void Start()
        {

        }

        private void Update()
        {
            // 인디케이터 타이머
            if (inputIndicatorTimer > 0f)
                inputIndicatorTimer -= Time.deltaTime;

            var kb = Keyboard.current;
            var mouse = Mouse.current;

            // ─── 방향 입력 (키보드 WASD / 방향키) ───
            Vector2 direction = Vector2.zero;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) direction.x -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) direction.x += 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) direction.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) direction.y -= 1f;
                if (direction.sqrMagnitude > 1f) direction.Normalize();
            }

            fsm.Context.lastInputDirection = direction;

            // ─── 기본 공격: Mouse 왼쪽 클릭 ───
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                inputCount++;
                lastInput = $"Attack #{inputCount}";
                inputIndicatorTimer = 0.5f;

                fsm.OnCombatInput(new InputData(InputType.Attack, direction));
            }

            // ─── 강공격: Mouse 오른쪽 클릭 ───
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                inputCount++;
                lastInput = $"Heavy #{inputCount}";
                inputIndicatorTimer = 0.5f;

                fsm.OnCombatInput(new InputData(InputType.Heavy, direction));
            }

            // ─── 회피: Keyboard Shift ───
            if (kb != null && kb.leftShiftKey.wasPressedThisFrame)
            {
                inputCount++;
                lastInput = $"Dodge #{inputCount}";
                inputIndicatorTimer = 0.5f;

                fsm.OnCombatInput(new InputData(InputType.Dodge, direction));
            }

            // ─── 처형: Keyboard F ───
            if (kb != null && kb.fKey.wasPressedThisFrame)
            {
                inputCount++;
                lastInput = $"Execute #{inputCount}";
                inputIndicatorTimer = 0.5f;

                fsm.OnCombatInput(new InputData(InputType.Execute, direction));
            }

            // ─── 헉슬리: Keyboard U ───
            if (kb != null && kb.uKey.wasPressedThisFrame)
            {
                inputCount++;
                lastInput = $"Huxley #{inputCount}";
                inputIndicatorTimer = 0.5f;

                fsm.OnCombatInput(new InputData(InputType.Huxley, direction));
            }
        }

        /// <summary>입력 감지 상태를 화면 하단에 표시</summary>
        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerLeft
            };
            style.normal.textColor = inputIndicatorTimer > 0f ? Color.green : Color.gray;

            string text = inputIndicatorTimer > 0f
                ? $"INPUT: {lastInput}"
                : "LClick:Attack | RClick:Guard | Shift:Dodge | F:Execute | U:Huxley";

            GUI.Label(new Rect(10, Screen.height - 40, 600, 30), text, style);

            // ── 현재 상태 + 액션 표시 ──
            var actionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft
            };
            actionStyle.normal.textColor = Color.white;
            string stateName = fsm.CurrentStateName;
            string actionId = fsm.CurrentActionId;
            string actionText = stateName == "Strike" ? $"{stateName} : {actionId}" : stateName;
            GUI.Label(new Rect(10, 10, 400, 35), actionText, actionStyle);
        }
    }
}
