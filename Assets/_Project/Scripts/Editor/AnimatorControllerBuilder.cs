using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace FreeFlowHero.Editor
{
    /// <summary>
    /// EEJANAI 애니메이션 팩의 FBX 클립을 전투 액션에 매핑한 AnimatorController를 자동 생성한다.
    /// 메뉴: REPLACED > Setup > 3. Build Animator Controller
    /// </summary>
    public static class AnimatorControllerBuilder
    {
        private const string AnimatorPath = "Assets/_Project/Animations/Player/PlayerCombatAnimator.controller";
        private const string EEJANAIRoot = "Assets/EEJANAI_Team/FreeFighterAnimations";
        private const string FBXFolder = EEJANAIRoot + "/FBX";
        private const string AnimFolder = EEJANAIRoot + "/Animations";

        // ─── Locomotion FBX ───
        private const string LocomotionRoot =
            "Assets/ExplosiveLLC/Fighter Pack Bundle FREE/Fighters/" +
            "Female Fighter Mecanim Animation Pack FREE/Animations";
        // Idle: Martial Art Animations Sample의 Fight_Idle (리타겟팅으로 자연스러운 전투 대기)
        private const string IdleFBX = "Assets/Martial Art Animations Sample/Animations/Fight_Idle.fbx";
        private const string WalkFBX = LocomotionRoot + "/Female@WalkForward.FBX";
        // Run은 WalkForward를 속도 1.5배로 사용 (임시)

        // ─── Martial Art Animations Sample (1~3타 콤보) ───
        private const string MartialArtRoot = "Assets/Martial Art Animations Sample/Animations";
        private const string Atk1FBX = MartialArtRoot + "/Atk_P_1.fbx";
        private const string Atk2FBX = MartialArtRoot + "/Atk_P_2.fbx";
        private const string Atk3FBX = MartialArtRoot + "/Atk_K_1.fbx";

        // ─── 4타 (EEJANAI knee strike) ───
        private const string Atk4FBX = FBXFolder + "/knee strike.fbx";

        // ─── 히트 리액션 클립 (Martial Art) ───
        private const string FlinchFBX = MartialArtRoot + "/Hit_A.fbx";
        private const string KnockdownFBX = MartialArtRoot + "/Knock_A.fbx";

        // 애니메이션 → 전투 액션 매핑 (1~3타: Martial Art, 나머지: EEJANAI)
        private static readonly (string fbxName, string stateName, string triggerName)[] AnimMap = new[]
        {
            // Idle/Walk/Run은 Locomotion BlendTree에서 별도 처리
            // 1~3타는 절대경로 로드 (아래 Execute에서 별도 처리)
            ("combo",              "Strike_ComboChain",  "Strike"),
            ("low kick",           "Strike_LowKick",     "Strike"),
            ("charge fist",        "HeavyAttack",        "Heavy"),
            ("spinning elbow",     "Counter_Normal",     "Counter"),
            ("back kick",          "Counter_Perfect",    "CounterPerfect"),
            ("front sweep",        "DodgeAttack",        "DodgeAttack"),
            ("cressent kick",      "Execution_1",        "Execution"),
            ("axe kick",           "Execution_2",        "Execution"),
            ("spinning axe kick",  "Execution_3",        "Execution"),
            ("jumping uppercut",   "Launch",             "Launch"),
            ("jumping side kick",  "AirFinisher",        "AirFinisher"),
            ("webster side kick",  "HuxleyFinisher",     "HuxleyFinisher"),
            ("super blast",        "HuxleyShot",         "HuxleyShot"),
        };

        [MenuItem("REPLACED/Setup/3. Build Animator Controller", priority = 3)]
        public static void Execute()
        {
            EnsureFolder("Assets/_Project/Animations");
            EnsureFolder("Assets/_Project/Animations/Player");

            // 기존 컨트롤러가 있으면 삭제 후 재생성
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorPath) != null)
            {
                AssetDatabase.DeleteAsset(AnimatorPath);
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(AnimatorPath);

            // ─── 파라미터 등록 ───
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float); // Locomotion 블렌드
            controller.AddParameter("ComboIndex", AnimatorControllerParameterType.Int);
            controller.AddParameter("Idle", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Strike", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Heavy", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Counter", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("CounterPerfect", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("DodgeAttack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dodge", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Execution", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Launch", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("AirFinisher", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("HuxleyShot", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("HuxleyFinisher", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Flinch", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Knockdown", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("CounterStrike", AnimatorControllerParameterType.Trigger);

            // ─── 베이스 레이어 ───
            var rootStateMachine = controller.layers[0].stateMachine;

            // ─── Locomotion 블렌드 트리 (Idle/Walk/Run) ───
            AnimatorState locomotionState = CreateLocomotionBlendTree(
                rootStateMachine, controller);
            rootStateMachine.defaultState = locomotionState;
            int clipFoundCount = 0;

            // ─── 1~4타 콤보 상태 (1~3타: Martial Art, 4타: EEJANAI knee strike) ───
            int stateCount = 0;
            string[] atkFBXPaths = { Atk1FBX, Atk2FBX, Atk3FBX, Atk4FBX };
            string[] atkStateNames = { "Strike_LightAtk1", "Strike_LightAtk2", "Strike_LightAtk3", "Strike_LightAtk4" };
            // FBX 이름 → FindClipByFBXName 폴백용 (EEJANAI 등 .anim 추출된 에셋 대응)
            string[] atkFBXFallbackNames = { null, null, null, "knee strike" };
            for (int i = 0; i < atkFBXPaths.Length; i++)
            {
                AnimationClip clip = LoadClipFromFBX(atkFBXPaths[i]);

                // FBX 직접 로드 실패 시 이름으로 폴백 검색 (.anim 추출된 에셋 대응)
                if (clip == null && atkFBXFallbackNames[i] != null)
                {
                    clip = FindClipByFBXName(atkFBXFallbackNames[i]);
                    if (clip != null)
                        Debug.Log($"[AnimBuilder] ✓ {atkStateNames[i]} 클립 (폴백 검색): {clip.name} ({clip.length:F2}초)");
                }

                var state = rootStateMachine.AddState(atkStateNames[i],
                    GetStatePosition(stateCount + 1));
                stateCount++;

                if (clip != null)
                {
                    state.motion = clip;
                    clipFoundCount++;
                    if (atkFBXFallbackNames[i] == null) // 폴백이 아닌 경우만 로그 (폴백은 위에서 이미 출력)
                        Debug.Log($"[AnimBuilder] ✓ {atkStateNames[i]} 클립: {clip.name} ({clip.length:F2}초)");
                }
                else
                {
                    Debug.LogWarning($"[AnimBuilder] ❌ {atkStateNames[i]} 클립 미발견: {atkFBXPaths[i]}");
                }
            }

            // ─── 나머지 전투 액션 상태 (EEJANAI) ───
            foreach (var (fbxName, stateName, triggerName) in AnimMap)
            {
                // FBX에서 AnimationClip 찾기
                AnimationClip clip = FindClipByFBXName(fbxName);

                // 상태 생성
                var state = rootStateMachine.AddState(stateName,
                    GetStatePosition(stateCount + 1)); // +1: Locomotion이 0번
                stateCount++;

                if (clip != null)
                {
                    state.motion = clip;
                    clipFoundCount++;
                }
                else
                {
                    Debug.LogWarning($"[AnimBuilder] 클립 미발견: \"{fbxName}\" → {stateName} (빈 상태로 생성)");
                }

                // 트리거 전환: Any State → 이 상태
                if (triggerName != "Strike") // Strike는 ComboIndex로 분기
                {
                    var transition = rootStateMachine.AddAnyStateTransition(state);
                    transition.AddCondition(AnimatorConditionMode.If, 0, triggerName);
                    transition.hasExitTime = false;
                    transition.duration = 0.05f;
                    transition.canTransitionToSelf = false;
                }
            }

            // ─── Strike 콤보 분기 (ComboIndex 기반) ───
            SetupStrikeComboTransitions(rootStateMachine, controller);

            // ─── CounterStrike → Counter_Normal 상태로 전환 ───
            AnimatorState counterNormalState = FindState(rootStateMachine, "Counter_Normal");
            if (counterNormalState != null)
            {
                var csTransition = rootStateMachine.AddAnyStateTransition(counterNormalState);
                csTransition.AddCondition(AnimatorConditionMode.If, 0, "CounterStrike");
                csTransition.hasExitTime = false;
                csTransition.duration = 0.05f;
                csTransition.canTransitionToSelf = false;
            }

            // ─── Flinch 상태 (경직 피격) ───
            {
                var flinchState = rootStateMachine.AddState("Flinch", GetStatePosition(stateCount + 1));
                stateCount++;
                AnimationClip flinchClip = LoadClipFromFBX(FlinchFBX);
                if (flinchClip != null)
                {
                    flinchState.motion = flinchClip;
                    clipFoundCount++;
                }

                var tr = rootStateMachine.AddAnyStateTransition(flinchState);
                tr.AddCondition(AnimatorConditionMode.If, 0, "Flinch");
                tr.hasExitTime = false;
                tr.duration = 0.02f;
                tr.canTransitionToSelf = true; // 연속 피격 시 리셋
            }

            // ─── Knockdown 상태 (넉다운 에어본) ───
            {
                var knockdownState = rootStateMachine.AddState("Knockdown", GetStatePosition(stateCount + 1));
                stateCount++;
                AnimationClip knockClip = LoadClipFromFBX(KnockdownFBX);
                if (knockClip != null)
                {
                    knockdownState.motion = knockClip;
                    clipFoundCount++;
                }

                var tr = rootStateMachine.AddAnyStateTransition(knockdownState);
                tr.AddCondition(AnimatorConditionMode.If, 0, "Knockdown");
                tr.hasExitTime = false;
                tr.duration = 0.05f;
                tr.canTransitionToSelf = false;
            }

            // ─── 모든 전투 상태 → Locomotion 복귀 (Exit Time) ───
            foreach (var childState in rootStateMachine.states)
            {
                if (childState.state != locomotionState)
                {
                    var toLocomotion = childState.state.AddTransition(locomotionState);
                    toLocomotion.hasExitTime = true;
                    toLocomotion.exitTime = 0.9f;
                    toLocomotion.duration = 0.15f;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[REPLACED] AnimatorController 생성 완료: {AnimatorPath}" +
                $"\n  Locomotion (Idle/Walk/Run 블렌드) + 전투 상태 {stateCount}개, 클립 {clipFoundCount}개 매핑됨");
        }

        /// <summary>Strike 트리거 + ComboIndex로 4종 분기 (1~4타)</summary>
        private static void SetupStrikeComboTransitions(
            AnimatorStateMachine sm, AnimatorController controller)
        {
            string[] strikeStates = { "Strike_LightAtk1", "Strike_LightAtk2",
                                      "Strike_LightAtk3", "Strike_LightAtk4" };

            for (int i = 0; i < strikeStates.Length; i++)
            {
                AnimatorState target = FindState(sm, strikeStates[i]);
                if (target == null) continue;

                var transition = sm.AddAnyStateTransition(target);
                transition.AddCondition(AnimatorConditionMode.If, 0, "Strike");
                transition.AddCondition(AnimatorConditionMode.Equals, i, "ComboIndex");
                transition.hasExitTime = false;
                transition.duration = 0.05f;
                transition.canTransitionToSelf = false;
            }
        }

        /// <summary>
        /// Locomotion 블렌드 트리 생성: Speed 파라미터로 Idle(0)/Walk(0.5)/Run(1) 블렌딩.
        /// ExplosiveLLC FBX를 사용한다.
        /// </summary>
        private static AnimatorState CreateLocomotionBlendTree(
            AnimatorStateMachine sm, AnimatorController controller)
        {
            // 클립 로드
            AnimationClip idleClip = LoadClipFromFBX(IdleFBX);
            AnimationClip walkClip = LoadClipFromFBX(WalkFBX);
            // Run = Walk 클립 (BlendTree 안에서 speed 조절)

            if (idleClip == null)
                Debug.LogError("[AnimBuilder] ❌ Idle 클립 미발견: " + IdleFBX +
                    "\n  → 이 파일이 프로젝트에 존재하는지 확인하세요.");
            else
                Debug.Log($"[AnimBuilder] ✓ Idle 클립 로드 성공: {idleClip.name} ({idleClip.length:F2}초)");

            if (walkClip == null)
                Debug.LogError("[AnimBuilder] ❌ Walk 클립 미발견: " + WalkFBX +
                    "\n  → 이 파일이 프로젝트에 존재하는지 확인하세요.");
            else
                Debug.Log($"[AnimBuilder] ✓ Walk 클립 로드 성공: {walkClip.name} ({walkClip.length:F2}초)");

            // 블렌드 트리 생성
            BlendTree blendTree;
            var locomotionState = controller.CreateBlendTreeInController(
                "Locomotion", out blendTree, 0);

            blendTree.blendParameter = "Speed";
            blendTree.blendType = BlendTreeType.Simple1D;

            // Idle: Speed = 0
            if (idleClip != null)
                blendTree.AddChild(idleClip, 0f);

            // Walk: Speed = 0.5
            if (walkClip != null)
                blendTree.AddChild(walkClip, 0.5f);

            // Run: Speed = 1.0 (Walk 클립을 속도 빠르게)
            if (walkClip != null)
                blendTree.AddChild(walkClip, 1f);

            // Locomotion 위치
            var states = sm.states;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].state == locomotionState)
                {
                    var cs = states[i];
                    cs.position = new Vector3(0, 0, 0);
                    states[i] = cs;
                    break;
                }
            }
            sm.states = states;

            Debug.Log("[AnimBuilder] Locomotion 블렌드 트리 생성 (Idle/Walk/Run)");
            return locomotionState;
        }

        /// <summary>절대 경로의 FBX에서 AnimationClip을 로드한다.</summary>
        private static AnimationClip LoadClipFromFBX(string fbxPath)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            if (assets == null) return null;

            foreach (Object asset in assets)
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
            }
            return null;
        }

        /// <summary>FBX 이름으로 AnimationClip 검색</summary>
        private static AnimationClip FindClipByFBXName(string fbxName)
        {
            // FBX 폴더에서 검색 (EEJANAI + Martial Art)
            string[] searchFolders = { FBXFolder, AnimFolder, EEJANAIRoot, MartialArtRoot };
            string[] guids = AssetDatabase.FindAssets(fbxName, searchFolders);

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (Object asset in assets)
                {
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                        return clip;
                }
            }

            // 대안: 전체 에셋에서 t:AnimationClip으로 검색
            guids = AssetDatabase.FindAssets($"t:AnimationClip {fbxName}");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null && !clip.name.StartsWith("__preview__"))
                    return clip;
            }

            return null;
        }

        private static AnimatorState FindState(AnimatorStateMachine sm, string name)
        {
            foreach (var child in sm.states)
            {
                if (child.state.name == name)
                    return child.state;
            }
            return null;
        }

        /// <summary>상태를 격자 형태로 배치</summary>
        private static Vector3 GetStatePosition(int index)
        {
            int col = index % 4;
            int row = index / 4;
            return new Vector3(250 * col, 80 * row, 0);
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
                string folder = System.IO.Path.GetFileName(path);
                AssetDatabase.CreateFolder(parent, folder);
            }
        }
    }
}
