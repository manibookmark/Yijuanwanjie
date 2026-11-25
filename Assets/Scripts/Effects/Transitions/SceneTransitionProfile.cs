using UnityEngine;

namespace WanjieScroll.Effects.Transitions
{
    /// <summary>
    /// 统一转场参数资产。保存稳定 contentId、覆盖/揭幕/HUD 时长和 Ready 超时。
    /// </summary>
    [CreateAssetMenu(
        fileName = "SceneTransitionProfile",
        menuName = "WanjieScroll/Transitions/Profile")]
    public sealed class SceneTransitionProfile : ScriptableObject
    {
        [Header("稳定标识")]
        public string contentId = "transition.paper-cloud.standard.v1";

        [Header("地图时序")]
        public float mapFocusDuration = 0.30f;
        public float mapOverviewDuration = 0.35f;
        public float nodePurificationDuration = 1.10f;

        [Header("遮幕时序")]
        public float coverDuration = 0.50f;
        public float revealDuration = 0.45f;
        public float hudFadeDuration = 0.20f;

        [Header("返回转场")]
        [Tooltip("返回地图时合拢动画的时长（秒）。0 则复用 coverDuration。")]
        public float returnCoverDuration = 0.70f;
        [Tooltip("返回地图时揭幕动画的时长（秒）。设为很小的值可实现合拢后即刻显现地图。")]
        public float returnRevealDuration = 0.10f;

        [Header("超时")]
        [Min(1f)]
        public float destinationReadyTimeout = 15f;

        /// <summary>校验配置完整性。</summary>
        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(contentId))
            {
                error = "SceneTransitionProfile contentId 不能为空。";
                return false;
            }
            if (coverDuration <= 0f || revealDuration <= 0f)
            {
                error = $"{contentId} 的 coverDuration/revealDuration 必须为正。";
                return false;
            }
            if (returnCoverDuration < 0f || returnRevealDuration <= 0f)
            {
                error = $"{contentId} 的 returnRevealDuration 必须为正，returnCoverDuration 不能为负。";
                return false;
            }
            if (destinationReadyTimeout < 1f)
            {
                error = $"{contentId} 的 destinationReadyTimeout 必须 >= 1s。";
                return false;
            }
            error = null;
            return true;
        }
    }
}
