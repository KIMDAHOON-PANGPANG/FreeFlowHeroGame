using UnityEngine;

namespace FreeFlowHero.Combat.Core
{
    /// <summary>
    /// BattleSettings ScriptableObject를 런타임에 BattleSettings.Instance로 할당하는 로더.
    /// CombatSceneSetup에서 자동 생성되며, Awake에서 즉시 할당한다.
    /// </summary>
    public class BattleSettingsLoader : MonoBehaviour
    {
        [SerializeField] private BattleSettings settings;

        private void Awake()
        {
            if (settings != null)
            {
                BattleSettings.Instance = settings;
                Debug.Log("<color=cyan>[BattleSettingsLoader] BattleSettings 런타임 로드 완료</color>");
            }
            else
            {
                Debug.LogWarning("[BattleSettingsLoader] BattleSettings 에셋이 할당되지 않았습니다.");
            }
        }
    }
}
