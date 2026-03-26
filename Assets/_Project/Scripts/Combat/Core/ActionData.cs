using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreeFlowHero.Combat.Core
{
    // ═══════════════════════════════════════════════════════
    //  액션 테이블 데이터 구조
    //  JSON ↔ C# 직렬화용. JsonUtility 호환 (public 필드).
    //  모든 액터(PC, 몬스터, 보스 등)가 공유하는 통합 포맷.
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// 캔슬 경로: 특정 입력이 들어오면 어떤 액션으로 전이하는지 정의.
    /// input = 입력 타입 문자열 ("Attack", "Dodge", "Counter", "Heavy", "Huxley", "Move" 등)
    /// next  = 전이할 액션 ID 문자열 ("LightAtk2", "Dodge" 등)
    /// </summary>
    [Serializable]
    public class CancelRoute
    {
        public string input;
        public string next;
    }

    /// <summary>
    /// 단일 액션 엔트리.
    /// 프레임 데이터, 캔슬 테이블, 애니메이션 클립 이름 등 액션의 모든 정보를 담는다.
    /// </summary>
    [Serializable]
    public class ActionEntry
    {
        public string id;            // 고유 ID (예: "LightAtk1")
        public string name;          // 표시 이름 (예: "기본공격 1타")
        public string clip;          // 애니메이션 클립 이름 (EEJANAI FBX 매핑)

        // ─── 프레임 데이터 (60fps 기준) ───
        public int startup;          // 선딜 프레임
        public int active;           // 히트 판정 활성 프레임
        public int recovery;         // 후딜 프레임

        // ─── 재생 배율 ★ 데이터 튜닝 ───
        public float playbackRate = 1.0f;  // 애니메이션 재생 배율 (1.0 = 원본 속도)

        // ─── 트랜지션 블렌딩 ★ 데이터 튜닝 ───
        public float transitionIn = 0.0f;   // 이전 액션 → 이 액션 진입 블렌딩 시간 (초)
        public float transitionOut = 0.0f;  // 이 액션 → 다음 액션 퇴장 블렌딩 시간 (초)

        // ─── 캔슬 설정 ───
        public float cancelRatio;    // Recovery 캔슬 비율 (0.0~1.0)
        public float moveSpeed;      // 액션 중 이동 속도

        // ─── 캔슬 경로 ───
        public CancelRoute[] cancels;  // 입력별 캔슬 가능 액션
        public string defaultNext;     // 모션 끝까지 재생 후 입력 없을 때 전이할 액션

        // ─── 태그 ───
        public string[] tags;          // 분류 태그 (light, heavy, combo, dodge, counter 등)

        // ─── 노티파이 타임라인 ───
        public ActionNotify[] notifies;  // 타임라인 노티파이 배열 (STARTUP, COLLISION, CANCEL_WINDOW 등)

        // ─── 계산 프로퍼티 ───

        /// <summary>총 프레임 수</summary>
        public int TotalFrames => startup + active + recovery;

        /// <summary>캔슬 가능 시점 (초)</summary>
        public float CancelDelay => (startup + active + recovery * cancelRatio) * CombatConstants.FrameDuration;

        /// <summary>총 지속 시간 (초)</summary>
        public float TotalDuration => TotalFrames * CombatConstants.FrameDuration;

        /// <summary>특정 입력에 대한 캔슬 대상 액션 ID 반환. 없으면 null.</summary>
        public string GetCancelTarget(string inputType)
        {
            if (cancels == null) return null;
            for (int i = 0; i < cancels.Length; i++)
            {
                if (string.Equals(cancels[i].input, inputType, StringComparison.OrdinalIgnoreCase))
                    return cancels[i].next;
            }
            return null;
        }

        /// <summary>특정 태그를 가지고 있는지 확인</summary>
        public bool HasTag(string tag)
        {
            if (tags == null) return false;
            for (int i = 0; i < tags.Length; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>노티파이가 있는지 확인</summary>
        public bool HasNotifies => notifies != null && notifies.Length > 0;

        /// <summary>노티파이 기반 총 프레임 수 (노티파이 중 가장 큰 endFrame)</summary>
        public int NotifyTotalFrames
        {
            get
            {
                if (!HasNotifies) return TotalFrames;
                int max = 0;
                for (int i = 0; i < notifies.Length; i++)
                {
                    if (notifies[i].endFrame > max)
                        max = notifies[i].endFrame;
                }
                return Mathf.Max(max, 1);
            }
        }

        /// <summary>특정 프레임에서 활성인 노티파이 중 지정 타입 검색</summary>
        public ActionNotify GetActiveNotify(int frame, NotifyType type)
        {
            if (notifies == null) return null;
            string typeStr = type.ToString();
            for (int i = 0; i < notifies.Length; i++)
            {
                if (notifies[i].type == typeStr && notifies[i].ContainsFrame(frame))
                    return notifies[i];
            }
            return null;
        }

        /// <summary>특정 타입의 모든 노티파이 검색</summary>
        public ActionNotify[] GetNotifiesByType(NotifyType type)
        {
            if (notifies == null) return System.Array.Empty<ActionNotify>();
            string typeStr = type.ToString();
            var list = new System.Collections.Generic.List<ActionNotify>();
            for (int i = 0; i < notifies.Length; i++)
            {
                if (notifies[i].type == typeStr)
                    list.Add(notifies[i]);
            }
            return list.ToArray();
        }
    }

    /// <summary>
    /// 액터별 액션 테이블. 하나의 JSON 파일 = 하나의 ActorActionTable.
    /// PC_Hero.json, Enemy_Grunt.json 등 액터 유형마다 하나씩.
    /// </summary>
    [Serializable]
    public class ActorActionTable
    {
        public string actorId;         // 액터 고유 ID (예: "PC_Hero")
        public string actorName;       // 표시 이름 (예: "주인공")
        public ActionEntry[] actions;  // 모든 액션 목록

        // ─── 런타임 캐시 (JSON 직렬화 대상 아님) ───
        [NonSerialized] private Dictionary<string, ActionEntry> actionMap;

        /// <summary>
        /// 액션 ID로 빠른 조회. 첫 호출 시 Dictionary 빌드.
        /// </summary>
        public ActionEntry GetAction(string actionId)
        {
            if (actionMap == null)
                BuildMap();

            actionMap.TryGetValue(actionId, out var entry);
            return entry;
        }

        /// <summary>모든 액션 ID 목록 반환</summary>
        public IEnumerable<string> GetAllActionIds()
        {
            if (actionMap == null)
                BuildMap();
            return actionMap.Keys;
        }

        /// <summary>Dictionary 캐시 빌드</summary>
        public void BuildMap()
        {
            actionMap = new Dictionary<string, ActionEntry>(StringComparer.OrdinalIgnoreCase);
            if (actions == null) return;
            for (int i = 0; i < actions.Length; i++)
            {
                if (actions[i] != null && !string.IsNullOrEmpty(actions[i].id))
                    actionMap[actions[i].id] = actions[i];
            }
        }

        /// <summary>캐시 무효화 (JSON 리로드 후 호출)</summary>
        public void InvalidateCache()
        {
            actionMap = null;
        }
    }
}
