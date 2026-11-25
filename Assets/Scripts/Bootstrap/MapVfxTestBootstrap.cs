using UnityEngine;
using WanjieScroll.Effects.Map;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 隔离测试场景入口。只保留默认配置加载和隐藏快捷键；
    /// 所有地图构建、交互和完成检测委托给 <see cref="MapPresentation"/>。
    /// </summary>
    public sealed class MapVfxTestBootstrap : MonoBehaviour
    {
        private const string DefaultProfilePath =
            "MapPreviewProfiles/T0-tutorial-huaguoshan";

        [SerializeField] private MapPreviewProfile previewProfile;

        private MapPresentation _presentation;
        private string _focusGroupId;

        public MapPresentation Presentation => _presentation;
        public MapPreviewProfile Profile => previewProfile;
        public bool HasDeveloperHud => false;

        private void Start()
        {
            BuildPreview();
        }

        private void Update()
        {
            if (_presentation == null || !_presentation.IsBuilt) return;

            // Hidden debug shortcuts — no on-screen legend.
            if (Input.GetKeyDown(KeyCode.Alpha1))
                SetPreviewState(MapVfxState.Locked);
            if (Input.GetKeyDown(KeyCode.Alpha2))
                SetPreviewState(MapVfxState.Available);
            if (Input.GetKeyDown(KeyCode.Alpha3))
                SetPreviewState(MapVfxState.Hovered);
            if (Input.GetKeyDown(KeyCode.Space))
                _presentation.TryPlayCompletion(_focusGroupId);
            if (Input.GetKeyDown(KeyCode.R))
                SetPreviewState(MapVfxState.Available);
        }

        public void BuildPreview()
        {
            if (_presentation != null && _presentation.IsBuilt) return;

            if (previewProfile == null)
                previewProfile = Resources.Load<MapPreviewProfile>(
                    DefaultProfilePath);
            if (previewProfile == null)
                throw new System.InvalidOperationException(
                    $"Missing map preview profile at {DefaultProfilePath}.");

            _focusGroupId = previewProfile.debugFocusGroupId;
            _presentation = gameObject.AddComponent<MapPresentation>();
            _presentation.Build(previewProfile, null);
        }

        public void BuildPreview(MapPreviewProfile profile)
        {
            if (profile == null)
                throw new System.ArgumentNullException(nameof(profile));
            if (_presentation != null && _presentation.IsBuilt)
                throw new System.InvalidOperationException(
                    "A preview is already built for this bootstrap.");

            previewProfile = profile;
            BuildPreview();
        }

        private void SetPreviewState(MapVfxState state)
        {
            if (_presentation == null || string.IsNullOrEmpty(_focusGroupId))
                return;
            _presentation.SetGroupState(_focusGroupId, state);
        }
    }
}
