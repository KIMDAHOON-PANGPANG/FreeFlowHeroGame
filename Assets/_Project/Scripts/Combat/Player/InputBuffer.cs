using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// 인풋 버퍼 시스템.
    /// 선입력을 저장하고 Recovery 프레임에서 소비한다.
    /// </summary>
    public class InputBuffer
    {
        private InputData bufferedInput;
        private float bufferTimeRemaining;

        /// <summary>버퍼 유지 시간 (외부에서 설정 가능, 기본 0.5초)</summary>
        public float Duration { get; set; } = 0.5f;

        /// <summary>입력을 버퍼에 저장</summary>
        public void BufferInput(InputData input)
        {
            bufferedInput = input;
            bufferTimeRemaining = Duration;
        }

        /// <summary>매 프레임 틱 (타이머 감소)</summary>
        public void Update(float deltaTime)
        {
            if (bufferTimeRemaining > 0f)
            {
                bufferTimeRemaining -= deltaTime;
                if (bufferTimeRemaining <= 0f)
                {
                    bufferedInput = null;
                }
            }
        }

        /// <summary>버퍼 소비 (소비 후 null 반환)</summary>
        public InputData Consume()
        {
            var result = bufferedInput;
            bufferedInput = null;
            bufferTimeRemaining = 0f;
            return result;
        }

        /// <summary>버퍼에 입력이 있는지 확인</summary>
        public bool HasInput => bufferedInput != null && bufferTimeRemaining > 0f;

        /// <summary>버퍼 내용 미리보기 (소비하지 않음)</summary>
        public InputData Peek => bufferedInput;

        /// <summary>버퍼 비우기</summary>
        public void Clear()
        {
            bufferedInput = null;
            bufferTimeRemaining = 0f;
        }
    }
}
