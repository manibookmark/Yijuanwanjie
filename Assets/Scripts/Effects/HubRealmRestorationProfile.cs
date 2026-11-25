using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Stores the complete visual language of the first ancient-realm awakening.
    /// Triggering and save history remain outside this profile; every value here
    /// can be tuned without changing progression or character presentation rules.
    /// </summary>
    [CreateAssetMenu(
        fileName = "hub-first-restoration-vfx",
        menuName = "Wanjie Scroll/Hub/First Restoration VFX")]
    public sealed class HubRealmRestorationProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _contentId =
            "vfx.hub.first-restoration.stage01.v1";

        [Header("沉寂神域")]
        [Tooltip("卷灵说话期间覆盖神域的暗色；Alpha 由下方独立强度参数控制。")]
        [SerializeField] private Color _dormantVeilColor =
            new Color(0.006f, 0.015f, 0.024f, 1f);
        [Tooltip("卷灵出现和说话期间的遮罩强度；数值越大，神域越暗。")]
        [SerializeField, Range(0f, 1f)]
        private float _spiritDialogueVeilOpacity = 0.9f;
        [Tooltip("圆形光浪揭开神域的最大半径（1920×1080 画布像素）。")]
        [SerializeField, Range(800f, 2200f)]
        private float _realmRevealRadius = 1450f;
        [Tooltip("光浪边缘的柔化宽度。")]
        [SerializeField, Range(20f, 420f)]
        private float _realmRevealFeather = 210f;

        [Header("卷灵化光")]
        [Tooltip("仅重播特效、没有卷灵位置快照时使用的备用画布坐标；(0,0) 是屏幕中心。")]
        [SerializeField] private Vector2 _spiritEnergyOrigin =
            new Vector2(-115f, -65f);
        [Tooltip("卷灵开始退场后，等待多少秒再生成飞向八卦阵的粒子。0 表示同帧衔接。")]
        [SerializeField, Range(0f, 1f)]
        private float _spiritParticleStartDelay;
        [SerializeField, Range(16, 480)]
        private int _spiritParticleCount = 180;
        [SerializeField, Range(0.25f, 3f)]
        private float _spiritParticleSize = 1f;
        [SerializeField, Range(0.25f, 2.5f)]
        private float _spiritParticleBrightness = 1.15f;
        [SerializeField, Range(0f, 420f)]
        private float _spiritParticleSpread = 155f;
        [SerializeField, Range(-420f, 420f)]
        private float _spiritFlightArc = 175f;
        [SerializeField, Range(0.15f, 3f)]
        private float _spiritFlightDuration = 0.72f;

        [Header("神域与设施唤醒时间线（均从卷灵退场开始计时）")]
        [SerializeField, Range(0f, 3f)]
        private float _realmRevealDelay = 0.78f;
        [SerializeField, Range(0.2f, 4f)]
        private float _realmRevealDuration = 1.45f;
        [SerializeField, Range(0f, 3f)]
        private float _portalRevealDelay = 0.86f;
        [SerializeField, Range(0.2f, 4f)]
        private float _portalRevealDuration = 1.28f;
        [SerializeField, Range(0f, 4f)]
        private float _tianluRevealDelay = 1.58f;
        [SerializeField, Range(0.2f, 4f)]
        private float _tianluRevealDuration = 1.08f;
        [SerializeField, Range(0f, 2f)]
        private float _settleDuration = 0.38f;

        [Header("设施显化回弹")]
        [SerializeField, Range(0.35f, 1f)]
        private float _hiddenScale = 0.72f;
        [SerializeField, Range(0f, 0.2f)]
        private float _scaleOvershoot = 0.065f;
        [SerializeField] private Color _jadeEnergyColor =
            new Color(0.42f, 1f, 0.82f, 1f);

        public string ContentId => _contentId;
        public Color DormantVeilColor
        {
            get
            {
                Color colour = _dormantVeilColor;
                colour.a = SpiritDialogueVeilOpacity;
                return colour;
            }
        }
        public float SpiritDialogueVeilOpacity =>
            Mathf.Clamp01(_spiritDialogueVeilOpacity);
        public float RealmRevealRadius => Mathf.Max(1f, _realmRevealRadius);
        public float RealmRevealFeather => Mathf.Max(1f, _realmRevealFeather);
        public Vector2 SpiritEnergyOrigin => _spiritEnergyOrigin;
        public float SpiritParticleStartDelay =>
            Mathf.Max(0f, _spiritParticleStartDelay);
        public int SpiritParticleCount => Mathf.Max(1, _spiritParticleCount);
        public float SpiritParticleSize => Mathf.Max(0.01f, _spiritParticleSize);
        public float SpiritParticleBrightness =>
            Mathf.Max(0f, _spiritParticleBrightness);
        public float SpiritParticleSpread => Mathf.Max(0f, _spiritParticleSpread);
        public float SpiritFlightArc => _spiritFlightArc;
        public float SpiritFlightDuration => Mathf.Max(0.01f, _spiritFlightDuration);
        public float RealmRevealDelay => Mathf.Max(0f, _realmRevealDelay);
        public float RealmRevealDuration => Mathf.Max(0.01f, _realmRevealDuration);
        public float PortalRevealDelay => Mathf.Max(0f, _portalRevealDelay);
        public float PortalRevealDuration => Mathf.Max(0.01f, _portalRevealDuration);
        public float TianluRevealDelay => Mathf.Max(0f, _tianluRevealDelay);
        public float TianluRevealDuration => Mathf.Max(0.01f, _tianluRevealDuration);
        public float SettleDuration => Mathf.Max(0f, _settleDuration);
        public float HiddenScale => Mathf.Clamp(_hiddenScale, 0.05f, 1f);
        public float ScaleOvershoot => Mathf.Max(0f, _scaleOvershoot);
        public Color JadeEnergyColor => _jadeEnergyColor;

        public float TotalDuration => Mathf.Max(
            SpiritParticleStartDelay + SpiritFlightDuration,
            Mathf.Max(
                RealmRevealDelay + RealmRevealDuration,
                Mathf.Max(
                    PortalRevealDelay + PortalRevealDuration,
                    TianluRevealDelay + TianluRevealDuration))) +
            SettleDuration;

        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(_contentId))
                return Fail("神域复苏特效缺少 contentId。", out error);
            if (_spiritDialogueVeilOpacity < 0f ||
                _spiritDialogueVeilOpacity > 1f)
                return Fail("卷灵对话遮罩强度必须在 [0,1]。", out error);
            if (_realmRevealRadius <= 0f || _realmRevealFeather <= 0f)
                return Fail("神域揭幕半径与柔边必须大于 0。", out error);
            if (_spiritParticleCount <= 0)
                return Fail("神域复苏粒子数量必须大于 0。", out error);
            if (_spiritParticleSize <= 0f)
                return Fail("神域复苏粒子大小必须大于 0。", out error);
            if (_spiritFlightDuration <= 0f || _realmRevealDuration <= 0f ||
                _portalRevealDuration <= 0f || _tianluRevealDuration <= 0f)
                return Fail("神域复苏各阶段时长必须大于 0。", out error);
            if (_spiritParticleStartDelay < 0f || _realmRevealDelay < 0f ||
                _portalRevealDelay < 0f || _tianluRevealDelay < 0f ||
                _settleDuration < 0f)
                return Fail("神域复苏延迟与收束时长不能为负数。", out error);
            if (_hiddenScale <= 0f || _hiddenScale > 1f ||
                _scaleOvershoot < 0f)
                return Fail("神域设施沉寂缩放或回弹量非法。", out error);

            error = null;
            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _dormantVeilColor.a = 1f;
            _spiritDialogueVeilOpacity = Mathf.Clamp01(
                _spiritDialogueVeilOpacity);
            _realmRevealRadius = Mathf.Clamp(_realmRevealRadius, 800f, 2200f);
            _realmRevealFeather = Mathf.Clamp(_realmRevealFeather, 20f, 420f);
            _spiritParticleStartDelay = Mathf.Clamp01(
                _spiritParticleStartDelay);
            _spiritParticleCount = Mathf.Clamp(_spiritParticleCount, 16, 480);
            _spiritParticleSize = Mathf.Clamp(_spiritParticleSize, 0.25f, 3f);
            _spiritParticleBrightness = Mathf.Clamp(
                _spiritParticleBrightness, 0.25f, 2.5f);
            _spiritParticleSpread = Mathf.Clamp(_spiritParticleSpread, 0f, 420f);
            _spiritFlightArc = Mathf.Clamp(_spiritFlightArc, -420f, 420f);
            _spiritFlightDuration = Mathf.Clamp(_spiritFlightDuration, 0.15f, 3f);
            _realmRevealDelay = Mathf.Clamp(_realmRevealDelay, 0f, 3f);
            _realmRevealDuration = Mathf.Clamp(_realmRevealDuration, 0.2f, 4f);
            _portalRevealDelay = Mathf.Clamp(_portalRevealDelay, 0f, 3f);
            _portalRevealDuration = Mathf.Clamp(_portalRevealDuration, 0.2f, 4f);
            _tianluRevealDelay = Mathf.Clamp(_tianluRevealDelay, 0f, 4f);
            _tianluRevealDuration = Mathf.Clamp(_tianluRevealDuration, 0.2f, 4f);
            _settleDuration = Mathf.Clamp(_settleDuration, 0f, 2f);
            _hiddenScale = Mathf.Clamp(_hiddenScale, 0.35f, 1f);
            _scaleOvershoot = Mathf.Clamp(_scaleOvershoot, 0f, 0.2f);
        }
#endif
    }
}
