using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>可选发射蓄光；只延迟表现，不参与伤害结算。</summary>
    public interface IProjectileLaunchVisual
    {
        System.Collections.IEnumerator PrepareLaunch();
    }

    /// <summary>
    /// 投射物外观接口。每种投射物外观（Sprite、程序化刀刃、火焰等）实现此接口。
    /// 通过 visualType 字符串注册到工厂，VfxProfile.visualType 决定用哪种。
    /// </summary>
    public interface IProjectileVisual
    {
        void Initialize(GameObject host, VfxProfile profile, float scale);
        void OnFlightStart();
        void OnFlightEnd();
        Vector3 GetTrailAnchorOffset();
        void Cleanup();
    }

    /// <summary>
    /// 投射物外观工厂。通过 visualType 字符串创建对应实例。
    /// 用 EnsureRegistered 在首次调用时强制注册所有内置实现，
    /// 避免静态构造函数未被触发的问题。
    /// </summary>
    public static class ProjectileVisualFactory
    {
        private static readonly Dictionary<string, Func<IProjectileVisual>> _registry
            = new Dictionary<string, Func<IProjectileVisual>>();
        private static bool _builtinsRegistered;

        /// <summary>注册一种投射物外观</summary>
        public static void Register(string visualType, Func<IProjectileVisual> factory)
        {
            _registry[visualType] = factory;
        }

        /// <summary>确保内置实现已注册</summary>
        private static void EnsureRegistered()
        {
            if (_builtinsRegistered) return;
            _builtinsRegistered = true;

            // 显式注册内置实现，不依赖静态构造函数
            Register("Sprite", () => new SpriteProjectileVisual());
            Register("ProceduralBlade", () => new ProceduralBladeVisual());
            Register(ProceduralKnifeVisual.VisualTypeName, () => new ProceduralKnifeVisual());
            Register(ProceduralFireTalismanVisual.VisualTypeName, () => new ProceduralFireTalismanVisual());
            Register(ProceduralWaterDragonVisual.VisualTypeName, () => new ProceduralWaterDragonVisual());
            Register("ProceduralAxe", () => new ProceduralAxeVisual());
            Register("ProceduralFireVial", () => new ProceduralFireVialVisual());
            Register(ProceduralWandBoltVisual.VisualTypeName,
                () => new ProceduralWandBoltVisual());
            Register(ProceduralSolarBoltVisual.VisualTypeName,
                () => new ProceduralSolarBoltVisual());
            Register(ProceduralFireCrowFeatherVisual.VisualTypeName,
                () => new ProceduralFireCrowFeatherVisual());
            Register(ProceduralFireCrowRocketVisual.VisualTypeName,
                () => new ProceduralFireCrowRocketVisual());
            Register(ProceduralBeeStingVisual.VisualTypeName,
                () => new ProceduralBeeStingVisual());
            Register(RuyiStaffParticleVisual.VisualTypeName,
                () => new RuyiStaffParticleVisual());
            Register("Tornado", () => new TornadoVisual());
        }

        /// <summary>按 visualType 创建实例，找不到时回退到 Sprite</summary>
        public static IProjectileVisual Create(string visualType)
        {
            EnsureRegistered();

            if (!string.IsNullOrEmpty(visualType) && _registry.TryGetValue(visualType, out var factory))
                return factory();

            if (_registry.TryGetValue("Sprite", out var fallback))
                return fallback();

            return null;
        }

        /// <summary>检查是否已注册</summary>
        public static bool IsRegistered(string visualType)
        {
            EnsureRegistered();
            return _registry.ContainsKey(visualType);
        }
    }
}
