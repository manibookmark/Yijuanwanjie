using UnityEngine;

namespace WanjieScroll.UI.Daozang
{
    /// <summary>
    /// 道藏界面的权威表现资产。
    /// 布局参数集中在此，调整布局只需改 asset 不改代码。
    /// 设计稿 1672×941，换算到 1920×1080 scale=1.148。
    /// </summary>
    [CreateAssetMenu(
        fileName = "DefaultDaozangPresentation",
        menuName = "万界长卷/UI/道藏表现配置")]
    public sealed class DaozangPresentationProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;
        public const string DefaultResourcePath =
            "UI/Daozang/DefaultDaozangPresentation";

        [Header("身份与版本")]
        public int schemaVersion = CurrentSchemaVersion;
        public string profileId = "ui.daozang.standard.v1";
        [Min(1)] public int revision = 1;

        [Header("整体布局")]
        // 设计稿：灵玉icon top=31, height=38 → 总览icon top=23, height=58
        // topBar 高度 = max(31+38, 23+58) = 81, 加点余量
        public float topBarHeight = 81f;       // 70px header area + 11px padding
        public float bottomBarHeight = 220f;  // 保留底部按钮区
        // 设计稿：header left=77, 5个header+4个gap(58) = 5*261+4*58 = 1305+232=1537, 右边距=1672-77-1537=58 → 左右对称
        public float contentMarginX = 77f;    // 设计稿 left margin
        public float columnSpacing = 58f;     // 设计稿 header gap

        [Header("标题牌")]
        // 列宽 (1920-2*77-4*58)/5 = 306.8, header 416×224 → height = 306.8*224/416 ≈ 165
        public float headerHeight = 165f;

        [Header("符箓卡片")]
        public float talismanHeight = 640f;    // width = 640*256/768 ≈ 213
        public float talismanRowSpacing = 6f;

        [Header("底部按钮")]
        public float actionIconSize = 155f;
        public float actionBtnCenterX = 152f;
        public float actionBtnCenterY = 129f;

        [Header("总览按钮")]
        // 设计稿：right=33, top=23, icon=58px → 中心偏移 (33+29)*1.148=71, (23+29)*1.148=60
        public float overviewIconSize = 67f;   // 58 * 1.148
        public float overviewOffsetX = 71f;   // 距右边缘（含 icon 半宽的缩放中心偏移）
        public float overviewOffsetY = 60f;   // 距顶部（含 icon 半宽的缩放中心偏移）

        [Header("灵玉")]
        // 设计稿：icon 38×38 at (774, 31), 数字在右边
        public float lingyuIconSize = 44f;    // 38 * 1.148

        [Header("详情面板")]
        public float detailPanelWidth = 1150f;
        public float detailPanelMargin = 0f;

        [Header("选中色")]
        public Color bodySelectedColor = new(1f, 0.96f, 0.85f, 1f);
        public Color bodyLockedColor = new(0.55f, 0.52f, 0.48f, 0.35f);
    }
}
