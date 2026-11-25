using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WanjieScroll.Content
{
    /// <summary>
    /// 从 JSON 权威内容源加载卷内容。
    /// 每个 .json 文件表示一个 <see cref="VolumeContent"/>，使用
    /// <see cref="JsonUtility"/> 反序列化。JsonUtility 不支持顶层数组，
    /// 因此每个文件必须是单个对象而非数组。
    /// </summary>
    public static class ContentImporter
    {
        /// <summary>
        /// 扫描 <paramref name="contentSourcePath"/> 目录（含子目录）下所有 .json 文件，
        /// 逐个反序列化为 <see cref="VolumeContent"/> 并返回。文件按路径排序以保证确定性。
        /// </summary>
        /// <param name="contentSourcePath">内容源根目录，例如仓库根的 ContentSource/。</param>
        /// <returns>反序列化得到的卷内容列表。</returns>
        /// <exception cref="ArgumentException"><paramref name="contentSourcePath"/> 为空。</exception>
        /// <exception cref="DirectoryNotFoundException">目录不存在。</exception>
        /// <exception cref="InvalidOperationException">某个文件无法解析或缺少 volumeId。</exception>
        public static List<VolumeContent> LoadFromJson(string contentSourcePath)
        {
            if (string.IsNullOrWhiteSpace(contentSourcePath))
                throw new ArgumentException("内容源路径不能为空。", nameof(contentSourcePath));
            if (!Directory.Exists(contentSourcePath))
                throw new DirectoryNotFoundException($"内容源目录不存在：{contentSourcePath}");

            string[] jsonFiles = Directory.GetFiles(contentSourcePath, "*.json", SearchOption.AllDirectories);
            Array.Sort(jsonFiles, StringComparer.OrdinalIgnoreCase);

            var volumes = new List<VolumeContent>(jsonFiles.Length);
            foreach (string file in jsonFiles)
            {
                string json = File.ReadAllText(file, Encoding.UTF8);
                VolumeContent volume = ContentPackageLoader.ParseVolume(json, file);
                volumes.Add(volume);
            }

            return volumes;
        }
    }
}
