using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// 龙卷风（风涡）投射物外观。
    /// 四层 ParticleSystem 组合：
    ///   - 窄底、中段、宽顶三层风带构成漏斗轮廓
    ///   - 飘散叶屑反向旋转，留下短暂飞行轨迹
    /// 风带通过 orbital velocity 绕中心运动，并沿速度方向拉伸，而不是原地旋转圆形粒子。
    /// </summary>
    public class TornadoVisual : IProjectileVisual
    {
        private SpriteRenderer _renderer;
        private ParticleSystem _windCore;
        private ParticleSystem _windBand;
        private ParticleSystem _windCrown;
        private ParticleSystem _windLeaf;
        private Material _windMaterial;

        static TornadoVisual()
        {
            ProjectileVisualFactory.Register("Tornado", () => new TornadoVisual());
        }

        public void Initialize(GameObject host, VfxProfile profile, float scale)
        {
            _renderer = host.GetComponent<SpriteRenderer>();
            _renderer.sortingOrder = 20;
            _renderer.sprite = null;
            _renderer.enabled = false;

            host.transform.localScale = Vector3.one * scale;

            Color coreColor = profile != null ? profile.proceduralBladeColor : new Color(0.86f, 0.93f, 0.96f, 0.95f);
            Color bandColor = profile != null ? profile.proceduralBladeEdgeColor : new Color(0.52f, 0.64f, 0.7f, 0.72f);
            Color leafColor = new Color(0.72f, 0.56f, 0.28f, 0.72f);
            Transform tornadoRoot = EnsureTornadoRoot(host.transform);

            // 窄底：小半径、高速，构成漏斗尖端。
            _windCore = CreateVortexLayer(tornadoRoot, "WindCore",
                coreColor, new Vector3(0f, -0.42f, 0f),
                0.14f, 8f, 34, 48, 22,
                0.22f, 0.3f, ParticleSystemSimulationSpace.Local);

            // 中段：扩大环带，并与底层反向旋转，强化乱流感。
            _windBand = CreateVortexLayer(tornadoRoot, "WindBand",
                bandColor, new Vector3(0f, -0.04f, 0f),
                0.34f, -6f, 42, 60, 21,
                0.3f, 0.42f, ParticleSystemSimulationSpace.Local);

            // 宽顶：最大的风带圈，形成一眼可识别的龙卷风口。
            _windCrown = CreateVortexLayer(tornadoRoot, "WindCrown",
                coreColor, new Vector3(0f, 0.38f, 0f),
                0.58f, 4.5f, 48, 64, 23,
                0.36f, 0.5f, ParticleSystemSimulationSpace.Local);

            // 叶屑：独立暖色小粒子，World Space 留下短暂轨迹。
            _windLeaf = CreateVortexLayer(tornadoRoot, "WindLeaf",
                leafColor, new Vector3(0f, 0.12f, 0f),
                0.72f, -2.8f, 12, 32, 20,
                0.13f, 0.7f, ParticleSystemSimulationSpace.World);
        }

        public void OnFlightStart()
        {
            if (_windCore != null) { _windCore.gameObject.SetActive(true); RestartEmission(_windCore); }
            if (_windBand != null) { _windBand.gameObject.SetActive(true); RestartEmission(_windBand); }
            if (_windCrown != null) { _windCrown.gameObject.SetActive(true); RestartEmission(_windCrown); }
            if (_windLeaf != null) { _windLeaf.gameObject.SetActive(true); RestartEmission(_windLeaf); }
        }

        public void OnFlightEnd()
        {
            // 停止发射，已发射的粒子自然消散形成余韵
            if (_windCore != null) { var em = _windCore.emission; em.enabled = false; }
            if (_windBand != null) { var em = _windBand.emission; em.enabled = false; }
            if (_windCrown != null) { var em = _windCrown.emission; em.enabled = false; }
            if (_windLeaf != null) { var em = _windLeaf.emission; em.enabled = false; }
        }

        public Vector3 GetTrailAnchorOffset() => Vector3.zero;

        public void Cleanup()
        {
            if (_windMaterial != null)
            {
                if (Application.isPlaying) Object.Destroy(_windMaterial);
                else Object.DestroyImmediate(_windMaterial);
            }
        }

        // ==================== 内部方法 ====================

        private static Transform EnsureTornadoRoot(Transform host)
        {
            Transform root = host.Find("TornadoRoot");
            if (root == null)
            {
                var rootGo = new GameObject("TornadoRoot");
                rootGo.transform.SetParent(host, false);
                rootGo.AddComponent<TornadoUprightAnchor>();
                root = rootGo.transform;
            }

            root.localPosition = Vector3.zero;
            root.localScale = Vector3.one;
            root.rotation = Quaternion.identity;
            return root;
        }

        private ParticleSystem CreateVortexLayer(
            Transform host, string name, Color color,
            Vector3 localPosition,
            float shapeRadius, float orbitalSpeed,
            int emissionRate, int maxParticles, int sortingOrder,
            float particleSize, float lifetime,
            ParticleSystemSimulationSpace simulationSpace)
        {
            Transform child = host.Find(name);
            if (child == null)
            {
                var childGo = new GameObject(name, typeof(ParticleSystem));
                childGo.transform.SetParent(host, false);
                child = childGo.transform;
            }

            var ps = child.GetComponent<ParticleSystem>();
            ps.gameObject.SetActive(true);
            ps.transform.localPosition = localPosition;
            ps.transform.localRotation = Quaternion.identity;
            ps.transform.localScale = Vector3.one;

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = simulationSpace;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            // 颜色只在 colorOverLifetime 中设置一次，避免 alpha 重复相乘变得过淡。
            main.startColor = Color.white;
            main.maxParticles = maxParticles;
            main.startSize = particleSize;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;

            // shape: 圆环发射
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = shapeRadius;
            shape.radiusThickness = 1f;
            shape.angle = 0f;

            // emission
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = emissionRate;

            // 让粒子本身绕 Z 轴公转。旧版只旋转圆形粒子自身，视觉上等于没旋转。
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            // Unity 要求 orbitalX/Y/Z 的 MinMaxCurve mode 完全一致。
            // X/Y 默认为 Constant，所以 Z 也必须使用 Constant，否则 Emit 时会报错。
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(orbitalSpeed);
            velocity.radial = new ParticleSystem.MinMaxCurve(-0.12f, 0.04f);

            // sizeOverLifetime: 小→大→消散
            var sizeLife = ps.sizeOverLifetime;
            sizeLife.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.3f);
            sizeCurve.AddKey(0.3f, 1f);
            sizeCurve.AddKey(1f, 0.1f);
            // sizeOverLifetime 是对 startSize 的倍率。旧实现把两个尺寸相乘，
            // 风带最终只有 0.009 世界单位，在 F7 相机下不到 1 像素。
            sizeLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // colorOverLifetime: alpha 渐淡
            var colorLife = ps.colorOverLifetime;
            colorLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(color.a * 0.5f, 0.5f), new GradientAlphaKey(0f, 1f) }
            );
            colorLife.color = new ParticleSystem.MinMaxGradient(grad);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GetWindMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.velocityScale = 0.16f;
            renderer.lengthScale = 1.8f;
            renderer.cameraVelocityScale = 0f;
            renderer.sortingOrder = sortingOrder;

            return ps;
        }

        private static void RestartEmission(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var em = ps.emission;
            em.enabled = true;
            ps.Play();
            // 首帧立即形成风团，避免短途飞行结束前还没积累出足够粒子。
            ps.Emit(8);
        }

        private Material GetWindMaterial()
        {
            if (_windMaterial != null) return _windMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return null;

            _windMaterial = new Material(shader)
            {
                name = "TornadoWindMaterial",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = CreateSoftCircleTexture(64)
            };
            return _windMaterial;
        }

        private static Texture2D CreateSoftCircleTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float center = size / 2f;
            float maxDist = center;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(1f - dist / maxDist);
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            return tex;
        }
    }

    /// <summary>
    /// 投射物会朝飞行方向旋转；龙卷风需要保持屏幕空间竖直，才能稳定呈现上宽下窄的漏斗轮廓。
    /// </summary>
    internal sealed class TornadoUprightAnchor : MonoBehaviour
    {
        private void LateUpdate()
        {
            transform.rotation = Quaternion.identity;
        }
    }
}
