using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.UI.Battle;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Manual material review only. Owns the transient scenario and action-preview
    /// input lease; production profiles, saves and encounter rules are never edited.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class BossReviewTestController : MonoBehaviour
    {
        public const string ScenePath = "Assets/Scenes/BossReviewTest.unity";
        private static readonly string[] ScenarioPaths =
        {
            "Content/Battle/ShuangchalingF04B01",
            "Content/Battle/YingchoujianF05B01",
            "Content/Battle/GaolaozhuangF05B01"
        };
        private static int _selectedBoss = 2;
        private BattleScenarioProfile _scenario;
        private CombatSceneBootstrap _bootstrap;
        private BossSequenceAnimator _previewAnimator;
        private IDisposable _inputLease;
        private IDisposable _previewEffectPlayback;
        private int _previewVersion;
        private float _previewDeadline;
        private float _inactiveSince = -1f;
        private bool _switching;

        private void Awake()
        {
            if (gameObject.scene.path != ScenePath)
            {
                enabled = false;
                return;
            }
            _bootstrap = GetComponent<CombatSceneBootstrap>();
            var source = Resources.Load<BattleScenarioProfile>(ScenarioPaths[_selectedBoss]);
            if (_bootstrap == null || source == null)
                throw new InvalidOperationException("Boss review scene configuration is missing.");
            _scenario = Instantiate(source);
            _scenario.name = "Boss Review G" + (_selectedBoss + 1);
            _scenario.enemyContentIds = new List<string> { source.levelBossContentId };
            _scenario.enemyWaveSizes.Clear();
            _scenario.enemyFormationRows.Clear();
            _scenario.enemyFormationSourceSlots.Clear();
            _scenario.enemyFormationSourceColumnCount = 0;
            _scenario.enemyFormationPattern = "";
            var errors = _scenario.ValidateProfile();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));
            LevelRunSessionStore.Clear();
            SceneContext.Clear();
            _bootstrap.scenario = _scenario;
        }

        private void Update()
        {
            if (_inputLease != null)
            {
                if (_previewAnimator == null || !_previewAnimator.isActiveAndEnabled ||
                    Time.unscaledTime >= _previewDeadline) CancelPreview(true);
                else if (_previewAnimator.IsActionRunning) _inactiveSince = -1f;
                else if (_inactiveSince < 0f) _inactiveSince = Time.unscaledTime;
                // Normal completion includes a 0.1s return-to-idle blend after
                // IsActionRunning becomes false. Let its callback finish first.
                else if (Time.unscaledTime - _inactiveSince >= 0.25f) CancelPreview(true);
            }
            if (_switching || Time.timeScale <= 0f) return;
            if (!Input.GetKeyDown(KeyCode.Alpha1) && !Input.GetKeyDown(KeyCode.Alpha2) &&
                !Input.GetKeyDown(KeyCode.Alpha3) && !Input.GetKeyDown(KeyCode.R) &&
                !Input.GetKeyDown(KeyCode.J) && !Input.GetKeyDown(KeyCode.K)) return;
            foreach (var pause in FindObjectsOfType<CombatPauseMenu>())
                if (pause.IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchBoss(0);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchBoss(1);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchBoss(2);
            else if (Input.GetKeyDown(KeyCode.R)) SwitchBoss(_selectedBoss);
            else if (Input.GetKeyDown(KeyCode.J)) Preview(BossSequenceAnimator.BossAction.Attack);
            else if (Input.GetKeyDown(KeyCode.K)) Preview(BossSequenceAnimator.BossAction.Defend);
        }

        private void SwitchBoss(int index)
        {
            if (_switching) return;
            _switching = true;
            CancelPreview();
            _selectedBoss = index;
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath);
#endif
        }

        private void Preview(BossSequenceAnimator.BossAction action)
        {
            if (_bootstrap == null || !_bootstrap.IsBuilt || _inputLease != null ||
                _bootstrap.SceneController == null ||
                !_bootstrap.SceneController.IsCombatInputEnabled ||
                _bootstrap.SceneController.IsCardPresentationActive ||
                _bootstrap.SceneController.IsAwaitingRewardSelection ||
                !_bootstrap.Combat.State.CanPlayCards ||
                _bootstrap.EnemyTurnPresentationCoordinator?.IsPresenting == true) return;
            var animator = FindObjectOfType<BossSequenceAnimator>();
            if (animator == null || animator.IsActionRunning) return;
            _previewAnimator = animator;
            _inputLease = _bootstrap.SceneController.AcquireCombatInputLock();
            // The animator uses unscaled time. Bound withdrawn callbacks without
            // changing production pause or animation rules.
            _previewDeadline = Time.unscaledTime + 30f;
            _inactiveSince = -1f;
            int version = ++_previewVersion;
            try
            {
                string enemyId = _scenario.levelBossContentId;
                string actionId = ResolvePreviewActionId(enemyId, action);
                bool impactPresented = false;
                animator.PlayAction(action, actionId, () =>
                {
                    if (version != _previewVersion || impactPresented ||
                        !isActiveAndEnabled || _inputLease == null) return;
                    impactPresented = true;
                    EnemyIntent intent = action == BossSequenceAnimator.BossAction.Attack
                        ? EnemyIntent.Attack(0, actionId: actionId)
                        : EnemyIntent.Defend(0, actionId: actionId);
                    if (_bootstrap.BossActionEffects != null &&
                        _bootstrap.BossActionEffects.TryPlayScoped(
                            enemyId, intent, 0, out IDisposable playback))
                    {
                        if (version != _previewVersion) playback?.Dispose();
                        else _previewEffectPlayback = playback;
                    }
                }, () => FinishPreview(version), () => FinishPreview(version));
            }
            catch
            {
                CancelPreview();
                throw;
            }
        }

        private static string ResolvePreviewActionId(
            string enemyId, BossSequenceAnimator.BossAction action)
        {
            if (action == BossSequenceAnimator.BossAction.Defend)
                return enemyId == "enemy.xiyou.gaolaozhuang_cave_demon_lord"
                    ? "action.gaolaozhuang.cave_demon_lord.seal_guard" : null;
            switch (enemyId)
            {
                case "enemy.xiyou.yinjiangjun":
                    return "attack.yinjiangjun.camera_cross";
                case "enemy.xiyou.yingchoujian_abyss_warden":
                    return "attack.zhenyuan_yushi.abyss_sweep";
                case "enemy.xiyou.gaolaozhuang_cave_demon_lord":
                    return "action.gaolaozhuang.cave_demon_lord.cleave";
                default: return null;
            }
        }

        private void FinishPreview(int version)
        {
            if (version != _previewVersion) return;
            ++_previewVersion;
            _previewAnimator = null;
            IDisposable effect = _previewEffectPlayback;
            IDisposable input = _inputLease;
            _previewEffectPlayback = null;
            _inputLease = null;
            try { effect?.Dispose(); }
            finally { input?.Dispose(); }
        }

        private void CancelPreview(bool restoreIdle = false)
        {
            ++_previewVersion;
            var animator = _previewAnimator;
            _previewAnimator = null;
            IDisposable effect = _previewEffectPlayback;
            IDisposable input = _inputLease;
            _previewEffectPlayback = null;
            _inputLease = null;
            try { effect?.Dispose(); }
            finally { input?.Dispose(); }
            if (animator == null) return;
            animator.StopAll();
            if (restoreIdle && animator.isActiveAndEnabled) animator.EnterIdle();
        }

        private void OnDisable() => CancelPreview(!_switching);

        private void OnDestroy()
        {
            CancelPreview();
            if (_scenario != null) Destroy(_scenario);
            _scenario = null;
        }

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect((Screen.width - 780f) * 0.5f, 8f, 780f, 34f),
                "BOSS REVIEW  G" + (_selectedBoss + 1) +
                "     1 / 2 / 3: Boss     J: Attack     K: Defend     R: Reset     ESC: Menu", style);
        }
    }
}
