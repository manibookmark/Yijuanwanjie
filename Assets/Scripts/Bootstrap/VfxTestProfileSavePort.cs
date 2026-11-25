using System;
using UnityEngine;
using WanjieScroll.Effects;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Result returned by the editor-owned F7 profile saver. Runtime code only
    /// sees this narrow result and never depends on UnityEditor APIs.
    /// </summary>
    public readonly struct VfxProfileSaveResult
    {
        public bool Success { get; }
        public string Message { get; }

        private VfxProfileSaveResult(bool success, string message)
        {
            Success = success;
            Message = message ?? string.Empty;
        }

        public static VfxProfileSaveResult Saved(string message)
        {
            return new VfxProfileSaveResult(true, message);
        }

        public static VfxProfileSaveResult Failed(string message)
        {
            return new VfxProfileSaveResult(false, message);
        }
    }

    /// <summary>
    /// Bootstrap-side port for explicitly persisting an F7 tuning profile.
    /// The editor assembly installs the implementation after every domain
    /// reload; player builds keep the button unavailable.
    /// </summary>
    public static class VfxTestProfileSavePort
    {
        private static Func<VfxProfile, VfxProfileSaveResult> _saveProfile;
        private static Func<ScriptableObject, VfxProfileSaveResult> _saveAsset;

        public static bool IsAvailable => _saveProfile != null;
        public static bool IsAssetSaveAvailable => _saveAsset != null;

#if UNITY_EDITOR
        public static void RegisterEditorSaver(
            Func<VfxProfile, VfxProfileSaveResult> saveProfile)
        {
            _saveProfile = saveProfile;
        }

        public static void RegisterEditorAssetSaver(
            Func<ScriptableObject, VfxProfileSaveResult> saveAsset)
        {
            _saveAsset = saveAsset;
        }
#endif

        public static VfxProfileSaveResult Save(VfxProfile profile)
        {
            if (profile == null)
                return VfxProfileSaveResult.Failed("当前卡牌没有可保存的 VfxProfile。");
            if (_saveProfile == null)
                return VfxProfileSaveResult.Failed("当前运行环境不支持保存特效资产。");

            try
            {
                return _saveProfile(profile);
            }
            catch (Exception exception)
            {
                return VfxProfileSaveResult.Failed(
                    $"保存失败：{exception.Message}");
            }
        }

        /// <summary>
        /// 保存 F7 中的其他窄范围调参资产；玩家构建中始终不可用。
        /// </summary>
        public static VfxProfileSaveResult SaveAsset(
            ScriptableObject asset)
        {
            if (asset == null)
                return VfxProfileSaveResult.Failed("当前没有可保存的调参资产。");
            if (_saveAsset == null)
                return VfxProfileSaveResult.Failed("当前运行环境不支持保存调参资产。");

            try
            {
                return _saveAsset(asset);
            }
            catch (Exception exception)
            {
                return VfxProfileSaveResult.Failed(
                    $"保存失败：{exception.Message}");
            }
        }
    }
}
