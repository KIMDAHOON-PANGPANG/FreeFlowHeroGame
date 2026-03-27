using System.Collections.Generic;
using UnityEngine;

namespace FreeFlowHero.Combat.Core
{
    /// <summary>
    /// AnimatorOverrideController를 사용하여 JSON(ActionTable)의 clipPath 기반으로
    /// 런타임에 애니메이션 클립을 교체하는 컴포넌트.
    ///
    /// ★ 핵심 아키텍처:
    ///   1. 베이스 AnimatorController는 한 번만 빌드 (AnimatorControllerBuilder)
    ///   2. 이 컴포넌트가 Play 진입 시 clipPath에서 클립을 로드하여 Override 적용
    ///   3. JSON 수정(GUI 또는 바이브 코딩) → 핫 리로드 → Override 자동 갱신
    ///   → AnimatorController 리빌드 불필요!
    ///
    /// 사용법:
    ///   Player GameObject에 부착. actorId를 Inspector에서 설정 (기본: "PC_Hero").
    ///   Animator는 자동 검색.
    /// </summary>
    public class AnimatorClipOverrider : MonoBehaviour
    {
        // ★ 데이터 튜닝: 대상 액터 ID
        [Header("★ 설정")]
        [Tooltip("ActionTable에서 읽을 액터 ID")]
        [SerializeField] private string actorId = "PC_Hero";

        // ─── 내부 참조 ───
        private Animator animator;
        private RuntimeAnimatorController baseController;
        private AnimatorOverrideController overrideController;

        /// <summary>
        /// actionId → Animator 상태에 할당된 원본 클립 이름 매핑.
        /// AnimatorControllerBuilder가 생성한 상태와 PC_Hero.json의 clip 필드를 연결한다.
        /// ★ 새 액션 타입 추가 시 여기에도 매핑 추가 필요.
        /// </summary>
        private static readonly Dictionary<string, string> ActionToBaseClipName = new()
        {
            // 1~3타: Martial Art Animations Sample
            { "LightAtk1", "Atk_P_1" },
            { "LightAtk2", "Atk_P_2" },
            { "LightAtk3", "Atk_P_3" },    // 베이스 빌드 시점의 원본 클립명
            // 4타: EEJANAI knee strike
            { "LightAtk4", "knee strike" },
            // 나머지 전투 액션 (EEJANAI)
            { "HeavyAtk",  "charge fist" },
            { "Counter",    "spinning elbow" },
            // 필요 시 추가...
        };

        private void Start()
        {
            // Animator 검색 (PlayerCombatFSM과 동일한 로직)
            animator = GetComponent<Animator>();
            if (animator == null || !animator.enabled)
            {
                foreach (var anim in GetComponentsInChildren<Animator>(true))
                {
                    if (anim.enabled && anim.runtimeAnimatorController != null)
                    {
                        animator = anim;
                        break;
                    }
                }
            }

            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning("[ClipOverrider] Animator 또는 Controller를 찾을 수 없습니다.");
                return;
            }

            // 베이스 컨트롤러 저장 (Override가 아닌 원본)
            if (animator.runtimeAnimatorController is AnimatorOverrideController existing)
                baseController = existing.runtimeAnimatorController;
            else
                baseController = animator.runtimeAnimatorController;

            ApplyOverrides();

            // 핫 리로드 구독
            if (ActionTableManager.Instance != null)
                ActionTableManager.Instance.OnReloaded += ApplyOverrides;
        }

        private void OnDestroy()
        {
            if (ActionTableManager.Instance != null)
                ActionTableManager.Instance.OnReloaded -= ApplyOverrides;
        }

        /// <summary>
        /// JSON clipPath를 읽어 AnimatorOverrideController로 클립 교체 적용.
        /// Play 진입 시 + 핫 리로드 시 호출.
        /// </summary>
        public void ApplyOverrides()
        {
            if (animator == null || baseController == null) return;

            var table = ActionTableManager.Instance?.GetActorTable(actorId);
            if (table == null)
            {
                Debug.LogWarning($"[ClipOverrider] 액터 테이블 없음: {actorId}");
                return;
            }

            // Override 컨트롤러 생성
            overrideController = new AnimatorOverrideController(baseController);

            // 현재 Override 목록 가져오기
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(
                overrideController.overridesCount);
            overrideController.GetOverrides(overrides);

            int appliedCount = 0;

            // 각 액션의 clipPath로 클립 교체
            if (table.actions != null)
            {
                foreach (var action in table.actions)
                {
                    if (string.IsNullOrEmpty(action.clipPath)) continue;

                    // clipPath에서 AnimationClip 로드
                    AnimationClip newClip = LoadClipFromPath(action.clipPath);
                    if (newClip == null)
                    {
                        Debug.LogWarning($"[ClipOverrider] 클립 로드 실패: {action.id} → {action.clipPath}");
                        continue;
                    }

                    // 원본 클립 이름 결정 (매핑 테이블 우선, 없으면 action.clip 사용)
                    string baseClipName;
                    if (!ActionToBaseClipName.TryGetValue(action.id, out baseClipName))
                        baseClipName = action.clip;

                    if (string.IsNullOrEmpty(baseClipName)) continue;

                    // Override 목록에서 원본 클립 찾아서 교체
                    for (int i = 0; i < overrides.Count; i++)
                    {
                        var originalClip = overrides[i].Key;
                        if (originalClip != null && originalClip.name == baseClipName)
                        {
                            overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(
                                originalClip, newClip);
                            appliedCount++;
                            // break 안 함: 같은 클립이 여러 상태에 쓰일 수 있음
                        }
                    }
                }
            }

            overrideController.ApplyOverrides(overrides);
            animator.runtimeAnimatorController = overrideController;

            Debug.Log($"<color=lime>[ClipOverrider] Override 적용 완료 — {appliedCount}개 클립 교체</color>");
        }

        /// <summary>FBX 에셋 경로에서 첫 번째 AnimationClip을 로드한다.</summary>
        private AnimationClip LoadClipFromPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;

#if UNITY_EDITOR
            // 에디터 Play 모드: AssetDatabase로 직접 로드
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                        return clip;
                }
            }
            Debug.LogWarning($"[ClipOverrider] FBX에서 클립 미발견: {assetPath}");
            return null;
#else
            // 빌드 환경: 현재 미지원 (프로토타입 단계)
            Debug.LogWarning("[ClipOverrider] 빌드 환경에서는 clipPath Override 미지원. Addressables 필요.");
            return null;
#endif
        }
    }
}
