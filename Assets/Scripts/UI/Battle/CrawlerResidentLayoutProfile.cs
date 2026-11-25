using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.UI.Views
{
    [Serializable]
    public struct CrawlerResidentSlotLayout
    {
        [Tooltip("1920×1080 参考分辨率下，角色中心相对左下角的位置。")]
        public Vector2 anchoredPosition;

        [Min(64f), Tooltip("该槽位的角色显示高度。")]
        public float displayHeight;
    }

    /// <summary>
    /// 驻场英灵 Hover 螺旋灵气的已解析参数快照：形状来自共享
    /// 布局，身体范围和 Offset 来自当前英灵表现资产。
    /// </summary>
    public readonly struct CrawlerResidentAuraSettings
    {
        public readonly int ParticleCount;
        public readonly float Radius;
        public readonly float CyclesPerSecond;
        public readonly float ParticleSize;
        public readonly Vector2 Offset;
        public readonly Rect BodyBounds;

        public CrawlerResidentAuraSettings(
            int particleCount,
            float radius,
            float cyclesPerSecond,
            float particleSize,
            Vector2 offset,
            Rect? bodyBounds = null)
        {
            ParticleCount = particleCount;
            Radius = radius;
            CyclesPerSecond = cyclesPerSecond;
            ParticleSize = particleSize;
            Offset = offset;
            BodyBounds = bodyBounds ?? new Rect(0.2f, 0.06f, 0.6f, 0.88f);
        }
    }

    /// <summary>
    /// 驻灵轨道的共享布局、首帧碎片聚散与 Hover 灵气参数。英灵身份、序列帧和
    /// 专属动作不放在这里，避免每增加一名英灵就复制一遍三槽空间规则。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewCrawlerResidentLayout",
        menuName = "万界长卷/战斗/驻场英灵布局")]
    public sealed class CrawlerResidentLayoutProfile : ScriptableObject
    {
        public const int MinMaterializationParticleCount = 48;
        public const int MaxMaterializationParticleCount = 512;
        public const float MinMaterializationSizeScale = 0.35f;
        public const float MaxMaterializationSizeScale = 1.08f;
        public const int MinAuraParticleCount = 8;
        public const int MaxAuraParticleCount = 64;
        public const float MinAuraRadius = 32f;
        public const float MaxAuraRadius = 96f;
        public const float MinAuraCyclesPerSecond = 0.1f;
        public const float MaxAuraCyclesPerSecond = 0.8f;
        public const float MinAuraParticleSize = 2f;
        public const float MaxAuraParticleSize = 10f;

        private const string DefaultResourcePath =
            "UI/Battle/DefaultCrawlerResidentLayout";

        [Header("三槽纵深队列（1920×1080 参考分辨率）")]
        public List<CrawlerResidentSlotLayout> slots =
            new List<CrawlerResidentSlotLayout>
            {
                new CrawlerResidentSlotLayout
                {
                    anchoredPosition = new Vector2(240f, 465f),
                    displayHeight = 260f
                },
                new CrawlerResidentSlotLayout
                {
                    anchoredPosition = new Vector2(275f, 565f),
                    displayHeight = 220f
                },
                new CrawlerResidentSlotLayout
                {
                    anchoredPosition = new Vector2(310f, 665f),
                    displayHeight = 220f
                }
            };

        [Header("首帧碎片聚散")]
        [Range(0.65f, 0.95f)] public float entryDuration = 0.82f;
        [Range(0.45f, 0.85f)] public float exitDuration = 0.64f;
        [Range(0.5f, 1f)] public float entryStartScale = 0.84f;
        public Vector2 entryStartOffset = new Vector2(-10f, 24f);
        [Range(
            MinMaterializationParticleCount,
            MaxMaterializationParticleCount)]
        public int materializationParticleCount = 160;
        [Range(
            MinMaterializationSizeScale,
            MaxMaterializationSizeScale)]
        public float materializationSizeScale = 0.94f;

        [Header("凝神数字")]
        public Vector2 durationOffset = new Vector2(-12f, 6f);
        [Range(20, 42)] public int durationFontSize = 32;

        [Header("Hover 螺旋灵气")]
        [Range(MinAuraParticleCount, MaxAuraParticleCount)]
        public int hoverAuraParticleCount = 36;
        [Tooltip("按身体范围适配后的松紧：64 为 1 倍，32–96 对应 0.5–1.5 倍；保留既有序列化值。")]
        [Range(MinAuraRadius, MaxAuraRadius)]
        public float hoverAuraRadius = 64f;
        [Range(MinAuraCyclesPerSecond, MaxAuraCyclesPerSecond)]
        public float hoverAuraCyclesPerSecond = 0.32f;
        [Range(MinAuraParticleSize, MaxAuraParticleSize)]
        public float hoverAuraParticleSize = 5f;

        public int SlotCount => slots?.Count ?? 0;

        public CrawlerResidentSlotLayout GetSlot(int index)
        {
            if (slots == null || slots.Count == 0)
                throw new InvalidOperationException("驻场英灵布局没有配置槽位。");
            return slots[Mathf.Clamp(index, 0, slots.Count - 1)];
        }

        public CrawlerResidentMaterializationSettings
            GetMaterializationSettings()
        {
            return new CrawlerResidentMaterializationSettings(
                materializationParticleCount,
                materializationSizeScale);
        }

        public CrawlerResidentAuraSettings GetHoverAuraSettings(
            Vector2 offset,
            Rect? bodyBounds = null)
        {
            return new CrawlerResidentAuraSettings(
                hoverAuraParticleCount,
                hoverAuraRadius,
                hoverAuraCyclesPerSecond,
                hoverAuraParticleSize,
                offset,
                bodyBounds);
        }

        public bool TryValidate(out string error)
        {
            if (slots == null || slots.Count != 3)
                return Fail("驻场英灵布局必须恰好配置 3 个槽位。", out error);
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].displayHeight <= 0f)
                    return Fail($"驻场英灵槽位 {i + 1} 的显示高度必须大于 0。", out error);
            }
            if (entryDuration < 0.65f || entryDuration > 0.95f)
                return Fail("驻场英灵入场时长必须位于 0.65–0.95 秒。", out error);
            if (exitDuration < 0.45f || exitDuration > 0.85f)
                return Fail("驻场英灵退场时长必须位于 0.45–0.85 秒。", out error);
            if (materializationParticleCount <
                    MinMaterializationParticleCount ||
                materializationParticleCount >
                    MaxMaterializationParticleCount)
            {
                return Fail("驻场英灵首帧碎片数必须位于 48–512。", out error);
            }
            if (materializationSizeScale <
                    MinMaterializationSizeScale ||
                materializationSizeScale >
                    MaxMaterializationSizeScale)
            {
                return Fail("驻场英灵首帧碎片缩放必须位于 0.35–1.08。", out error);
            }
            if (durationFontSize < 20 || durationFontSize > 42)
                return Fail("驻场英灵凝神字号必须位于 20–42。", out error);
            if (hoverAuraParticleCount < MinAuraParticleCount ||
                hoverAuraParticleCount > MaxAuraParticleCount)
            {
                return Fail("驻场英灵 Hover 灵气粒子数必须位于 8–64。", out error);
            }
            if (hoverAuraRadius < MinAuraRadius ||
                hoverAuraRadius > MaxAuraRadius)
            {
                return Fail("驻场英灵 Hover 灵气半径必须位于 32–96。", out error);
            }
            if (hoverAuraCyclesPerSecond < MinAuraCyclesPerSecond ||
                hoverAuraCyclesPerSecond > MaxAuraCyclesPerSecond)
            {
                return Fail("驻场英灵 Hover 灵气速度必须位于 0.1–0.8。", out error);
            }
            if (hoverAuraParticleSize < MinAuraParticleSize ||
                hoverAuraParticleSize > MaxAuraParticleSize)
            {
                return Fail("驻场英灵 Hover 灵气粒子尺寸必须位于 2–10。", out error);
            }
            if (entryStartScale <= 0f || entryStartScale > 1f)
                return Fail("驻场英灵入场起始缩放必须位于 (0, 1]。", out error);

            error = null;
            return true;
        }

        public static CrawlerResidentLayoutProfile LoadDefault()
        {
            CrawlerResidentLayoutProfile profile =
                Resources.Load<CrawlerResidentLayoutProfile>(DefaultResourcePath);
            if (profile == null)
            {
                throw new InvalidOperationException(
                    $"找不到默认驻场英灵布局：Resources/{DefaultResourcePath}。");
            }
            if (!profile.TryValidate(out string error))
                throw new InvalidOperationException(error);
            return profile;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
