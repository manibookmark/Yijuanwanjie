using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Audio;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Effects.Map;
using WanjieScroll.UI.Components;
using Object = UnityEngine.Object;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns one chapter's immutable resource residency and its entrance cover.
    /// No encounters, cards, reward state, particles or video players are created
    /// by warmup. Those remain under their existing gameplay/lifecycle owners.
    /// </summary>
    public sealed class StageResourceWarmupRuntime : MonoBehaviour
    {
        private static StageResourceWarmupRuntime _instance;
        private readonly Dictionary<string, Object> _resident =
            new Dictionary<string, Object>(StringComparer.Ordinal);
        private readonly StageResourcePreparationState _state = new StageResourcePreparationState();
        private Coroutine _preparation;
        private StageLoadingCoverView _cover;
        private IDisposable _inputLease;
        private bool _coverHeld;
        public string Error { get; private set; }

        public static StageResourceWarmupRuntime Ensure()
        {
            if (_instance != null) return _instance;
            var root = new GameObject("[StageResourceWarmup]");
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<StageResourceWarmupRuntime>();
            _instance._cover = root.AddComponent<StageLoadingCoverView>();
            return _instance;
        }

        public IEnumerator EnsureReady(string dungeonContentId)
        {
            SceneAudioListenerContinuity.Ensure().Reconcile();
            while (_preparation != null) yield return null;
            Error = null;
            if (_state.IsReadyFor(dungeonContentId))
                yield break;
            if (!StageResourceManifestCatalog.TryGet(dungeonContentId, out var manifest))
            {
                Error = "关区预加载清单缺失：" + dungeonContentId;
                yield break;
            }
            if (!manifest.TryCreatePlan(out var plan, out string planError))
            {
                Error = "关区预加载清单无效：" + planError;
                yield break;
            }
            _preparation = StartCoroutine(Prepare(plan));
            while (_preparation != null) yield return null;
        }

        private IEnumerator Prepare(StageResourceLoadPlan plan)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            yield return null;
            int generation = _state.Begin(plan);
            bool completed = false;
            try
            {
                _coverHeld = true;
                _cover.Show(plan.DisplayName);
                _inputLease = SceneTransitionRuntime.InteractionBarrier?.Hold("stage-resource-preparation");
                // Display the chapter cover before the first asset import/upload.
                yield return null;
                yield return null;
                var requiredKeys = new HashSet<string>(StringComparer.Ordinal);
                // A small parallel window avoids adding one empty frame per file.
                // Dependencies already loaded by a profile are reused by Resources.
                const int batchSize = 12;
                for (int offset = 0; offset < plan.Resources.Count && Error == null; offset += batchSize)
                {
                    int count = Math.Min(batchSize, plan.Resources.Count - offset);
                    var requests = new ResourceRequest[count];
                    for (int i = 0; i < count; i++)
                    {
                        StageResourceLoadItem item = plan.Resources[offset + i];
                        requiredKeys.Add(item.CacheKey);
                        if (_resident.TryGetValue(item.CacheKey, out Object existing) && existing != null) continue;
                        try { requests[i] = BeginLoad(item); }
                        catch (Exception exception) { Error = item.ResourcePath + "：" + exception.Message; break; }
                    }
                    for (int i = 0; i < count && Error == null; i++)
                    {
                        StageResourceLoadItem item = plan.Resources[offset + i];
                        if (requests[i] != null && !requests[i].isDone) yield return requests[i];
                        Object asset = requests[i] != null ? requests[i].asset : _resident[item.CacheKey];
                        if (asset == null)
                        {
                            Error = "关区预加载资源缺失：" + item.ResourcePath;
                            break;
                        }
                        _resident[item.CacheKey] = asset;
                        if (asset is MapPreviewProfile map) MapPresentationCatalog.Register(map);
                        if (asset is Sprite cover && item.ResourcePath == plan.CoverResourcePath)
                            _cover.SetBackground(cover);
                        if (asset is AudioClip clip && clip.loadType != AudioClipLoadType.Streaming)
                        {
                            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                            while (clip.loadState == AudioDataLoadState.Loading) yield return null;
                            if (clip.loadState == AudioDataLoadState.Failed)
                            {
                                Error = "关区音频预加载失败：" + item.ResourcePath;
                                break;
                            }
                        }
                        _state.TryMarkLoaded(generation, item.CacheKey);
                    }
                    yield return null;
                }
                if (Error == null)
                {
                    // Drop only our old chapter pins. Shared entries stay resident;
                    // no forced UnloadUnusedAssets pause is introduced between floors.
                    var expired = new List<string>();
                    foreach (string key in _resident.Keys)
                        if (!requiredKeys.Contains(key)) expired.Add(key);
                    foreach (string key in expired) _resident.Remove(key);
                    completed = _state.IsReadyFor(plan.DungeonContentId);
                }
            }
            finally
            {
                if (!completed)
                {
                    Error = Error ?? "关区资源预加载被中断。";
                    _state.TryFail(generation, Error);
                    _cover.ShowFailure();
                    Debug.LogError("[StageWarmup] " + Error);
                }
                _preparation = null;
                Debug.Log("[StageWarmupTiming] scope=" + plan.DungeonContentId +
                    " ready=" + completed + " total=" + clock.Elapsed.TotalMilliseconds.ToString("F1",
                        System.Globalization.CultureInfo.InvariantCulture) + "ms");
            }
        }

        // Compatibility entry: Audio owns the fallback. An early call cannot
        // disable it before the map has actually created a scene listener.
        public void BeginMapConstruction()
        {
            SceneAudioListenerContinuity.Ensure().Reconcile();
        }

        public void CompleteMapEntrance()
        {
            if (!_coverHeld) return;
            _coverHeld = false;
            _cover.Hide();
            _inputLease?.Dispose(); _inputLease = null;
        }

        public void FailMapEntrance(string message)
        {
            Error = message ?? "地图准备失败。";
            _cover?.ShowFailure();
            Debug.LogError("[StageWarmup] " + Error);
        }

        public void BeginMapPreparation(string displayName)
        {
            if (_coverHeld) return;
            Error = null;
            _coverHeld = true;
            _cover.Show(displayName);
            _inputLease = SceneTransitionRuntime.InteractionBarrier?.Hold("map-shell-preparation");
        }

        private static ResourceRequest BeginLoad(StageResourceLoadItem item)
        {
            switch (item.Kind)
            {
                case StageResourceKind.Sprite: return Resources.LoadAsync<Sprite>(item.ResourcePath);
                case StageResourceKind.Texture2D: return Resources.LoadAsync<Texture2D>(item.ResourcePath);
                case StageResourceKind.AudioClip: return Resources.LoadAsync<AudioClip>(item.ResourcePath);
                default: return Resources.LoadAsync<Object>(item.ResourcePath);
            }
        }

        private void OnDestroy()
        {
            _inputLease?.Dispose();
            _resident.Clear();
            _state.Clear();
            if (_instance == this) _instance = null;
        }
    }
}
