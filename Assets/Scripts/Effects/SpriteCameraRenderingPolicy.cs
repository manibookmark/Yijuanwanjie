using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns the URP camera contract shared by sprite-heavy map and combat
    /// presentations. The policy keeps post-processing and screen-space AA
    /// enabled when cameras are created dynamically, so a serialized scene
    /// default cannot silently disable the final AA pass.
    /// </summary>
    public static class SpriteCameraRenderingPolicy
    {
        public const bool EnablePostProcessing = true;

        public static readonly AntialiasingMode TargetAntialiasing =
            AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        public static readonly AntialiasingQuality TargetAntialiasingQuality =
            AntialiasingQuality.High;

        /// <summary>
        /// Applies the complete camera contract and is safe to call on a
        /// newly created camera or an existing scene camera.
        /// </summary>
        public static void Apply(Camera camera)
        {
            // Keep this boundary void-returning so Bootstrap never has to reference URP types.
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            UniversalAdditionalCameraData cameraData =
                camera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
            {
                cameraData = camera.gameObject.AddComponent<
                    UniversalAdditionalCameraData>();
            }

            cameraData.renderPostProcessing = EnablePostProcessing;
            cameraData.antialiasing = TargetAntialiasing;
            cameraData.antialiasingQuality = TargetAntialiasingQuality;

            // 画幅收口：地图与战斗的呈现相机强制 16:9 可见区，边条由填充相机补。
            // 所有关卡因此只需要一套归一化坐标，不必按画幅维护多套或做扩图。
            // 神域不经过本策略，保持它自己的 16:9 核心区 + 延展背景方案。
            StageAspectEnforcer.EnsureOn(camera.gameObject);
        }
    }
}
