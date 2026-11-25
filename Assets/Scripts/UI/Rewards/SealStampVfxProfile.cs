using UnityEngine;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>
    /// 钤印命中与成果卡消散共享的可调表现参数。资源中的改动可由
    /// SealAssignmentCoordinator Inspector 持久保存并在重播时即时读取。
    /// </summary>
    [CreateAssetMenu(
        fileName = "SealStampVfxProfile",
        menuName = "WanjieScroll/UI/Seal Stamp VFX Profile")]
    public sealed class SealStampVfxProfile : ScriptableObject
    {
        internal const string DefaultResourcePath =
            "UI/SealStampVfxProfile";

        [Header("砸印 · 灵气被压出")]
        [SerializeField, Range(8, 320)] private int impactShardCount = 96;
        [SerializeField, Range(0, 480)] private int impactDustCount = 180;
        [SerializeField, Range(0.2f, 1.5f)] private float impactDuration = 0.72f;
        [SerializeField, Range(0f, 0.4f)] private float impactDelaySpread = 0.10f;
        [SerializeField, Range(0f, 180f)] private float impactStartRadius = 58f;
        [SerializeField, Range(80f, 520f)] private float impactEndRadius = 310f;
        [Tooltip("小于 1 会把喷出范围纵向压扁，形成玉印下压后向两侧挤出的感觉。")]
        [SerializeField, Range(0.15f, 1.5f)] private float impactVerticalCompression = 0.55f;
        [SerializeField, Range(2f, 80f)] private float impactShardLength = 20f;
        [SerializeField, Range(0.15f, 8f)] private float impactShardWidth = 0.85f;
        [SerializeField, Range(0.25f, 6f)] private float impactDustMinSize = 0.6f;
        [SerializeField, Range(0.5f, 10f)] private float impactDustMaxSize = 2.2f;
        [SerializeField, Range(0f, 60f)] private float impactTangentialDrift = 16f;
        [SerializeField, Range(0f, 1f)] private float impactAlpha = 0.78f;

        [Header("成果卡 · 随风化粒")]
        [SerializeField, Range(16, 480)] private int dissolveParticleCount = 280;
        [SerializeField, Range(0.3f, 2f)] private float dissolveDuration = 0.82f;
        [SerializeField, Range(0f, 0.6f)] private float dissolveDelaySpread = 0.22f;
        [Tooltip("粒子在整个退场期间的总位移；X 为风向，Y 为上扬。")]
        [SerializeField] private Vector2 dissolveWind = new Vector2(175f, 58f);
        [SerializeField, Range(0f, 100f)] private float dissolveTurbulence = 34f;
        [SerializeField, Range(0.25f, 12f)] private float dissolveParticleMinSize = 0.65f;
        [SerializeField, Range(0.5f, 20f)] private float dissolveParticleMaxSize = 2.6f;
        [SerializeField, Range(0f, 1f)] private float dissolveAlpha = 0.86f;

        public int ImpactShardCount => impactShardCount;
        public int ImpactDustCount => impactDustCount;
        public float ImpactDuration => impactDuration;
        public float ImpactDelaySpread => impactDelaySpread;
        public float ImpactStartRadius => impactStartRadius;
        public float ImpactEndRadius => impactEndRadius;
        public float ImpactVerticalCompression => impactVerticalCompression;
        public float ImpactShardLength => impactShardLength;
        public float ImpactShardWidth => impactShardWidth;
        public float ImpactDustMinSize => impactDustMinSize;
        public float ImpactDustMaxSize => impactDustMaxSize;
        public float ImpactTangentialDrift => impactTangentialDrift;
        public float ImpactAlpha => impactAlpha;
        public int DissolveParticleCount => dissolveParticleCount;
        public float DissolveDuration => dissolveDuration;
        public float DissolveDelaySpread => dissolveDelaySpread;
        public Vector2 DissolveWind => dissolveWind;
        public float DissolveTurbulence => dissolveTurbulence;
        public float DissolveParticleMinSize => dissolveParticleMinSize;
        public float DissolveParticleMaxSize => dissolveParticleMaxSize;
        public float DissolveAlpha => dissolveAlpha;

        internal static SealStampVfxProfile LoadDefault()
        {
            return Resources.Load<SealStampVfxProfile>(
                DefaultResourcePath);
        }

        private void OnValidate()
        {
            impactEndRadius = Mathf.Max(impactStartRadius, impactEndRadius);
            impactDustMaxSize = Mathf.Max(
                impactDustMinSize,
                impactDustMaxSize);
            dissolveParticleMaxSize = Mathf.Max(
                dissolveParticleMinSize,
                dissolveParticleMaxSize);
        }
    }
}
