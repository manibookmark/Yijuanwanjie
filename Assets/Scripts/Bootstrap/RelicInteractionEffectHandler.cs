using System;
using WanjieScroll.Content;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 把地图 GrantRelic 效果提交到局外遗珍存档。地图只携带稳定
    /// targetContentId，不依赖显示名称或具体遗珍类型。
    /// </summary>
    public sealed class RelicInteractionEffectHandler :
        ILevelInteractionEffectHandler
    {
        private readonly MetaProgression _meta;
        private readonly Func<string, RelicData> _resolveRelic;
        private readonly Action<MetaProgression> _save;
        private readonly bool _replayOwnedForSession;

        public LevelInteractionEffectKind Kind =>
            LevelInteractionEffectKind.GrantRelic;

        public RelicInteractionEffectHandler(
            MetaProgression meta,
            Action<MetaProgression> save = null,
            bool replayOwnedForSession = false)
            : this(
                meta,
                ResolveFromCatalog,
                save,
                replayOwnedForSession)
        {
        }

        public RelicInteractionEffectHandler(
            MetaProgression meta,
            Func<string, RelicData> resolveRelic,
            Action<MetaProgression> save = null,
            bool replayOwnedForSession = false)
        {
            _meta = meta ?? throw new ArgumentNullException(nameof(meta));
            _resolveRelic = resolveRelic ??
                throw new ArgumentNullException(nameof(resolveRelic));
            _save = save;
            _replayOwnedForSession = replayOwnedForSession;
        }

        public bool TryApply(
            LevelInteractionEffect effect,
            out int valueBefore,
            out int valueAfter,
            out int appliedAmount,
            out string error)
        {
            valueBefore = 0;
            valueAfter = 0;
            appliedAmount = 0;
            error = null;

            if (effect == null ||
                effect.kind != LevelInteractionEffectKind.GrantRelic)
            {
                error = "遗珍处理器只接受 GrantRelic 效果。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(effect.targetContentId))
            {
                error = $"交互效果 {effect.effectId} 缺少遗珍 targetContentId。";
                return false;
            }

            valueBefore = _meta.IsRelicUnlocked(effect.targetContentId)
                ? 1
                : 0;
            RelicData relic = _resolveRelic(effect.targetContentId);
            if (relic == null)
            {
                error = $"找不到遗珍内容：{effect.targetContentId}。";
                return false;
            }
            if (!Enum.IsDefined(typeof(RelicType), relic.relicType))
            {
                error = $"遗珍 {effect.targetContentId} 使用了未知类型：" +
                        relic.relicType + "。";
                return false;
            }

            bool changed = _meta.TryUnlockRelic(
                relic.contentId,
                (RelicType)relic.relicType,
                relic.name,
                relic.description);
            valueAfter = _meta.IsRelicUnlocked(effect.targetContentId)
                ? 1
                : 0;
            if (valueAfter == 0)
            {
                error = $"遗珍 {effect.targetContentId} 未能写入进度。";
                return false;
            }

            if (changed)
                _save?.Invoke(_meta);
            // 编辑器直开地图的临时会话允许重播已拥有遗珍的获得反馈，
            // 但仍不重复写入或保存局外进度。正式会话保持严格幂等。
            appliedAmount = changed || _replayOwnedForSession
                ? effect.amount
                : 0;
            return true;
        }

        private static RelicData ResolveFromCatalog(string contentId)
            => ContentCatalog.Current.TryGetRelic(
                contentId, out RelicData relic)
                ? relic
                : null;
    }
}
