using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.UI;
using WanjieScroll.UI.Daozang;
using WanjieScroll.UI.Views;

namespace WanjieScroll.EditorTools
{
    /// <summary>Validates the same runtime resource keys used by Player factories.
    /// Reads authored components only; never instantiates a UI or uses an editor fallback.</summary>
    public static class RuntimeUiPrefabBuildValidator
    {
        public static List<string> CollectErrors()
        {
            var errors = new List<string>();
            errors.AddRange(ValidateResource(BattleHudFactory.ResourcePath,
                typeof(BattleHudView), Resources.Load<GameObject>(BattleHudFactory.ResourcePath)));
            errors.AddRange(ValidateResource(DaozangFactory.ResourcePath,
                typeof(DaozangView), Resources.Load<GameObject>(DaozangFactory.ResourcePath)));
            return errors;
        }

        internal static List<string> ValidateResource(string key, Type componentType, GameObject prefab)
        {
            var errors = new List<string>();
            if (prefab == null)
                errors.Add("Player 必需预制体无法从 Resources 加载：" + key);
            else if (prefab.GetComponent(componentType) == null)
                errors.Add("Player 预制体缺少根组件：" + key + " / " + componentType.FullName);
            return errors;
        }
    }
}
