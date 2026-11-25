using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects.Terrain
{
    public interface ITerrainEffectVisual
    {
        void Initialize(TerrainVfxProfile profile, TerrainEffectChange initial);
        void Place(TerrainEffectChange change);
        void Trigger(TerrainEffectChange change);
        void BeginRemove(TerrainEffectChange change, Action completed);
    }

    /// <summary>
    /// 地形外观工厂。Presenter 不含火焰、冰面等分支；新增视觉类型只注册一次。
    /// </summary>
    public static class TerrainEffectVisualFactory
    {
        private static readonly Dictionary<string, Func<GameObject, ITerrainEffectVisual>>
            Factories = new Dictionary<string, Func<GameObject, ITerrainEffectVisual>>();
        private static bool _registered;

        public static void Register(
            string visualType,
            Func<GameObject, ITerrainEffectVisual> factory)
        {
            if (string.IsNullOrWhiteSpace(visualType) || factory == null) return;
            Factories[visualType] = factory;
        }

        public static ITerrainEffectVisual Create(
            string visualType,
            GameObject host)
        {
            EnsureRegistered();
            if (!string.IsNullOrWhiteSpace(visualType) &&
                Factories.TryGetValue(visualType, out var factory))
            {
                return factory(host);
            }
            return Factories[ProceduralFlameTerrainVisual.VisualTypeName](host);
        }

        private static void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;
            Register(
                ProceduralFlameTerrainVisual.VisualTypeName,
                host => host.AddComponent<ProceduralFlameTerrainVisual>());
        }
    }
}
