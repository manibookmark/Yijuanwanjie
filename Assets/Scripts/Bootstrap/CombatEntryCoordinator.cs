using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.UI.Battle;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 战斗入场协调器。背景、敌人与 HUD 预先就绪，揭幕后按顺序播放遭遇表现、抽牌并解锁输入。
    /// </summary>
    public sealed class CombatEntryCoordinator : MonoBehaviour
    {
        private CombatSceneController _sceneController;
        private BattleEntryPresentation _entryPresentation;
        private CanvasGroup _hudCanvasGroup;
        private bool _started;
        private int _entryExperienceGrant;
        private System.Action<System.Action> _encounterPresentation;

        /// <summary>
        /// 绑定依赖。
        /// </summary>
        public void Bind(
            CombatSceneController sceneController,
            CanvasGroup hudCanvasGroup,
            float hudFadeDuration = 0.20f,
            int entryExperienceGrant = 0,
            System.Action<System.Action> encounterPresentation = null)
        {
            _sceneController = sceneController;
            _hudCanvasGroup = hudCanvasGroup;
            _entryExperienceGrant = Mathf.Max(0, entryExperienceGrant);
            _encounterPresentation = encounterPresentation;

            if (_hudCanvasGroup != null)
            {
                _entryPresentation = gameObject.AddComponent<BattleEntryPresentation>();
                _entryPresentation.Initialize(_hudCanvasGroup, hudFadeDuration);
                _entryPresentation.PrepareVisibleNonInteractive();
            }
            _sceneController?.SetCombatInputEnabled(false);
        }

        /// <summary>
        /// 开始战斗入场流程。有 requestId 时通过 SceneTransitionRuntime 揭幕；
        /// 无 requestId 时（独立打开场景）等待一帧后直接开始。
        /// </summary>
        public void BeginEntry(string transitionRequestId = null)
        {
            if (_started) return;
            _started = true;
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log(
                    $"[CombatHoverDiag] Entry begin request=" +
                    $"{transitionRequestId ?? "none"}, input=" +
                    $"{_sceneController?.IsCombatInputEnabled}, hud=" +
                    $"{DescribeHudGroup()}");

            // HUD 在揭幕前已经准备并保持可见，只锁住输入。
            _entryPresentation?.PrepareVisibleNonInteractive();
            _sceneController?.SetCombatInputEnabled(false);

            if (!string.IsNullOrEmpty(transitionRequestId) &&
                SceneTransitionRuntime.IsAvailable)
            {
                // Mark destination ready — reveal callback continues the flow
                bool marked = SceneTransitionRuntime.TryMarkDestinationReady(
                    transitionRequestId,
                    BeginEncounterPresentation,
                    out string error);
                if (CombatLogger.EnableVerboseDiagnostics)
                    Debug.Log(
                        $"[CombatHoverDiag] Entry destination ready=" +
                        $"{marked}, error={error ?? "none"}.");
            }
            else
            {
                BeginEncounterPresentation();
            }
        }

        private void BeginEncounterPresentation()
        {
            if (_encounterPresentation == null)
            {
                BeginInitialDraw();
                return;
            }

            bool continued = false;
            // The injected stage owns only presentation and must invoke this
            // continuation exactly once before the initial draw may begin.
            _encounterPresentation(() =>
            {
                if (continued) return;
                continued = true;
                BeginInitialDraw();
            });
        }

        private void BeginInitialDraw()
        {
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log(
                    $"[CombatHoverDiag] Initial draw begin; input=" +
                    $"{_sceneController?.IsCombatInputEnabled}, hud=" +
                    $"{DescribeHudGroup()}");
            // Begin first turn (triggers initial draw)
            _sceneController?.BeginFirstTurn(() =>
            {
                _sceneController?.GrantEntryExperience(
                    _entryExperienceGrant);
                // Initial draw complete — enable input
                bool canAcceptCombatInput =
                    _sceneController != null &&
                    !_sceneController.IsAwaitingRewardSelection;
                _sceneController?.SetCombatInputEnabled(
                    canAcceptCombatInput);
                // CanvasGroup 必须恢复交互，升级奖励面板也挂在同一 Canvas 下；
                // 战斗按钮由 CombatSceneController 单独保持禁用。
                _entryPresentation?.SetInteractable(true);
                if (CombatLogger.EnableVerboseDiagnostics)
                    Debug.Log(
                        $"[CombatHoverDiag] Initial draw complete; input=" +
                        $"{_sceneController?.IsCombatInputEnabled}, " +
                        $"awaitingReward=" +
                        $"{_sceneController?.IsAwaitingRewardSelection}, hud=" +
                        $"{DescribeHudGroup()}");
            });
        }

        private string DescribeHudGroup()
        {
            if (_hudCanvasGroup == null)
                return "null";
            return $"alpha={_hudCanvasGroup.alpha:F2}," +
                   $"interactable={_hudCanvasGroup.interactable}," +
                   $"raycasts={_hudCanvasGroup.blocksRaycasts}";
        }
    }
}
