using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects.Terrain
{
    /// <summary>
    /// 地形规则事件到世界空间外观的唯一入口。调用方只提交事件和落点，Presenter
    /// 隐藏 profile 查找、运行时对象索引、叠层更新以及延迟销毁。
    /// </summary>
    public sealed class TerrainEffectPresenter : MonoBehaviour
    {
        private sealed class Entry
        {
            public GameObject Root;
            public ITerrainEffectVisual Visual;
        }

        [SerializeField] private Transform visualRoot;
        private readonly Dictionary<int, Entry> _entries =
            new Dictionary<int, Entry>();
        private IEffectOneShotAudio _audio;
        private float _lastTriggerSound = -100f;

        public void SetOneShotAudio(IEffectOneShotAudio audio) => _audio = audio;

        public int ActiveVisualCount => _entries.Count;

        public void SetVisualRoot(Transform root)
        {
            visualRoot = root;
        }

        public void Present(TerrainEffectChange change, Vector3 worldAnchor)
        {
            switch (change.ChangeKind)
            {
                case TerrainEffectChangeKind.Placed:
                    PresentPlacement(change, worldAnchor);
                    break;
                case TerrainEffectChangeKind.Triggered:
                    if (_entries.TryGetValue(change.RuntimeId, out Entry triggered))
                    {
                        // Column fire follows the freshly resolved front enemy.
                        if (change.TargetIndex >= 0) triggered.Root.transform.position = worldAnchor;
                        triggered.Visual.Trigger(change);
                        TerrainVfxProfile profile = TerrainVfxProfileRegistry.Get(change.EffectId);
                        if (profile != null && !string.IsNullOrWhiteSpace(profile.triggerSfxPath) &&
                            Time.unscaledTime - _lastTriggerSound >= 0.075f)
                        {
                            AudioClip clip = Resources.Load<AudioClip>(profile.triggerSfxPath);
                            if (clip != null)
                            {
                                _audio?.Play(clip, EffectOneShotKind.PeriodicAttackHit, 1f, profile.triggerSfxGain);
                                _lastTriggerSound = Time.unscaledTime;
                            }
                        }
                    }
                    break;
                case TerrainEffectChangeKind.Removed:
                    BeginRemove(change);
                    break;
            }
        }

        public bool HasVisual(int runtimeId)
        {
            return _entries.ContainsKey(runtimeId);
        }

        private void PresentPlacement(
            TerrainEffectChange change,
            Vector3 worldAnchor)
        {
            if (!_entries.TryGetValue(change.RuntimeId, out Entry entry))
            {
                TerrainVfxProfile profile =
                    TerrainVfxProfileRegistry.Get(change.EffectId);
                if (profile == null)
                {
                    Debug.LogWarning(
                        $"[TerrainVFX] 缺少 effectId={change.EffectId} 的表现配置。");
                    return;
                }

                var root = new GameObject(
                    $"TerrainEffect_{change.EffectId}_{change.RuntimeId}");
                root.transform.SetParent(
                    visualRoot != null ? visualRoot : transform,
                    false);
                root.transform.position = worldAnchor;
                ITerrainEffectVisual visual = TerrainEffectVisualFactory.Create(
                    profile.visualType,
                    root);
                visual.Initialize(profile, change);
                entry = new Entry { Root = root, Visual = visual };
                _entries.Add(change.RuntimeId, entry);
            }

            if (change.TargetMode == TerrainTargetMode.ColumnDynamicFront && change.TargetIndex >= 0)
                entry.Root.transform.position = worldAnchor;
            entry.Visual.Place(change);
        }

        private void BeginRemove(TerrainEffectChange change)
        {
            if (!_entries.TryGetValue(change.RuntimeId, out Entry entry)) return;
            GameObject expectedRoot = entry.Root;
            entry.Visual.BeginRemove(change, () =>
            {
                if (_entries.TryGetValue(change.RuntimeId, out Entry current) &&
                    current.Root == expectedRoot)
                {
                    _entries.Remove(change.RuntimeId);
                }
            });
        }

        private void OnDestroy()
        {
            _entries.Clear();
        }
    }
}
