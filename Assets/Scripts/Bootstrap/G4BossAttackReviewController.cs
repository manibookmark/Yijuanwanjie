using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.UI.Battle;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Editor-only manual review driver. Owns its transient boss-phase scenario
    /// and optional J-preview input lease; actual turns use the production
    /// combat/presentation pipeline. No production scenario or AI is modified.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    [RequireComponent(typeof(CombatSceneBootstrap))]
    public sealed class G4BossAttackReviewController : MonoBehaviour
    {
        public const string ScenePath = "Assets/Scenes/G4BossAttackReview.unity";
        public BattleScenarioProfile sourceScenario;
        public LevelRunProfile reviewFloor;

#if UNITY_EDITOR
        private static bool _fullEncounter;
        private CombatSceneBootstrap _bootstrap;
        private BattleScenarioProfile _scenarioClone;
        private BossAttackCloseupPresenter _previewCloseup;
        private IDisposable _inputLease;
        private int _previewVersion;
        private float _previewDeadline;
        private bool _switching;
        private bool _configured;
        private string _status = "正在准备战斗";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetReviewMode() => _fullEncounter = false;

        private void Awake()
        {
            if (gameObject.scene.path != ScenePath)
            {
                enabled = false;
                return;
            }
            _bootstrap = GetComponent<CombatSceneBootstrap>();
            if (_bootstrap == null || sourceScenario == null || reviewFloor == null ||
                string.IsNullOrWhiteSpace(sourceScenario.levelBossContentId))
                throw new InvalidOperationException("G4 Boss review requires its production scenario and F5 floor.");

            // Reuse the typed F5 account/slot boundary. The root session was
            // checked before entering this independent manual review scene.
            EditorLevelMapQuickPlayRuntime.ApplyPresetAccountForLevel(reviewFloor);
            LevelRunSessionStore.Clear();
            DungeonRunSessionStore.Clear();
            SceneContext.Clear();
            ConfigureScenario();
            _configured = true;
        }

        private void ConfigureScenario()
        {
            if (_fullEncounter)
            {
                _bootstrap.scenario = sourceScenario;
                return;
            }
            _scenarioClone = Instantiate(sourceScenario);
            _scenarioClone.name = "G4 Boss phase manual review";
            _scenarioClone.scenarioId = "battle.review.xiyou.liushahe.f05.boss_phase.v1";
            _scenarioClone.enemyContentIds = new List<string> { sourceScenario.levelBossContentId };
            _scenarioClone.enemyWaveSizes = new List<int>();
            _scenarioClone.enemyFormationRows = new List<int>();
            _scenarioClone.enemyFormationSourceSlots = new List<EnemyFormationSourceSlot>();
            _scenarioClone.enemyFormationSourceColumnCount = 0;
            _scenarioClone.enemyFormationPattern = string.Empty;
            List<string> errors = _scenarioClone.ValidateProfile();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));
            _bootstrap.scenario = _scenarioClone;
        }

        private void Update()
        {
            if (!_configured || _switching) return;
            if (_inputLease != null && (_previewCloseup == null ||
                !_previewCloseup.isActiveAndEnabled || Time.unscaledTime >= _previewDeadline))
            {
                CancelPreview(restoreIdle: true);
                _status = "演示已中断，可重新按 J";
            }
            if (Time.timeScale <= 0f) return;
            if (Input.GetKeyDown(KeyCode.Alpha1)) Reload(fullEncounter: false);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) Reload(fullEncounter: true);
            else if (Input.GetKeyDown(KeyCode.R)) Reload(_fullEncounter);
            else if (Input.GetKeyDown(KeyCode.J)) PreviewAttack();
        }

        private void Reload(bool fullEncounter)
        {
            if (_switching) return;
            _switching = true;
            CancelPreview(restoreIdle: false);
            _fullEncounter = fullEncounter;
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                ScenePath, new UnityEngine.SceneManagement.LoadSceneParameters(
                    UnityEngine.SceneManagement.LoadSceneMode.Single));
        }

        private bool CanPreview()
        {
            return _configured && !_switching && _inputLease == null &&
                   _bootstrap != null && _bootstrap.IsBuilt && _bootstrap.Combat != null &&
                   _bootstrap.SceneController != null && _bootstrap.SceneController.IsCombatInputEnabled &&
                   !_bootstrap.SceneController.IsCardPresentationActive &&
                   !_bootstrap.SceneController.IsAwaitingRewardSelection &&
                   _bootstrap.Combat.State.CanPlayCards &&
                   _bootstrap.EnemyTurnPresentationCoordinator?.IsPresenting != true &&
                   _bootstrap.Battlefield?.EnemyArea != null &&
                   !_bootstrap.Battlefield.EnemyArea.IsEnemyActionPresentationActive;
        }

        private BossAttackCloseupPresenter FindReadyCloseup(out int enemyIndex)
        {
            enemyIndex = -1;
            if (_bootstrap == null || _bootstrap.Combat == null || sourceScenario == null) return null;
            for (int i = 0; i < _bootstrap.Combat.Enemies.Count; i++)
            {
                EnemyStats enemy = _bootstrap.Combat.Enemies[i];
                if (enemy == null || enemy.IsDead || !enemy.IsDeployed ||
                    !string.Equals(enemy.contentId, sourceScenario.levelBossContentId, StringComparison.Ordinal)) continue;
                foreach (BossAttackCloseupPresenter closeup in FindObjectsOfType<BossAttackCloseupPresenter>())
                    if (closeup.gameObject.scene == gameObject.scene && closeup.IsReady && !closeup.IsPlaying &&
                        string.Equals(closeup.ContentId, enemy.contentId, StringComparison.Ordinal))
                    {
                        enemyIndex = i;
                        return closeup;
                    }
            }
            return null;
        }

        private void PreviewAttack()
        {
            if (!CanPreview())
            {
                _status = "请等待入场、抽牌或当前演出结束";
                return;
            }
            BossAttackCloseupPresenter closeup = FindReadyCloseup(out int index);
            if (closeup == null)
            {
                _status = "等待动作资源；完整遭遇中需先击败杂兵让 Boss 登场";
                return;
            }
            BossAttackCloseupProfile profile = Resources.Load<BossAttackCloseupProfile>(
                BossAttackCloseupProfile.DefaultResourcePath);
            if (profile == null || !closeup.CanPlay(profile.actionId))
            {
                _status = "等待动作资源";
                return;
            }
            _previewCloseup = closeup;
            _inputLease = _bootstrap.SceneController.AcquireCombatInputLock();
            _previewDeadline = Time.unscaledTime + Mathf.Max(10f, profile.Duration + 5f);
            int version = ++_previewVersion;
            _status = "攻击演示中（仅表现，不结算伤害）";
            try
            {
                // Empty impact deliberately performs no rule command. Manual
                // end-turn remains the real AI/14-damage combat path.
                _bootstrap.Battlefield.EnemyArea.PlayAttackMotion(index, profile.actionId,
                    () => { }, () => FinishPreview(version), () => FinishPreview(version));
            }
            catch
            {
                CancelPreview(restoreIdle: true);
                throw;
            }
        }

        private void FinishPreview(int version)
        {
            if (version != _previewVersion) return;
            ++_previewVersion;
            _previewCloseup = null;
            IDisposable lease = _inputLease;
            _inputLease = null;
            lease?.Dispose();
            if (!_switching) _status = "演示结束；J 可重播，结束回合可看真实攻击";
        }

        private void CancelPreview(bool restoreIdle)
        {
            ++_previewVersion;
            BossAttackCloseupPresenter closeup = _previewCloseup;
            _previewCloseup = null;
            IDisposable lease = _inputLease;
            _inputLease = null;
            try
            {
                if (closeup != null)
                {
                    closeup.Cancel();
                    BossSequenceAnimator animator = closeup.GetComponent<BossSequenceAnimator>();
                    if (restoreIdle && !_switching && animator != null && animator.isActiveAndEnabled)
                        animator.EnterIdle();
                }
            }
            finally { lease?.Dispose(); }
        }

        private void ReleaseScenario()
        {
            BattleScenarioProfile clone = _scenarioClone;
            _scenarioClone = null;
            if (clone == null) return;
            if (_bootstrap != null && _bootstrap.scenario == clone)
                _bootstrap.scenario = sourceScenario;
            Destroy(clone);
        }

        private void OnDisable()
        {
            CancelPreview(restoreIdle: !_switching);
            // The live Combat bootstrap still borrows this immutable scenario.
            // Disabling manual controls must not replace a running encounter's
            // configuration. Release the borrowed clone at final scene teardown.
        }

        private void OnDestroy()
        {
            CancelPreview(restoreIdle: false);
            ReleaseScenario();
        }

        private void OnGUI()
        {
            if (!_configured || gameObject.scene.path != ScenePath) return;
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = true
            };
            float width = Mathf.Min(1080f, Screen.width - 24f);
            string mode = _fullEncounter ? "完整遭遇（15杂兵＋Boss）" : "Boss阶段预览";
            GUI.Box(new Rect((Screen.width - width) * 0.5f, 8f, width, 66f),
                "G4 " + mode + "（独立验收）  |  1：Boss阶段  2：完整遭遇  R：重开\n" +
                "J：仅演示攻击、不扣血  |  结束回合：真实战斗  |  " + _status, style);
        }
#else
        private void Awake() => enabled = false;
#endif
    }
}
