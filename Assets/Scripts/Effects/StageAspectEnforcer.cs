using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 把游戏可见画幅收口到 16:9 核心舞台，使所有关卡的节点坐标只维护一套。
    ///
    /// 为什么不在设置里"固定分辨率"：PlayerSettings 当前是 FullScreenWindow
    /// （fullscreenMode: 1），全屏直接使用显示器分辨率，配置里的分辨率列表挡不住
    /// 2560x1600(16:10)、2160x1440(3:2) 这类显示器；改用独占全屏时缩放方式又由
    /// 显卡驱动决定，不可控。因此在渲染层收口，且两个方向策略不同：
    ///
    ///   - 比 16:9 更宽（21:9 / 32:9）：相机全屏不动，核心 16:9 仍按高度 100%
    ///     居中，多出来的左右宽度由各楼层的横向扩图承接（缺扩图时会露出相机
    ///     底色，MapPresentation / BattlefieldView 已有对应警告）。不需要边条。
    ///   - 比 16:9 更窄（16:10 / 3:2 / 4:3）：只按高度适配会把核心两侧裁掉，
    ///     所以这里把主相机收成屏幕内最大的 16:9 矩形，上下留边；边条由一张
    ///     只做纯色清屏的相机补上（不受主相机 rect 影响）。
    ///
    /// 效果：16:9 及更宽时坐标与画面比例与改造前一致（核心永远高度 100%、居中）；
    /// 更窄时核心整体可见、居中，只牺牲上下边条。任何画幅下节点坐标、命中区
    /// 都是同一套，不需要按画幅维护多套坐标。
    ///
    /// 挂载方式：<see cref="EnsureOn"/> 由 SpriteCameraRenderingPolicy 在地图/战斗
    /// 相机创建时调用（神域不经过该策略，保持它自己的 16:9 核心区＋延展背景方案）。
    /// 相机的指针换算走 camera.ScreenToWorldPoint / WorldToScreenPoint，会自动考虑
    /// rect，因此点击与"世界坐标贴到 Overlay UI"的逻辑无需改动。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageAspectEnforcer : MonoBehaviour
    {
        public const float TargetAspect = 16f / 9f;
        private const int BorderCameraDepth = -100;

        [SerializeField, Tooltip(
            "比 16:9 更窄时上下留边的颜色（更宽时不留边，左右由横向扩图承接）。")]
        private Color borderColor = new Color(0.055f, 0.043f, 0.031f, 1f);

        [SerializeField, Tooltip(
            "跟随 Camera.main。挂在与相机同一物体上时建议关闭，直接使用本物体相机。")]
        private bool followMainCamera;

        private Camera _borderCamera;
        private Camera _stageCamera;
        private int _appliedWidth;
        private int _appliedHeight;

        /// <summary>当前被强制为 16:9 的舞台相机；可能为空。</summary>
        public Camera StageCamera { get { return _stageCamera; } }

        /// <summary>舞台（16:9 可见区）在视口中的归一化矩形。</summary>
        public Rect StageViewportRect
        {
            get
            {
                return _stageCamera != null
                    ? _stageCamera.rect
                    : new Rect(0f, 0f, 1f, 1f);
            }
        }

        /// <summary>把强制器挂到指定相机物体上（已存在则复用）。</summary>
        public static StageAspectEnforcer EnsureOn(GameObject host)
        {
            if (host == null) return null;
            StageAspectEnforcer enforcer = host.GetComponent<StageAspectEnforcer>();
            if (enforcer == null)
                enforcer = host.AddComponent<StageAspectEnforcer>();
            return enforcer;
        }

        private void OnEnable()
        {
            EnsureBorderCamera();
            Apply(true);
        }

        private void OnDisable()
        {
            if (_stageCamera != null &&
                _stageCamera.rect != new Rect(0f, 0f, 1f, 1f))
            {
                // 离开该场景时还原满屏，避免影响其它场景或预览相机。
                _stageCamera.rect = new Rect(0f, 0f, 1f, 1f);
            }
            _stageCamera = null;
        }

        private void Update()
        {
            Apply(false);
        }

        /// <summary>
        /// 屏幕尺寸对应的舞台矩形（视口归一化坐标）。
        /// 16:9 及更宽返回满屏（宽度交给各层横向扩图）；更窄时上下居中留边。
        /// </summary>
        public static Rect ComputeStageRect(int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return new Rect(0f, 0f, 1f, 1f);
            float screenAspect = screenWidth / (float)screenHeight;
            if (screenAspect >= TargetAspect)
                return new Rect(0f, 0f, 1f, 1f);
            float height = screenAspect / TargetAspect;
            return new Rect(0f, (1f - height) * 0.5f, 1f, height);
        }

        /// <summary>是否存在比 16:9 更窄/更宽而被裁掉的画幅（供 UI/日志判断）。</summary>
        public bool IsLetterboxed
        {
            get
            {
                Rect rect = StageViewportRect;
                return rect.width < 0.999f || rect.height < 0.999f;
            }
        }

        private Camera ResolveTarget()
        {
            if (followMainCamera) return Camera.main;
            return GetComponent<Camera>();
        }

        private void Apply(bool force)
        {
            EnsureBorderCamera();

            Camera target = ResolveTarget();
            if (target != _stageCamera)
            {
                _stageCamera = target;
                force = true;
            }
            if (_stageCamera == null) return;

            if (!force &&
                Screen.width == _appliedWidth &&
                Screen.height == _appliedHeight)
            {
                return;
            }
            if (Screen.width <= 0 || Screen.height <= 0) return;

            _appliedWidth = Screen.width;
            _appliedHeight = Screen.height;
            // 主相机仍按自己的背景色清楚它那块 rect，边条交给填充相机。
            _stageCamera.rect = ComputeStageRect(Screen.width, Screen.height);
        }

        /// <summary>
        /// 边条填充相机：只做纯色清屏（cullingMask=0），深度低于主相机。
        /// 没有它时 camera.rect 之外是未定义内容（上一帧残留）。
        /// </summary>
        private void EnsureBorderCamera()
        {
            if (_borderCamera != null && _borderCamera.gameObject.activeInHierarchy)
            {
                _borderCamera.backgroundColor = borderColor;
                return;
            }
            var go = new GameObject("StageBorderCamera", typeof(Camera));
            go.transform.SetParent(transform, false);
            var camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = borderColor;
            camera.cullingMask = 0;
            camera.orthographic = true;
            camera.orthographicSize = 1f;
            camera.depth = BorderCameraDepth;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 10f;
            // 不加 AudioListener，也不打 MainCamera 标签，避免抢 Camera.main。
            _borderCamera = camera;
        }

        /// <summary>找到场景中的强制器（诊断用；UI 侧请直接读 Camera.main.rect）。</summary>
        public static StageAspectEnforcer FindActive()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<StageAspectEnforcer>();
#else
            return Object.FindObjectOfType<StageAspectEnforcer>();
#endif
        }
    }
}
