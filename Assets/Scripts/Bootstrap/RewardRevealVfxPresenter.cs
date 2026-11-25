using System.Collections;
using UnityEngine;
using WanjieScroll.Audio;
using WanjieScroll.Effects;
using WanjieScroll.Effects.Map;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 在 Bootstrap 层把通用奖励品阶交给 Effects，并通过透明纹理放在整个
    /// 奖励面板之后。UI 不依赖 Effects，地图和战斗奖励复用同一套庆祝协议。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RewardRevealVfxPresenter : MonoBehaviour
    {
        private const int RewardVfxLayer = 31;
        // Infinity 1.5.1 的非烘焙批渲染会把默认 SortingLayer ID 作为
        // BatchFilterSettings.layer 提交，因此即使对象已移到专用层，批次仍在 0 层。
        // 奖励舞台位于远离正常场景的独立坐标，可安全让专用相机兼容这两个层。
        private const int InfinityBatchLayer = 0;
        private const int RewardVfxCullingMask =
            (1 << RewardVfxLayer) | (1 << InfinityBatchLayer);
        private static readonly Vector3 StagePosition =
            new Vector3(10000f, 10000f, 0f);
        private static readonly Vector3 ResourceStagePosition =
            StagePosition + new Vector3(0f, 100f, 0f);

        private RewardRevealOverlay _overlay;
        private RewardRevealEffect _effect;
        private Camera _camera;
        private RenderTexture _target;
        private ProceduralMapVfx _mapResourceEffect;
        private GameObject _mapResourceRoot;
        private Coroutine _mapResourceActivationRoutine;
        private RenderTexture _mapResourceTarget;

        public bool IsPresenting => _effect != null;

        public void Present(
            RewardRevealOverlay overlay,
            RewardFeedbackItem item)
        {
            Release();
            if (overlay == null || item == null)
                return;

            // 奖励揭示时播放统一音效
            PlayRewardSfx();

            _overlay = overlay;
            _overlay.VisualHoverChanged += OnVisualHoverChanged;
            _overlay.Closed += Release;

            _target = new RenderTexture(
                1024,
                768,
                16,
                RenderTextureFormat.ARGB32)
            {
                name = "RewardRevealVfxTexture",
                antiAliasing = 2,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _target.Create();

            var cameraObject = new GameObject(
                "RewardRevealVfxCamera",
                typeof(Camera));
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.position = StagePosition +
                                              Vector3.back * 10f;
            _camera = cameraObject.GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 1.6f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.clear;
            _camera.cullingMask = RewardVfxCullingMask;
            _camera.targetTexture = _target;
            _camera.allowHDR = false;
            _camera.allowMSAA = true;

            var effectObject = new GameObject("RewardRevealVfx");
            effectObject.layer = RewardVfxLayer;
            effectObject.transform.SetParent(transform, false);
            effectObject.transform.position = StagePosition;
            _effect = effectObject.AddComponent<RewardRevealEffect>();
            _effect.Initialize(item.Accent, item.Rarity);
            SetLayerRecursively(effectObject, RewardVfxLayer);

            _overlay.SetEffectTexture(_target);
            if ((item.Kind == RewardFeedbackKind.BaseMana ||
                 item.Kind == RewardFeedbackKind.CurrentHealth) &&
                _overlay.MapVfxPreview != null)
            {
                BuildResourcePreview(
                    _overlay.MapVfxPreview,
                    item.Kind == RewardFeedbackKind.CurrentHealth
                        ? MapVfxKind.HealthPearl
                        : MapVfxKind.ManaPearl);
            }
        }

        public void Release()
        {
            if (_overlay != null)
            {
                _overlay.SetEffectTexture(null);
                _overlay.SetSubjectEffectTexture(null);
                if (_overlay.MapVfxPreview != null)
                    _overlay.MapVfxPreview.SetTexture(null);
                _overlay.VisualHoverChanged -= OnVisualHoverChanged;
                _overlay.Closed -= Release;
                _overlay = null;
            }

            if (_effect != null)
            {
                Destroy(_effect.gameObject);
                _effect = null;
            }
            if (_camera != null)
            {
                _camera.enabled = false;
                _camera.targetTexture = null;
                Destroy(_camera.gameObject);
                _camera = null;
            }
            if (_target != null)
            {
                _target.Release();
                Destroy(_target);
                _target = null;
            }
            ReleaseResourcePreview();
        }

        private void OnVisualHoverChanged(bool hovered)
        {
            if (hovered)
            {
                _effect?.PlayHoverPulse();
                if (_mapResourceActivationRoutine == null)
                    _mapResourceEffect?.SetState(MapVfxState.Hovered);
            }
            else if (_mapResourceEffect != null &&
                     _mapResourceActivationRoutine == null)
            {
                _mapResourceEffect.SetState(MapVfxState.Available);
            }
        }

        private void LateUpdate()
        {
            if (_camera == null || _target == null ||
                _mapResourceTarget == null)
                return;

            // Infinity 1.5.1 owns a global layer-0 batch registry. Two active RT
            // cameras can traverse that native state in the same frame. Keep a
            // single Camera instance and render the two isolated stages in order.
            RenderStage(
                _target,
                StagePosition + Vector3.back * 10f,
                1.6f);
            RenderStage(
                _mapResourceTarget,
                ResourceStagePosition + new Vector3(0f, 0f, -8f),
                1.48f);
        }

        private void RenderStage(
            RenderTexture target,
            Vector3 position,
            float orthographicSize)
        {
            _camera.transform.position = position;
            _camera.orthographicSize = orthographicSize;
            _camera.targetTexture = target;
            _camera.Render();
        }

        private void BuildResourcePreview(
            RewardMapVfxPreview preview,
            MapVfxKind resourceKind)
        {
            bool health = resourceKind == MapVfxKind.HealthPearl;
            _mapResourceTarget = new RenderTexture(
                640,
                640,
                16,
                RenderTextureFormat.ARGB32)
            {
                name = health
                    ? "RewardMapHealthTexture"
                    : "RewardMapManaTexture",
                antiAliasing = 2,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _mapResourceTarget.Create();

            // LateUpdate renders both RTs sequentially through this one camera.
            // Disable its automatic pass before the second stage is introduced.
            if (_camera != null)
                _camera.enabled = false;

            _mapResourceRoot = new GameObject(
                health
                    ? "RewardMapHealthPearl"
                    : "RewardMapManaPearl");
            _mapResourceRoot.SetActive(false);
            _mapResourceRoot.transform.SetParent(transform, false);
            _mapResourceRoot.transform.position = ResourceStagePosition;
            _mapResourceRoot.layer = RewardVfxLayer;
            preview.SetTexture(_mapResourceTarget);
            _mapResourceActivationRoutine = StartCoroutine(
                ActivateResourcePreviewInStages(resourceKind));
        }

        private IEnumerator ActivateResourcePreviewInStages(
            MapVfxKind resourceKind)
        {
            // Infinity owns native buffers. Let its global manager survive one
            // stable post-reload frame, then activate and initialize together;
            // playback starts on the following frame.
            yield return null;
            if (_mapResourceRoot == null)
            {
                _mapResourceActivationRoutine = null;
                yield break;
            }

            // Infinity 1.5.1 only creates NativeModuleConfig for components
            // that enter an active hierarchy. Initialize synchronously writes
            // renderer sorting, so the preview root must be active first.
            _mapResourceRoot.SetActive(true);
            _mapResourceEffect =
                _mapResourceRoot.AddComponent<ProceduralMapVfx>();
            _mapResourceEffect.Initialize(
                resourceKind,
                MapVfxState.Hidden,
                180);
            SetLayerRecursively(_mapResourceRoot, RewardVfxLayer);

            yield return null;
            if (_mapResourceRoot == null || _mapResourceEffect == null)
            {
                _mapResourceActivationRoutine = null;
                yield break;
            }
            if (_mapResourceEffect != null &&
                _mapResourceEffect.gameObject.activeInHierarchy)
                _mapResourceEffect.PlayRewardArrival();
            _mapResourceActivationRoutine = null;
        }

        private void ReleaseResourcePreview()
        {
            if (_mapResourceActivationRoutine != null)
            {
                StopCoroutine(_mapResourceActivationRoutine);
                _mapResourceActivationRoutine = null;
            }
            if (_mapResourceRoot != null)
                Destroy(_mapResourceRoot);
            _mapResourceRoot = null;
            _mapResourceEffect = null;
            if (_mapResourceTarget != null)
            {
                _mapResourceTarget.Release();
                Destroy(_mapResourceTarget);
                _mapResourceTarget = null;
            }
            if (_camera != null && _target != null)
            {
                _camera.transform.position =
                    StagePosition + Vector3.back * 10f;
                _camera.orthographicSize = 1.6f;
                _camera.targetTexture = _target;
                _camera.enabled = true;
            }
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        private void OnDestroy()
        {
            Release();
        }

        private static void PlayRewardSfx()
        {
            AudioManager.PlaySfxSafe(SfxType.RewardReveal);
        }
    }
}
