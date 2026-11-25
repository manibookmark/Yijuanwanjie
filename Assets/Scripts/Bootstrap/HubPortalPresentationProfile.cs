using UnityEngine;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns the tunable Stage 1 Wanjie-portal presentation values. This is an
    /// asset rather than scene state so Inspector changes made during Play mode
    /// remain available after stopping the scene.
    /// </summary>
    [CreateAssetMenu(
        fileName = "hub-portal-presentation",
        menuName = "Wanjie Scroll/Hub/Portal Presentation")]
    public sealed class HubPortalPresentationProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _contentId =
            "presentation.hub.wanjie-portal.stage01.v1";

        [Header("阵盘对齐（1920×1080 母图坐标）")]
        [Tooltip("阵盘圆心，坐标原点位于母图左上角。")]
        [SerializeField] private Vector2 _formationCentre = new Vector2(960f, 620f);
        [Tooltip("未做透视压缩前的阵盘直径。")]
        [SerializeField, Min(100f)] private float _formationDiameter = 590f;
        [Tooltip("阵盘平面向远处倾斜的角度；越大越扁。")]
        [SerializeField, Range(0f, 82f)] private float _formationTiltDegrees = 69.8f;
        [Tooltip("阵盘在台面内的顺／逆时针微调。")]
        [SerializeField, Range(-15f, 15f)] private float _formationRotationDegrees;

        [Header("Idle 与点击旋启")]
        [Tooltip("Idle 时始终存在的微弱流光；设为 0 才会完全静止。")]
        [SerializeField, Range(0f, 0.45f)] private float _idleBaseFlowStrength = 0.10f;
        [SerializeField, Min(2f)] private float _idleFlowInterval = 6.4f;
        [SerializeField, Range(0.35f, 2.5f)] private float _idleFlowDuration = 1.05f;
        [SerializeField, Range(0f, 1f)] private float _idleFlowStrength = 0.62f;
        [Tooltip("Idle 流光沿阵纹环行的速度。")]
        [SerializeField, Range(0.1f, 2f)] private float _idleFlowSpeed = 0.58f;
        [Tooltip("指针停留时整座阵盘每秒旋转角度。")]
        [SerializeField, Range(1f, 24f)] private float _hoverSpinSpeedDegrees = 9f;
        [Tooltip("指针移出后阵盘回到初始朝向的每秒角度。")]
        [SerializeField, Range(4f, 90f)] private float _hoverReturnSpeedDegrees = 32f;
        [Tooltip("点击后整盘完成快速旋转的时间。")]
        [SerializeField, Range(0.12f, 0.8f)] private float _openingSpinDuration = 0.30f;
        [Tooltip("点击后整盘旋转圈数；1 表示一整圈。")]
        [SerializeField, Range(0.25f, 2f)] private float _openingSpinTurns = 0.90f;
        [Tooltip("旋转中段的向心下沉幅度。")]
        [SerializeField, Range(0f, 0.18f)] private float _openingSinkStrength = 0.05f;

        [Header("入卷牵引")]
        [Tooltip("阵心爆发到关卡文字开始稳定的完整牵引时长。")]
        [SerializeField, Range(0.55f, 1.1f)] private float _pullDuration = 0.76f;
        [Tooltip("纵深光迹数量。光迹按方向分组，不会随机堆满屏幕。")]
        [SerializeField, Range(96, 480)] private int _pullParticleCount = 216;
        [Tooltip("所有牵引光迹的统一尺寸倍率。")]
        [SerializeField, Range(0.7f, 2f)] private float _pullParticleScale = 1.08f;
        [Tooltip("所有牵引光迹的统一亮度倍率。")]
        [SerializeField, Range(0.5f, 1.8f)] private float _pullParticleBrightness = 1.12f;
        [Tooltip("入卷完成后全屏深青场域的遮挡强度。")]
        [SerializeField, Range(0.72f, 0.94f)] private float _selectionScrimAlpha = 0.88f;

        public string ContentId => _contentId;
        public Vector2 FormationCentre => _formationCentre;
        public float FormationDiameter => _formationDiameter;
        public float FormationTiltDegrees => _formationTiltDegrees;
        public float FormationRotationDegrees => _formationRotationDegrees;
        public float IdleBaseFlowStrength => _idleBaseFlowStrength;
        public float IdleFlowInterval => _idleFlowInterval;
        public float IdleFlowDuration => _idleFlowDuration;
        public float IdleFlowStrength => _idleFlowStrength;
        public float IdleFlowSpeed => _idleFlowSpeed;
        public float HoverSpinSpeedDegrees => _hoverSpinSpeedDegrees;
        public float HoverReturnSpeedDegrees => _hoverReturnSpeedDegrees;
        public float OpeningSpinDuration => _openingSpinDuration;
        public float OpeningSpinTurns => _openingSpinTurns;
        public float OpeningSinkStrength => _openingSinkStrength;
        public float PullDuration => _pullDuration;
        public int PullParticleCount => _pullParticleCount;
        public float PullParticleScale => _pullParticleScale;
        public float PullParticleBrightness => _pullParticleBrightness;
        public float SelectionScrimAlpha => _selectionScrimAlpha;

        public Vector2 FormationSize
        {
            get
            {
                float height = _formationDiameter * Mathf.Cos(
                    _formationTiltDegrees * Mathf.Deg2Rad);
                return new Vector2(_formationDiameter, Mathf.Max(48f, height));
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _formationDiameter = Mathf.Max(100f, _formationDiameter);
            _formationTiltDegrees = Mathf.Clamp(_formationTiltDegrees, 0f, 82f);
            _formationRotationDegrees = Mathf.Clamp(_formationRotationDegrees, -15f, 15f);
            _idleBaseFlowStrength = Mathf.Clamp(_idleBaseFlowStrength, 0f, 0.45f);
            _idleFlowDuration = Mathf.Clamp(_idleFlowDuration, 0.35f, 2.5f);
            _idleFlowInterval = Mathf.Max(_idleFlowDuration + 0.6f, _idleFlowInterval);
            _idleFlowStrength = Mathf.Clamp01(_idleFlowStrength);
            _idleFlowSpeed = Mathf.Clamp(_idleFlowSpeed, 0.1f, 2f);
            _hoverSpinSpeedDegrees = Mathf.Clamp(_hoverSpinSpeedDegrees, 1f, 24f);
            _hoverReturnSpeedDegrees = Mathf.Clamp(_hoverReturnSpeedDegrees, 4f, 90f);
            _openingSpinDuration = Mathf.Clamp(_openingSpinDuration, 0.12f, 0.8f);
            _openingSpinTurns = Mathf.Clamp(_openingSpinTurns, 0.25f, 2f);
            _openingSinkStrength = Mathf.Clamp(_openingSinkStrength, 0f, 0.18f);
            _pullDuration = Mathf.Clamp(_pullDuration, 0.55f, 1.1f);
            _pullParticleCount = Mathf.Clamp(_pullParticleCount, 96, 480);
            _pullParticleScale = Mathf.Clamp(_pullParticleScale, 0.7f, 2f);
            _pullParticleBrightness = Mathf.Clamp(_pullParticleBrightness, 0.5f, 1.8f);
            _selectionScrimAlpha = Mathf.Clamp(_selectionScrimAlpha, 0.72f, 0.94f);
        }
#endif
    }
}
