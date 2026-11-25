using UnityEngine;
using WanjieScroll.Effects;
using WanjieScroll.UI.Presentations;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns the persistent trigger contract and tunable timeline for the first
    /// Stage 1 Hub restoration. The Hub, rather than the level being exited,
    /// decides whether this milestone still needs to be presented.
    /// </summary>
    [CreateAssetMenu(
        fileName = "hub-first-restoration-presentation",
        menuName = "Wanjie Scroll/Hub/First Restoration Presentation")]
    public sealed class HubArrivalPresentationProfile : ScriptableObject
    {
        /// <summary>Resources 路径（含首访演出档案的装配与测试档回放共用）。</summary>
        public const string ResourcesPath =
            "Content/Hub/hub-first-restoration-presentation";

        [Header("Identity and trigger")]
        [SerializeField] private string _contentId =
            "presentation.hub.first-restoration.stage01.v1";
        [Tooltip("完成该关卡且本演出尚未记入存档时，首次进入 Hub 播放。")]
        [SerializeField] private string _requiredClearedLevelContentId =
            "level.tutorial.huaguoshan.v1";
        [SerializeField] private string _characterPresentationId =
            CharacterPresentationIds.ScrollSpiritLevelMilestone;
        [SerializeField] private string _characterPresentationVariantId =
            CharacterPresentationVariantIds.TutorialHuaguoshanHubAwakening;
        [Tooltip("神域暗场、卷灵化光、万界阵与天箓唤醒的专用特效配置。")]
        [SerializeField] private HubRealmRestorationProfile _restorationVfx;

        public string ContentId => _contentId;
        public string RequiredClearedLevelContentId =>
            _requiredClearedLevelContentId;
        public string CharacterPresentationId => _characterPresentationId;
        public string CharacterPresentationVariantId =>
            _characterPresentationVariantId;
        public HubRealmRestorationProfile RestorationVfx => _restorationVfx;

        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(_contentId))
                return Fail("Hub 首次复苏演出缺少 contentId。", out error);
            if (string.IsNullOrWhiteSpace(_requiredClearedLevelContentId))
                return Fail("Hub 首次复苏演出缺少前置通关 contentId。", out error);
            if (string.IsNullOrWhiteSpace(_characterPresentationId))
                return Fail("Hub 首次复苏演出缺少角色演出 contentId。", out error);
            if (string.IsNullOrWhiteSpace(_characterPresentationVariantId))
                return Fail("Hub 首次复苏演出缺少角色演出变体 ID。", out error);
            if (_restorationVfx == null)
                return Fail("Hub 首次复苏演出缺少专用神域特效配置。", out error);
            if (!_restorationVfx.TryValidate(out string vfxError))
                return Fail(vfxError, out error);

            error = null;
            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

    }
}
