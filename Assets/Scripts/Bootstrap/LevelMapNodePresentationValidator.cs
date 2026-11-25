using System;
using System.Collections.Generic;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Checks the shared reward-art contract for map completion effects. The
    /// resolver owns which effect kinds require artwork and their defaults;
    /// this validator owns contextual diagnostics without loading UI objects,
    /// interpreting map names or changing gameplay/configuration.
    /// Character presentations and victory grants have separate validators.
    /// </summary>
    public static class LevelMapNodePresentationValidator
    {
        public static void Validate(
            LevelRunProfile level,
            Func<LevelInteractionEffect, string> resolvePresentationId,
            Func<string, bool> canResolvePresentationId,
            IList<string> errors)
        {
            if (resolvePresentationId == null)
                throw new ArgumentNullException(nameof(resolvePresentationId));
            if (canResolvePresentationId == null)
                throw new ArgumentNullException(nameof(canResolvePresentationId));
            if (errors == null)
                throw new ArgumentNullException(nameof(errors));
            if (level?.nodes == null)
                return;

            foreach (LevelNode node in level.nodes)
            {
                if (node?.completionEffects == null)
                    continue;
                foreach (LevelInteractionEffect effect in node.completionEffects)
                {
                    if (effect == null)
                        continue;
                    string presentationId = resolvePresentationId(effect);
                    if (string.IsNullOrWhiteSpace(presentationId) ||
                        canResolvePresentationId(presentationId))
                    {
                        continue;
                    }

                    errors.Add(
                        $"节点 {node.contentId} 的交互效果 {effect.effectId} " +
                        $"({effect.kind}) 无法解析奖励主体：{presentationId}。");
                }
            }
        }
    }
}
