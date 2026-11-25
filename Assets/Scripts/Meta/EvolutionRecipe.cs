using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Meta
{
    /// <summary>
    /// 旧进化配方序列化外壳。正式规则为主卡 + 化形印 → 结果卡；
    /// 副卡列表仅为旧数据兼容保留，不参与资格判断或消耗。
    /// </summary>
    [Serializable]
    public class EvolutionRecipe
    {
        public Core.CardConfig primaryCard;     // 主卡
        public List<Core.CardConfig> secondaryCards = new List<Core.CardConfig>(); // 旧兼容字段，忽略
        public Core.CardConfig result;          // 进化结果

        public bool CanEvolve(List<Core.CardConfig> deck, out Core.CardConfig matchedSecondary)
        {
            matchedSecondary = null;
            bool hasPrimary = false;
            foreach (var c in deck)
            {
                if (c == primaryCard) hasPrimary = true;
            }
            return hasPrimary;
        }
    }
}
