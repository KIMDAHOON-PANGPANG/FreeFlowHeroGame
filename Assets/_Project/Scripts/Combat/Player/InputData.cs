using UnityEngine;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// 전투 입력 데이터.
    /// InputSystem에서 변환된 전투 전용 입력 구조체.
    /// </summary>
    public class InputData
    {
        public InputType Type { get; set; }
        public Vector2 Direction { get; set; }
        public float Timestamp { get; set; }

        public InputData(InputType type, Vector2 direction)
        {
            Type = type;
            Direction = direction;
            Timestamp = Time.time;
        }
    }

    /// <summary>전투 입력 타입</summary>
    public enum InputType
    {
        Attack,     // 기본 공격 (Light)
        Heavy,      // 강공격 / 가드
        Dodge,      // 회피 / 벽타기
        Huxley,     // 헉슬리 건
        Execute,    // 처형 (F키)
        Jump        // 점프 (Space)
    }
}
