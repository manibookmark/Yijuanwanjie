using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// Shared runtime-only soft particle material. The texture is generated in
    /// memory, so particle visuals do not depend on imported sprites.
    /// </summary>
    internal static class ProceduralParticleMaterial
    {
        private const int TextureSize = 32;
        private static Texture2D _softDisc;
        private static Texture2D _softBox;
        private static Material _additive;
        private static Material _alphaBlended;
        private static Material _alphaBlendedBox;

        public static Material GetAdditive()
        {
            if (_additive != null) return _additive;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return null;

            _additive = new Material(shader)
            {
                name = "ProceduralSoftParticleAdditive",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = GetSoftDisc()
            };
            _additive.SetInt("_SrcBlend",
                (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _additive.SetInt("_DstBlend",
                (int)UnityEngine.Rendering.BlendMode.One);
            _additive.SetInt("_ZWrite", 0);
            _additive.renderQueue = 3000;
            return _additive;
        }

        /// <summary>
        /// Alpha-blended variant for dark particle silhouettes. Additive
        /// blending cannot display the black-iron body of the Ruyi Staff.
        /// </summary>
        public static Material GetAlphaBlended()
        {
            if (_alphaBlended != null) return _alphaBlended;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return null;

            _alphaBlended = new Material(shader)
            {
                name = "ProceduralSoftParticleAlphaBlended",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = GetSoftDisc()
            };
            _alphaBlended.SetInt("_SrcBlend",
                (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _alphaBlended.SetInt("_DstBlend",
                (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _alphaBlended.SetInt("_ZWrite", 0);
            _alphaBlended.renderQueue = 3000;
            return _alphaBlended;
        }

        /// <summary>
        /// Alpha-blended material whose opacity stays flat across most of the
        /// particle quad. Unlike the radial soft disc, stretching this texture
        /// does not turn a staff shaft into a pointed dart silhouette.
        /// </summary>
        public static Material GetAlphaBlendedBox()
        {
            if (_alphaBlendedBox != null) return _alphaBlendedBox;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return null;

            _alphaBlendedBox = new Material(shader)
            {
                name = "ProceduralBoxParticleAlphaBlended",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = GetSoftBox()
            };
            _alphaBlendedBox.SetInt("_SrcBlend",
                (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _alphaBlendedBox.SetInt("_DstBlend",
                (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _alphaBlendedBox.SetInt("_ZWrite", 0);
            _alphaBlendedBox.renderQueue = 3000;
            return _alphaBlendedBox;
        }

        private static Texture2D GetSoftDisc()
        {
            if (_softDisc != null) return _softDisc;

            _softDisc = new Texture2D(
                TextureSize,
                TextureSize,
                TextureFormat.RGBA32,
                false)
            {
                name = "ProceduralSoftParticleDisc",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float normalizedX =
                        (x + 0.5f) / TextureSize * 2f - 1f;
                    float normalizedY =
                        (y + 0.5f) / TextureSize * 2f - 1f;
                    float distance = Mathf.Sqrt(
                        normalizedX * normalizedX +
                        normalizedY * normalizedY);
                    float alpha = Mathf.Clamp01(1f - distance);
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    pixels[y * TextureSize + x] =
                        new Color(1f, 1f, 1f, alpha);
                }
            }

            _softDisc.SetPixels(pixels);
            _softDisc.Apply(false, true);
            return _softDisc;
        }

        private static Texture2D GetSoftBox()
        {
            if (_softBox != null) return _softBox;

            _softBox = new Texture2D(
                TextureSize,
                TextureSize,
                TextureFormat.RGBA32,
                false)
            {
                name = "ProceduralSoftParticleBox",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float normalizedX =
                        (x + 0.5f) / TextureSize * 2f - 1f;
                    float normalizedY =
                        (y + 0.5f) / TextureSize * 2f - 1f;
                    float edgeDistance = Mathf.Min(
                        1f - Mathf.Abs(normalizedX),
                        1f - Mathf.Abs(normalizedY));
                    float alpha = Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(0f, 0.15f, edgeDistance));
                    pixels[y * TextureSize + x] =
                        new Color(1f, 1f, 1f, alpha);
                }
            }

            _softBox.SetPixels(pixels);
            _softBox.Apply(false, true);
            return _softBox;
        }
    }
}
