using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns one floor's additive Combat scene and its reusable presentation shell.
    /// Every selected encounter still receives a new Core and binding lifetime.
    /// This owner serializes load, build, return cleanup and final scene disposal;
    /// map rewards cannot invalidate the shell because it contains no Run data.
    /// The initial pilot is selected by a stable floor ID, independently of routes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FloorCombatSceneOwner : MonoBehaviour
    {
        public const string PilotFloorContentId = "level.xiyou.gaolaozhuang.v1";
        private const float ShellReadyTimeoutSeconds = 30f;
        private static FloorCombatSceneOwner _instance;
        private readonly FloorCombatSceneState _state = new FloorCombatSceneState();
        private DungeonRunSession _run;
        private LevelRunSession _floor;
        private CombatSceneBootstrap _shell;
        private Scene _combatScene;
        private CombatScenePreloadPlan _plan;
        private Coroutine _loadRoutine;
        private Coroutine _encounterRoutine;
        private Coroutine _returnRoutine;
        private Coroutine _releaseRoutine;
        private int _loadGeneration;
        private bool _acceptClaims;

        public static FloorCombatSceneOwner Current => _instance;
        public LevelRunSession Floor => _floor;
        public FloorCombatScenePhase State => _state.Phase;
        public bool IsBusy => _state.IsBusy;
        public string Error => _state.Error;

        public static bool IsPilot(LevelRunSession floor) => floor?.Profile != null &&
            string.Equals(floor.Profile.contentId, PilotFloorContentId, StringComparison.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        public static FloorCombatSceneOwner Ensure()
        {
            if (_instance != null) return _instance;
            var root = new GameObject("[FloorCombatSceneOwner]");
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<FloorCombatSceneOwner>();
            return _instance;
        }

        public IEnumerator PrepareFloor(DungeonRunSession run)
        {
            if (run == null || !IsPilot(run.CurrentFloor)) yield break;
            LevelRunSession floor = run.CurrentFloor;
            while (_releaseRoutine != null) yield return null;
            if (Owns(run, floor))
            {
                while (_loadRoutine != null) yield return null;
                yield break;
            }
            if (_run != null)
            {
                ReleaseFloorScope(_run, _floor);
                while (_releaseRoutine != null) yield return null;
            }
            // The caller may have left this floor while waiting for old disposal.
            if (!ReferenceEquals(run, DungeonRunSessionStore.Current) ||
                !ReferenceEquals(floor, run.CurrentFloor)) yield break;
            _run = run;
            _floor = floor;
            _plan = default;
            _loadGeneration = _state.Begin(run.RunId, floor.Profile.contentId);
            _acceptClaims = true;
            _loadRoutine = StartCoroutine(LoadShell(_loadGeneration));
            while (_loadRoutine != null) yield return null;
        }

        /// <summary>
        /// Claims even a late, cancelled load so its Bootstrap cannot enter the
        /// ordinary BuildNow path. IsShellBuildRequested guards the later Start.
        /// </summary>
        public bool TryClaim(CombatSceneBootstrap bootstrap)
        {
            if (!_acceptClaims || bootstrap == null ||
                bootstrap.gameObject.scene.name != SceneNames.Combat ||
                (_shell != null && _shell != bootstrap)) return false;
            _shell = bootstrap;
            _combatScene = bootstrap.gameObject.scene;
            return true;
        }

        public bool IsShellBuildRequested(CombatSceneBootstrap bootstrap) =>
            bootstrap != null && bootstrap == _shell &&
            _state.IsCurrent(_loadGeneration) &&
            _state.Phase == FloorCombatScenePhase.LoadingShell &&
            OwnsCurrentRun();

        public void MarkShellReady(CombatSceneBootstrap bootstrap)
        {
            if (!IsShellBuildRequested(bootstrap)) return;
            _state.MarkShellReady(_loadGeneration);
        }

        public void MarkFailed(CombatSceneBootstrap bootstrap, string error)
        {
            if (bootstrap != _shell) return;
            Fail(_state.Generation, error);
        }

        /// <summary>
        /// Accepts only intent before TryBeginEncounter. Dynamic encounter build
        /// waits for the load backend handoff after the transition has covered;
        /// the map can commit its exact launch and rollback checkpoint first.
        /// </summary>
        public bool PrepareEncounter(LevelRunSession floor, string nodeContentId)
        {
            if (!ReferenceEquals(floor, _floor) || !OwnsCurrentRun() ||
                !CombatScenePreloadPlan.TryCreate(floor, nodeContentId, out var plan)) return false;
            if ((_state.Phase == FloorCombatScenePhase.PreparingEncounter ||
                 _state.Phase == FloorCombatScenePhase.EncounterReady) &&
                _plan.Matches(plan)) return true;
            if (_shell == null || !_state.BeginEncounter(_state.Generation,
                    plan.NodeContentId, plan.ScenarioId)) return false;
            _plan = plan;
            return true;
        }

        public bool TryCreateLoadHandle(out ISceneLoadHandle handle)
        {
            handle = null;
            if (!OwnsCurrentRun() || _state.Phase == FloorCombatScenePhase.Empty ||
                _state.Phase == FloorCombatScenePhase.Releasing) return false;
            // The controller starts asynchronous loading alongside Cover. The
            // build routine separately waits for a fully rendered covered frame.
            if (_state.Phase == FloorCombatScenePhase.PreparingEncounter && _encounterRoutine == null)
                _encounterRoutine = StartCoroutine(BuildEncounterAfterLaunch(_state.Generation, _plan));
            handle = new ResidentCombatLoadHandle(this, _state.Generation, _plan);
            return true;
        }

        /// <summary>Recovers a rejected launch or transition without rebuilding the floor shell.</summary>
        public void DiscardPreparedEncounter(LevelRunSession floor)
        {
            // Rollback may already have replaced the Run/Floor references. The
            // captured source floor can still discard its own dynamic lifetime.
            if (!ReferenceEquals(floor, _floor) ||
                !_state.DiscardEncounter(_state.Generation)) return;
            _returnRoutine = StartCoroutine(DiscardEncounterThenReady(_state.Generation));
        }

        /// <summary>
        /// Called before map roots reactivate. The resident root stays active so
        /// retained native presentation hosts are not disabled and reconstructed.
        /// Only dynamic encounter state is released; the shell and scene remain.
        /// A frame boundary lets deferred destruction complete before reuse.
        /// </summary>
        public bool TryRetainForReturn(Scene scene)
        {
            if (scene != _combatScene || !_combatScene.IsValid() || !_combatScene.isLoaded ||
                _shell == null || !OwnsCurrentRun() || !_state.BeginReturn(_state.Generation))
                return false;
            int generation = _state.Generation;
            try
            {
                _shell.ReleaseResidentEncounter();
                _shell.SetResidentPresentationVisible(false);
                _returnRoutine = StartCoroutine(FinishReturn(generation));
                return true;
            }
            catch (Exception exception)
            {
                Fail(generation, exception.Message);
                Debug.LogException(exception);
                return false;
            }
        }

        public void ReleaseFloorScope(DungeonRunSession run, LevelRunSession floor)
        {
            if (!ReferenceEquals(run, _run) || !ReferenceEquals(floor, _floor) ||
                _run == null || _releaseRoutine != null) return;
            int releaseGeneration = _state.Release();
            _releaseRoutine = StartCoroutine(DisposeScope(releaseGeneration));
        }

        private bool Owns(DungeonRunSession run, LevelRunSession floor) =>
            ReferenceEquals(run, _run) && ReferenceEquals(floor, _floor) &&
            _state.MatchesScope(run.RunId, floor.Profile.contentId);

        private bool OwnsCurrentRun() => _run != null && _floor != null &&
            ReferenceEquals(_run, DungeonRunSessionStore.Current) &&
            ReferenceEquals(_floor, _run.CurrentFloor) &&
            _state.MatchesScope(_run.RunId, _floor.Profile.contentId);

        private bool MatchesActiveLaunch(int generation, CombatScenePreloadPlan plan) =>
            _state.IsCurrent(generation) && OwnsCurrentRun() && _floor.ActiveEncounter.HasValue &&
            string.Equals(plan.RunId, _run.RunId, StringComparison.Ordinal) &&
            string.Equals(plan.FloorContentId, _floor.Profile.contentId, StringComparison.Ordinal) &&
            plan.Matches(_floor.ActiveEncounter.Value.NodeContentId,
                _floor.ActiveEncounter.Value.ScenarioId);

        private bool IsPreparingFor(int generation, CombatScenePreloadPlan plan) =>
            _state.IsCurrent(generation) &&
            _state.Phase == FloorCombatScenePhase.PreparingEncounter &&
            _state.MatchesEncounter(plan.NodeContentId, plan.ScenarioId);

        private IEnumerator LoadShell(int generation)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // Always leave StartCoroutine before a possible synchronous failure;
            // the caller must never retain the handle of an already-ended load.
            yield return null;
            try
            {
                Scene existing = SceneManager.GetSceneByName(SceneNames.Combat);
                if (existing.IsValid() && existing.isLoaded)
                {
                    Fail(generation, "已有未释放的 Combat 场景，无法准备楼层战斗框架。");
                    yield break;
                }
                AsyncOperation operation = null;
                try
                {
                    operation = SceneManager.LoadSceneAsync(SceneNames.Combat, LoadSceneMode.Additive);
                }
                catch (Exception exception)
                {
                    Fail(generation, exception.Message);
                    Debug.LogException(exception);
                }
                if (operation == null)
                {
                    if (string.IsNullOrEmpty(Error)) Fail(generation, "无法开始准备楼层战斗场景。");
                    yield break;
                }
                while (!operation.isDone) yield return null;
                if (!_combatScene.IsValid())
                    _combatScene = SceneManager.GetSceneByName(SceneNames.Combat);
                // A cancelled load is still owned until its asynchronous disposal.
                if (!_state.IsCurrent(generation)) yield break;
                if (!OwnsCurrentRun())
                {
                    ReleaseFloorScope(_run, _floor);
                    yield break;
                }
                float deadline = Time.realtimeSinceStartup + ShellReadyTimeoutSeconds;
                while (_state.Phase == FloorCombatScenePhase.LoadingShell &&
                    _state.IsCurrent(generation) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (_state.IsCurrent(generation) &&
                    _state.Phase == FloorCombatScenePhase.LoadingShell)
                    Fail(generation, "楼层战斗框架准备超时。");
                if (_state.Phase == FloorCombatScenePhase.ShellReady)
                    Debug.Log("[FloorCombatResidency] floor=" + _floor.Profile.contentId +
                        " shell-ready=" + clock.Elapsed.TotalMilliseconds.ToString("F1",
                            System.Globalization.CultureInfo.InvariantCulture) + "ms");
            }
            finally
            {
                _acceptClaims = false;
                _loadRoutine = null;
            }
        }

        private IEnumerator BuildEncounterAfterLaunch(int generation, CombatScenePreloadPlan plan)
        {
            yield return null;
            try
            {
                // BeginLoad can precede the end of the paper animation. Do not
                // construct Core until the persistent cover is fully shown.
                // Both discard and floor disposal revoke this wait immediately.
                while (IsPreparingFor(generation, plan) &&
                    MatchesActiveLaunch(generation, plan) &&
                    !SceneTransitionRuntimeBootstrap.IsCoverFullyShown)
                    yield return null;
                if (!IsPreparingFor(generation, plan)) yield break;
                if (!MatchesActiveLaunch(generation, plan))
                {
                    Fail(generation, "楼层战斗准备时活动遭遇已改变。");
                    yield break;
                }
                // IsCovered is an animation state; leave a complete render
                // opportunity before even the first assembly stage can take time.
                yield return null;
                if (!IsPreparingFor(generation, plan)) yield break;
                if (!MatchesActiveLaunch(generation, plan) ||
                    !SceneTransitionRuntimeBootstrap.IsCoverFullyShown)
                {
                    Fail(generation, "楼层战斗装配前入场条件或遮幕已改变。");
                    yield break;
                }
                var clock = System.Diagnostics.Stopwatch.StartNew();
                IEnumerator build = null;
                Exception failure = null;
                try
                {
                    build = _shell.BuildResidentEncounterStaged(plan,
                        () => IsPreparingFor(generation, plan) &&
                            MatchesActiveLaunch(generation, plan) &&
                            SceneTransitionRuntimeBootstrap.IsCoverFullyShown);
                }
                catch (Exception exception) { failure = exception; }
                if (build != null)
                {
                    try
                    {
                        while (true)
                        {
                            bool advanced = false;
                            try { advanced = build.MoveNext(); }
                            catch (Exception exception) { failure = exception; }
                            if (failure != null || !advanced) break;
                            yield return build.Current;
                        }
                    }
                    finally { (build as IDisposable)?.Dispose(); }
                }
                if (failure != null)
                {
                    Fail(generation, failure.Message);
                    Debug.LogException(failure);
                    yield break;
                }
                if (!IsPreparingFor(generation, plan)) yield break;
                if (!MatchesActiveLaunch(generation, plan) ||
                    !SceneTransitionRuntimeBootstrap.IsCoverFullyShown ||
                    _shell == null || !_shell.IsBuilt)
                {
                    Fail(generation, "楼层战斗分帧装配时入场条件已改变。");
                    yield break;
                }
                _state.MarkEncounterReady(generation);
                Debug.Log("[FloorCombatResidency] scenario=" + plan.ScenarioId +
                    " encounter-build=" + clock.Elapsed.TotalMilliseconds.ToString("F1",
                        System.Globalization.CultureInfo.InvariantCulture) + "ms staged=true");
            }
            finally { _encounterRoutine = null; }
        }

        private IEnumerator FinishReturn(int generation)
        {
            yield return null;
            _returnRoutine = null;
            _state.CompleteReturn(generation);
            _plan = default;
        }

        private IEnumerator DiscardEncounterThenReady(int generation)
        {
            yield return null;
            while (_encounterRoutine != null) yield return null;
            if (_state.IsCurrent(generation) &&
                _state.Phase == FloorCombatScenePhase.CleaningEncounter)
            {
                try
                {
                    _shell?.ReleaseResidentEncounter();
                    _shell?.SetResidentPresentationVisible(false);
                }
                catch (Exception exception)
                {
                    Fail(generation, exception.Message);
                    Debug.LogException(exception);
                }
                yield return null;
                _state.CompleteReturn(generation);
                _plan = default;
            }
            _returnRoutine = null;
        }

        private bool TryActivateEncounter(int generation, CombatScenePreloadPlan plan, out string error)
        {
            error = null;
            if (!MatchesActiveLaunch(generation, plan) ||
                _state.Phase != FloorCombatScenePhase.EncounterReady || _shell == null ||
                !_combatScene.IsValid() || !_combatScene.isLoaded)
            {
                error = Error ?? "楼层战斗尚未准备好，或活动遭遇已经改变。";
                return false;
            }
            Scene source = SceneManager.GetActiveScene();
            if (!SceneManager.SetActiveScene(_combatScene))
            {
                error = "无法激活楼层驻留战斗场景。";
                return false;
            }
            GameObject[] sourceRoots = source.IsValid() && source != _combatScene
                ? source.GetRootGameObjects() : Array.Empty<GameObject>();
            foreach (GameObject root in sourceRoots) root.SetActive(false);
            string requestId = SceneContext.Get<string>(SceneContextKeys.TransitionRequestId, null);
            bool activated = false;
            try { activated = _shell.ActivatePrepared(requestId, out error); }
            catch (Exception exception) { error = exception.Message; }
            if (!activated)
            {
                _shell.SetResidentPresentationVisible(false);
                foreach (GameObject root in sourceRoots) root.SetActive(true);
                if (source.IsValid() && source.isLoaded) SceneManager.SetActiveScene(source);
                return false;
            }
            return _state.Activate(generation, plan.NodeContentId, plan.ScenarioId);
        }

        private IEnumerator DisposeScope(int releaseGeneration)
        {
            // Leave the source scene's destruction stack before unloading siblings.
            yield return null;
            while (_loadRoutine != null || _encounterRoutine != null) yield return null;
            if (_returnRoutine != null)
            {
                StopCoroutine(_returnRoutine);
                _returnRoutine = null;
            }
            if (_combatScene.IsValid() && _combatScene.isLoaded)
            {
                foreach (GameObject root in _combatScene.GetRootGameObjects()) root.SetActive(false);
                AsyncOperation unload = SceneManager.UnloadSceneAsync(_combatScene);
                if (unload != null) while (!unload.isDone) yield return null;
            }
            _shell = null;
            _combatScene = default;
            _plan = default;
            _run = null;
            _floor = null;
            _acceptClaims = false;
            _state.CompleteRelease(releaseGeneration);
            _releaseRoutine = null;
        }

        private void Fail(int generation, string error)
        {
            if (_state.Fail(generation, error))
                Debug.LogError("[FloorCombatResidency] " + Error);
        }

        private void OnDestroy()
        {
            _state.Release();
            if (_instance == this) _instance = null;
            // Normal navigation calls ReleaseFloorScope; application shutdown owns
            // the final scene disposal and must not start fresh native work here.
        }

        private sealed class ResidentCombatLoadHandle : ISceneLoadHandle
        {
            private readonly FloorCombatSceneOwner _owner;
            private readonly int _generation;
            private readonly CombatScenePreloadPlan _plan;
            private bool _activated;
            private bool _done;
            private string _activationError;
            public float Progress => IsReadyForActivation ? 0.9f : 0.5f;
            public bool IsReadyForActivation => Error == null && !_activated &&
                _owner.State == FloorCombatScenePhase.EncounterReady;
            public bool IsDone => _done;
            public string Error => _activationError ?? (_owner == null
                ? "楼层战斗框架已释放。"
                : !_owner.MatchesActiveLaunch(_generation, _plan)
                    ? "楼层战斗对应的活动遭遇已改变。" : _owner.Error);

            public ResidentCombatLoadHandle(FloorCombatSceneOwner owner, int generation,
                CombatScenePreloadPlan plan)
            { _owner = owner; _generation = generation; _plan = plan; }

            public void Activate()
            {
                if (_activated || Error != null) return;
                _activated = true;
                if (!_owner.TryActivateEncounter(_generation, _plan, out _activationError))
                    _activationError ??= "楼层战斗激活失败。";
                _done = true;
            }
        }
    }
}
