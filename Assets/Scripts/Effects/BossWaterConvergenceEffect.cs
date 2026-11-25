using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 水汇聚的几何、材质及其释放由本对象独占；不修改 BOSS，不启动第二个时钟。
    /// BossEntranceEffect 统一传入进度，取消、完成和销毁均可幂等释放。
    /// </summary>
    public sealed class BossWaterConvergenceEffect : MonoBehaviour
    {
        private readonly LineRenderer[] _ribbons =
            new LineRenderer[BossWaterConvergencePattern.RibbonCount];
        private readonly Vector3[] _points =
            new Vector3[BossWaterConvergencePattern.PointCount];
        private Material _material;
        private Bounds _bounds;
        private BossEntranceEffectPreset _preset;

        public static BossWaterConvergenceEffect Create(
            Transform parent, SpriteRenderer subject, BossEntranceEffectPreset preset)
        {
            Shader shader = Resources.Load<Shader>("Shaders/BossWaterConvergence");
            if (shader == null || subject == null)
            {
                Debug.LogError("水汇聚登场缺少材质 Shader 或角色主体。");
                return null;
            }
            var host = new GameObject("BossWaterConvergence");
            host.transform.SetParent(parent, false);
            var effect = host.AddComponent<BossWaterConvergenceEffect>();
            effect._bounds = subject.bounds;
            effect._preset = preset;
            effect._material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int i = 0; i < effect._ribbons.Length; i++)
            {
                var ribbon = new GameObject("WaterRibbon" + i);
                ribbon.transform.SetParent(host.transform, false);
                LineRenderer line = ribbon.AddComponent<LineRenderer>();
                line.sharedMaterial = effect._material;
                line.useWorldSpace = true;
                line.positionCount = BossWaterConvergencePattern.PointCount;
                line.numCapVertices = 4;
                line.numCornerVertices = 3;
                line.textureMode = LineTextureMode.Stretch;
                line.sortingLayerID = subject.sortingLayerID;
                line.sortingOrder = subject.sortingOrder + 1;
                effect._ribbons[i] = line;
            }
            effect.Apply(0f);
            return effect;
        }

        public void Apply(float progress)
        {
            if (_material == null || _preset == null) return;
            progress = Mathf.Clamp01(progress);
            _material.SetFloat("_Progress", progress);
            float opacity = BossWaterConvergencePattern.Opacity(progress) *
                            _preset.waterOpacity;
            float height = Mathf.Max(0.1f, _bounds.size.y);
            for (int i = 0; i < _ribbons.Length; i++)
            {
                LineRenderer line = _ribbons[i];
                if (line == null) continue;
                line.enabled = opacity > 0.001f;
                for (int point = 0; point < _points.Length; point++)
                {
                    Vector2 local = BossWaterConvergencePattern.Point(
                        i, point / (float)(_points.Length - 1), progress,
                        _preset.waterRadius);
                    _points[point] = _bounds.center +
                        new Vector3(local.x * height, local.y * height, 0f);
                }
                line.SetPositions(_points);
                float width = BossWaterConvergencePattern.Width(i, progress) * height;
                line.startWidth = width;
                line.endWidth = width * 0.25f;
                Color color = BossWaterConvergencePattern.Tint(i);
                color.a = opacity;
                line.startColor = color;
                line.endColor = new Color(0.78f, 0.98f, 1f, opacity * 0.45f);
            }
        }

        public void Release()
        {
            for (int i = 0; i < _ribbons.Length; i++)
                if (_ribbons[i] != null) _ribbons[i].enabled = false;
            if (_material != null)
            {
                if (Application.isPlaying) Destroy(_material);
                else DestroyImmediate(_material);
                _material = null;
            }
        }

        private void OnDestroy() => Release();
    }

    /// <summary>运行时与工作台共享的确定性水流路径；坐标以角色画布高度为单位。</summary>
    public static class BossWaterConvergencePattern
    {
        public const int RibbonCount = 11;
        public const int PointCount = 40;

        public static float Opacity(float progress) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.16f, progress)) *
            (1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.68f, 1f, progress)));

        public static float Width(int index, float progress) =>
            (index < 3 ? 0.011f : 0.020f) * Mathf.Lerp(1f, 0.42f, progress);

        public static Color Tint(int index) => index % 3 == 0
            ? new Color(0.65f, 0.96f, 1f, 1f)
            : new Color(0.12f, 0.63f, 0.78f, 1f);

        public static Vector2 Point(int index, float along, float progress, float radius)
        {
            float gather = Mathf.SmoothStep(0f, 1f, progress);
            if (index < 3)
            {
                // 浅水面上三道不同相位的椭圆涟漪缓缓向内收束。
                float angle = along * Mathf.PI * 2f + index * 0.8f;
                float ringRadius = Mathf.Lerp(0.48f - index * 0.08f,
                    0.12f, gather) * radius;
                return new Vector2(Mathf.Cos(angle) * ringRadius,
                    -0.34f + Mathf.Sin(angle) * ringRadius * 0.22f);
            }

            // 水流从两侧水面滑入，沿主体上旋；末段逐渐收至轮廓内部。
            int stream = index - 3;
            float phase = stream * Mathf.PI * 0.25f;
            float angleFlow = phase + along * Mathf.PI * 1.55f - progress * 3.2f;
            float travel = Mathf.Clamp01(progress * 0.82f + along * 0.42f);
            float radial = Mathf.Lerp(0.54f, 0.10f, travel) * radius;
            float vertical = -0.34f + travel * (0.53f + (stream % 3) * 0.045f);
            return new Vector2(Mathf.Cos(angleFlow) * radial,
                vertical + Mathf.Sin(angleFlow) * radial * 0.20f);
        }
    }
}
