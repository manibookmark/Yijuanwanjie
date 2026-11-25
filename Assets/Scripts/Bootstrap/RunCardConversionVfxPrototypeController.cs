using System.Collections;
using UnityEngine;
using WanjieScroll.Effects.Map;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Isolated MapVfxTest adapter for the Guiyuan stele. It deliberately
    /// locates the authored X01 preview anchor by stable IDs and never enters
    /// production run rules or card-conversion transactions.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MapVfxTestBootstrap))]
    public sealed class RunCardConversionVfxPrototypeController : MonoBehaviour
    {
        private const string PrototypeResourcePath =
            "VfxProfiles/Map/GuiyuanStelePrototype";

        [SerializeField] private string targetContentId =
            "map.visual.xiyou.shuangchaling.f04.x01.conversion";
        [SerializeField] private string targetGroupId =
            "node.xiyou.shuangchaling.f04.x01";

        private MapVfxTestBootstrap _bootstrap;
        private MapPresentation _presentation;
        private RunCardConversionMapEffect _effect;
        private Transform _placeholderVisual;
        private MapVfxState _lastObservedState = MapVfxState.Hidden;
        private bool _forceHover;
        private bool _committing;

        public RunCardConversionMapEffect Effect => _effect;
        public MapVfxState PreviewState => _effect != null
            ? _effect.State
            : MapVfxState.Hidden;

        private IEnumerator Start()
        {
            _bootstrap = GetComponent<MapVfxTestBootstrap>();
            while (_bootstrap.Presentation == null ||
                   !_bootstrap.Presentation.IsBuilt)
            {
                yield return null;
            }

            _presentation = _bootstrap.Presentation;
            AttachPrototype();
            _bootstrap.enabled = false;
            PreviewAvailable();
        }

        private void Update()
        {
            if (_effect == null)
                return;

            if (_placeholderVisual != null &&
                _placeholderVisual.gameObject.activeSelf)
            {
                _placeholderVisual.gameObject.SetActive(false);
            }

            if (Input.GetKeyDown(KeyCode.Alpha1)) PreviewLocked();
            if (Input.GetKeyDown(KeyCode.Alpha2)) PreviewAvailable();
            if (Input.GetKeyDown(KeyCode.Alpha3)) PreviewHovered();
            if (Input.GetKeyDown(KeyCode.Alpha4)) PreviewCleared();
            if (Input.GetKeyDown(KeyCode.Space)) PlayCommitted();
            if (Input.GetKeyDown(KeyCode.R)) ResetAvailable();

            if (_committing || _forceHover || _presentation == null)
                return;

            MapVfxState observed =
                _presentation.GetGroupState(targetGroupId);
            if (observed == _lastObservedState)
                return;

            _lastObservedState = observed;
            if (observed == MapVfxState.Available ||
                observed == MapVfxState.Hovered ||
                observed == MapVfxState.Locked ||
                observed == MapVfxState.Completed)
            {
                _effect.SetState(observed);
            }
        }

        public void PreviewLocked()
        {
            ApplyPersistentState(MapVfxState.Locked);
        }

        public void PreviewAvailable()
        {
            ApplyPersistentState(MapVfxState.Available);
        }

        public void PreviewHovered()
        {
            if (!ReadyForCommands()) return;
            _committing = false;
            _forceHover = true;
            _presentation.SetInteractionEnabled(true);
            _presentation.SetGroupState(
                targetGroupId, MapVfxState.Available);
            _lastObservedState = MapVfxState.Available;
            _effect.SetState(MapVfxState.Hovered);
        }

        public void PreviewCleared()
        {
            ApplyPersistentState(MapVfxState.Completed);
        }

        public void ResetAvailable()
        {
            PreviewAvailable();
        }

        public void PlayCommitted()
        {
            if (!ReadyForCommands() || _committing)
                return;

            _forceHover = false;
            _committing = true;
            _presentation.SetInteractionEnabled(false);
            _presentation.SetGroupState(
                targetGroupId, MapVfxState.Available);
            _effect.SetState(MapVfxState.Hovered);
            _effect.PlayCommitted(() =>
            {
                if (_presentation != null)
                {
                    _presentation.SetGroupState(
                        targetGroupId, MapVfxState.Completed);
                }
                _lastObservedState = MapVfxState.Completed;
                _committing = false;
            });
        }

        private void ApplyPersistentState(MapVfxState state)
        {
            if (!ReadyForCommands()) return;
            _committing = false;
            _forceHover = false;
            _presentation.SetInteractionEnabled(
                state == MapVfxState.Available);
            _presentation.SetGroupState(targetGroupId, state);
            _lastObservedState = state;
            _effect.SetState(state);
        }

        private bool ReadyForCommands()
        {
            return _presentation != null && _effect != null;
        }

        private void AttachPrototype()
        {
            Transform anchor = _presentation.transform.Find(
                $"MapPresentationContent/{targetContentId}");
            if (anchor == null)
            {
                throw new MissingReferenceException(
                    $"归元碑样机找不到地图锚点 {targetContentId}。");
            }

            _placeholderVisual = anchor.Find("Visual");
            if (_placeholderVisual != null)
                _placeholderVisual.gameObject.SetActive(false);

            GameObject prefab = Resources.Load<GameObject>(
                PrototypeResourcePath);
            if (prefab == null)
            {
                throw new MissingReferenceException(
                    $"归元碑样机资源未生成：{PrototypeResourcePath}。");
            }

            GameObject instance = Instantiate(prefab, anchor, false);
            instance.name = "GuiyuanStelePrototype";
            instance.transform.localPosition = new Vector3(0f, 0f, -0.08f);
            _effect = instance.GetComponent<RunCardConversionMapEffect>();
            if (_effect == null)
                throw new MissingComponentException(
                    "归元碑样机 Prefab 缺少 RunCardConversionMapEffect。");
        }
    }
}
