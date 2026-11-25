using System;
using UnityEngine;
using UnityEngine.Rendering;
using WanjieScroll.Core;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns the one shared spirit-qi particle skin used by map mana and reward
    /// grades. Callers choose choreography only; shader, palette and silhouette
    /// remain identical so abundance never turns into a different substance.
    /// </summary>
    public static class SpiritQiParticleStyle
    {
        public const string ShaderName =
            "WanjieScroll/Effects/Map Aura Particle";
        public const string ShaderAssetPath =
            "Assets/Shaders/MapAuraParticle.shader";
        public const string RuntimeTemplateResourcePath =
            "Effects/Map/ManaSpiritReturningCurrentSource_Infinity";

        public const float StandardOpacity = 0.68f;
        public const float StandardSoftness = 0.64f;
        public const float StandardWispShape = 0.08f;

        public static Material CreateRuntimeMaterial(string name)
        {
            return CreateRuntimeMaterial(
                name,
                SpiritEnergyColorPalette.ParticleDeep,
                SpiritEnergyColorPalette.ParticleCore,
                SpiritEnergyColorPalette.ParticleHighlight);
        }

        public static Material CreateRuntimeMaterial(
            string name,
            Color deepColor,
            Color coreColor,
            Color highlightColor)
        {
            Material template = Resources.Load<Material>(
                RuntimeTemplateResourcePath);
            Shader shader = template != null
                ? template.shader
                : Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError(
                    $"[SpiritQi] 找不到共用灵气 Shader：{ShaderName}");
                return null;
            }

            var material = template != null
                ? new Material(template)
                : new Material(shader);
            material.name = name;
            material.hideFlags = HideFlags.HideAndDontSave;
            Apply(
                material,
                deepColor,
                coreColor,
                highlightColor);
            return material;
        }

        public static void Apply(
            Material material,
            float opacity = StandardOpacity,
            float softness = StandardSoftness,
            float wispShape = StandardWispShape)
        {
            Apply(
                material,
                SpiritEnergyColorPalette.ParticleDeep,
                SpiritEnergyColorPalette.ParticleCore,
                SpiritEnergyColorPalette.ParticleHighlight,
                opacity,
                softness,
                wispShape);
        }

        public static void Apply(
            Material material,
            Color deepColor,
            Color coreColor,
            Color highlightColor,
            float opacity = StandardOpacity,
            float softness = StandardSoftness,
            float wispShape = StandardWispShape)
        {
            if (material == null)
                throw new ArgumentNullException(nameof(material));

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new InvalidOperationException(
                    $"找不到共用灵气 Shader：{ShaderName}");

            material.shader = shader;
            material.shaderKeywords = Array.Empty<string>();
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor(
                "_DeepColor",
                deepColor);
            material.SetColor(
                "_CoreColor",
                coreColor);
            material.SetColor(
                "_HighlightColor",
                highlightColor);
            material.SetFloat("_Opacity", Mathf.Clamp01(opacity));
            material.SetFloat(
                "_Softness",
                Mathf.Clamp(softness, 0.05f, 0.90f));
            material.SetFloat(
                "_WispShape",
                Mathf.Clamp01(wispShape));
        }
    }
}
