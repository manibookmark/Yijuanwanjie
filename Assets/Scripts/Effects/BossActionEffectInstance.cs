using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Effects.Projectiles;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 一次性、无图片依赖的 BOSS 动作骨架。线条负责碎屏裂纹／盾牌轮廓，
    /// 自然粒子由 BossActionInfinityParticles 独立拥有并可在骨架销毁后收尾。
    /// 本类虽已进入复杂度审查区间，全部构建方法仍只服务同一个
    /// _strokes + _elapsed 生命周期；新增第四种视觉语言时再拆分构建策略。
    /// 显式命中锚点只替换同一布局的中心，不增加视觉种类或显示状态所有者。
    /// </summary>
    public sealed class BossActionEffectInstance : MonoBehaviour
    {
        public const string RootName = "BossActionEffect";
        public const int BaseSortingOrder = 94;

        private sealed class Stroke
        {
            public LineRenderer Line;
            public Vector3[] Points;
            public Color Color;
            public bool Reveals;
        }

        private readonly List<Stroke> _strokes = new List<Stroke>();
        private BossActionEffectPreset _preset;
        private BossActionEffectLayoutSample _layout;
        private float _elapsed;

        public BossActionEffectPreset Preset => _preset;

        public static GameObject Play(
            Transform parent,
            Camera presentationCamera,
            float planeZ,
            Rect sourceViewportRect,
            BossActionEffectPreset preset,
            Vector3? projectedImpactAnchor = null)
        {
            if (parent == null || presentationCamera == null ||
                preset == null || !preset.TryValidate(out _))
            {
                return null;
            }

            BossActionEffectLayoutSample layout;
            if (projectedImpactAnchor.HasValue)
            {
                if (!BossActionEffectLayout.TryResolveAtViewportAnchor(
                        preset.visualKind,
                        sourceViewportRect,
                        projectedImpactAnchor.Value,
                        out layout))
                    return null;
            }
            else
            {
                layout = BossActionEffectLayout.Resolve(
                    preset.visualKind, sourceViewportRect);
            }

            var root = new GameObject($"{RootName}_{preset.effectId}");
            root.transform.SetParent(parent, true);
            var instance = root.AddComponent<BossActionEffectInstance>();
            instance.Build(
                presentationCamera,
                planeZ,
                preset,
                layout);
            return root;
        }

        private void Build(
            Camera presentationCamera,
            float planeZ,
            BossActionEffectPreset preset,
            BossActionEffectLayoutSample layout)
        {
            _preset = preset;
            _layout = layout;
            float viewHeight = GetVisibleHeight(
                presentationCamera,
                planeZ);
            float viewWidth = viewHeight * presentationCamera.aspect;
            transform.position = ViewportToPlane(
                presentationCamera,
                _layout.Center,
                planeZ);

            switch (preset.visualKind)
            {
                case BossActionEffectVisualKind.HeavyPunchImpact:
                    BuildHeavyPunch(viewWidth, viewHeight);
                    break;

                case BossActionEffectVisualKind.HorizontalSweep:
                    BuildHorizontalSweep(
                        presentationCamera,
                        planeZ,
                        viewHeight);
                    break;

                case BossActionEffectVisualKind.ArmorCompress:
                    BuildArmorCompress(viewWidth, viewHeight);
                    break;
            }

            BossActionInfinityParticles.Play(
                transform.parent,
                transform.position,
                viewWidth,
                viewHeight,
                _layout,
                _preset);
            ApplySample(BossActionEffectSampler.Sample(_preset, 0f));
        }

        private void Update()
        {
            _elapsed += Time.unscaledDeltaTime;
            BossActionEffectSample sample =
                BossActionEffectSampler.Sample(_preset, _elapsed);
            ApplySample(sample);
            if (!sample.Active)
                Destroy(gameObject);
        }

        private void BuildHeavyPunch(float viewWidth, float viewHeight)
        {
            float radiusX = viewWidth * _layout.Radius.x;
            float radiusY = viewHeight * _layout.Radius.y;
            float lineWidth = viewHeight * _layout.LineWidth;
            AddImpactContour(
                "ScreenBreakPressureWave",
                radiusX * 0.82f,
                radiusY * 0.78f,
                lineWidth * 0.30f,
                _preset.primaryColor,
                BaseSortingOrder,
                0);
            AddImpactContour(
                "ScreenBreakCraterShadow",
                radiusX * 0.25f,
                radiusY * 0.22f,
                lineWidth * 1.08f,
                _preset.debrisColor,
                BaseSortingOrder + 1,
                2);
            AddImpactContour(
                "ScreenBreakCraterCore",
                radiusX * 0.17f,
                radiusY * 0.14f,
                lineWidth * 0.54f,
                _preset.coreColor,
                BaseSortingOrder + 2,
                3);

            for (int i = 0;
                 i < BossActionEffectSampler.ScreenBreakRayCount;
                 i++)
            {
                AddScreenBreakRay(
                    i,
                    branch: false,
                    radiusX,
                    radiusY,
                    lineWidth * (i % 3 == 0 ? 0.48f : 0.34f),
                    i % 3 == 0
                        ? _preset.coreColor
                        : _preset.primaryColor,
                    BaseSortingOrder + 4);
                AddScreenBreakRay(
                    i,
                    branch: true,
                    radiusX,
                    radiusY,
                    lineWidth * 0.21f,
                    i % 2 == 0
                        ? _preset.primaryColor
                        : _preset.debrisColor,
                    BaseSortingOrder + 3);
            }

            for (int i = 0;
                 i < BossActionEffectSampler.GlassShardCount;
                 i++)
            {
                AddGlassShard(
                    i,
                    radiusX,
                    radiusY,
                    lineWidth * 0.17f,
                    i % 3 == 0
                        ? _preset.primaryColor
                        : _preset.debrisColor,
                    BaseSortingOrder + 2);
            }
        }

        private void BuildHorizontalSweep(
            Camera camera,
            float planeZ,
            float viewHeight)
        {
            float thickness = _layout.Radius.y;
            var offsets = new[]
            {
                -thickness * 0.23f,
                0f,
                thickness * 0.27f
            };
            var widths = new[] { 0.48f, 1f, 0.32f };
            var colors = new[]
            {
                _preset.debrisColor,
                _preset.primaryColor,
                _preset.coreColor
            };
            for (int lineIndex = 0; lineIndex < offsets.Length; lineIndex++)
            {
                var points = new Vector3[
                    BossActionEffectSampler.SweepPointCount];
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 viewport =
                        BossActionEffectSampler.GetSweepViewportPoint(
                            i / (points.Length - 1f));
                    viewport.y += offsets[lineIndex];
                    points[i] = ViewportToPlane(
                        camera,
                        viewport,
                        planeZ) - transform.position;
                }
                AddStroke(
                    $"SweepPressure{lineIndex + 1}",
                    points,
                    viewHeight * _layout.LineWidth * widths[lineIndex],
                    colors[lineIndex],
                    BaseSortingOrder + lineIndex,
                    true);
            }
        }

        private void BuildArmorCompress(float viewWidth, float viewHeight)
        {
            float radiusX = viewWidth * _layout.Radius.x;
            float radiusY = viewHeight * _layout.Radius.y;
            float lineWidth = viewHeight * _layout.LineWidth;
            AddShield(
                "GuardOuterShield",
                radiusX,
                radiusY,
                lineWidth,
                _preset.primaryColor,
                BaseSortingOrder);
            AddShieldInset(
                "GuardInnerShield",
                radiusX,
                radiusY,
                0.075f,
                lineWidth * 0.5f,
                _preset.coreColor,
                BaseSortingOrder + 1);

            AddStroke(
                "GuardSpine",
                new[]
                {
                    new Vector3(0f, radiusY * 0.88f, 0f),
                    new Vector3(0f, -radiusY * 0.84f, 0f)
                },
                lineWidth * 0.52f,
                _preset.coreColor,
                BaseSortingOrder + 2,
                false);
            AddShieldScroll(
                "GuardLeftScroll",
                left: true,
                radiusX,
                radiusY,
                lineWidth * 0.58f,
                _preset.coreColor,
                BaseSortingOrder + 3,
                false);
            AddShieldScroll(
                "GuardRightScroll",
                left: false,
                radiusX,
                radiusY,
                lineWidth * 0.58f,
                _preset.coreColor,
                BaseSortingOrder + 3,
                false);

            var gem = new Vector3[5];
            for (int i = 0; i < gem.Length; i++)
            {
                Vector2 point =
                    BossActionEffectSampler.GetShieldGemUnitPoint(i);
                gem[i] = new Vector3(
                    point.x * radiusX,
                    point.y * radiusY,
                    0f);
            }
            AddStroke(
                "GuardCenterGem",
                gem,
                lineWidth * 0.72f,
                _preset.primaryColor,
                BaseSortingOrder + 4,
                false);
        }

        private void AddScreenBreakRay(
            int rayIndex,
            bool branch,
            float radiusX,
            float radiusY,
            float width,
            Color color,
            int sortingOrder)
        {
            int pointCount = branch
                ? BossActionEffectSampler.ScreenBreakBranchPointCount
                : BossActionEffectSampler.ScreenBreakRayPointCount;
            var points = new Vector3[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                Vector2 unit = branch
                    ? BossActionEffectSampler.
                        GetScreenBreakBranchUnitPoint(rayIndex, i)
                    : BossActionEffectSampler.
                        GetScreenBreakRayUnitPoint(rayIndex, i);
                points[i] = new Vector3(
                    unit.x * radiusX,
                    unit.y * radiusY,
                    0f);
            }
            AddStroke(
                branch
                    ? $"ScreenBreakBranch{rayIndex + 1}"
                    : $"ScreenBreakRay{rayIndex + 1}",
                points,
                width,
                color,
                sortingOrder,
                true);
        }

        private void AddGlassShard(
            int shardIndex,
            float radiusX,
            float radiusY,
            float width,
            Color color,
            int sortingOrder)
        {
            var points = new Vector3[
                BossActionEffectSampler.GlassShardPointCount];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 unit =
                    BossActionEffectSampler.GetGlassShardUnitPoint(
                        shardIndex, i);
                points[i] = new Vector3(
                    unit.x * radiusX,
                    unit.y * radiusY,
                    0f);
            }
            AddStroke(
                $"ScreenGlassShard{shardIndex + 1}",
                points,
                width,
                color,
                sortingOrder,
                false);
        }

        private void AddImpactContour(
            string objectName,
            float radiusX,
            float radiusY,
            float width,
            Color color,
            int sortingOrder,
            int layer)
        {
            var points = new Vector3[
                BossActionEffectSampler.ImpactPointCount];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 unit =
                    BossActionEffectSampler.GetImpactContourPoint(
                        i / (points.Length - 1f),
                        layer);
                points[i] = new Vector3(
                    unit.x * radiusX,
                    unit.y * radiusY,
                    0f);
            }
            AddStroke(
                objectName,
                points,
                width,
                color,
                sortingOrder,
                false);
        }

        private void AddShield(
            string objectName,
            float radiusX,
            float radiusY,
            float width,
            Color color,
            int sortingOrder)
        {
            var points = new Vector3[
                BossActionEffectSampler.ShieldPointCount];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 unit = BossActionEffectSampler.GetShieldUnitPoint(
                    i / (points.Length - 1f));
                points[i] = new Vector3(
                    unit.x * radiusX,
                    unit.y * radiusY,
                    0f);
            }
            AddStroke(
                objectName,
                points,
                width,
                color,
                sortingOrder,
                false);
        }

        private void AddShieldInset(
            string objectName,
            float radiusX,
            float radiusY,
            float inset,
            float width,
            Color color,
            int sortingOrder)
        {
            var points = new Vector3[
                BossActionEffectSampler.ShieldPointCount];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 unit =
                    BossActionEffectSampler.GetShieldInsetUnitPoint(
                        i / (points.Length - 1f),
                        inset);
                points[i] = new Vector3(
                    unit.x * radiusX,
                    unit.y * radiusY,
                    0f);
            }
            AddStroke(
                objectName,
                points,
                width,
                color,
                sortingOrder,
                false);
        }

        private void AddShieldScroll(
            string objectName,
            bool left,
            float radiusX,
            float radiusY,
            float width,
            Color color,
            int sortingOrder,
            bool reveals)
        {
            var points = new Vector3[
                BossActionEffectSampler.ShieldOrnamentPointCount];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 unit =
                    BossActionEffectSampler.GetShieldScrollUnitPoint(
                        left,
                        i / (points.Length - 1f));
                points[i] = new Vector3(
                    unit.x * radiusX,
                    unit.y * radiusY,
                    0f);
            }
            AddStroke(
                objectName,
                points,
                width,
                color,
                sortingOrder,
                reveals);
        }

        private void AddStroke(
            string objectName,
            Vector3[] points,
            float width,
            Color color,
            int sortingOrder,
            bool reveals)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.widthMultiplier = width;
            line.sharedMaterial =
                ProceduralParticleMaterial.GetAlphaBlended();
            line.startColor = color;
            line.endColor = color;
            line.sortingOrder = sortingOrder;
            line.positionCount = points.Length;
            line.SetPositions(points);
            _strokes.Add(new Stroke
            {
                Line = line,
                Points = points,
                Color = color,
                Reveals = reveals
            });
        }

        private void ApplySample(BossActionEffectSample sample)
        {
            float scale = Mathf.Max(0.001f, sample.Scale);
            transform.localScale = new Vector3(scale, scale, 1f);
            for (int i = 0; i < _strokes.Count; i++)
            {
                Stroke stroke = _strokes[i];
                if (stroke.Line == null)
                    continue;
                if (stroke.Reveals)
                {
                    int visible = Mathf.Clamp(
                        Mathf.CeilToInt(
                            Mathf.Lerp(2f, stroke.Points.Length,
                                sample.Reveal)),
                        2,
                        stroke.Points.Length);
                    if (stroke.Line.positionCount != visible)
                    {
                        stroke.Line.positionCount = visible;
                        for (int point = 0; point < visible; point++)
                            stroke.Line.SetPosition(point, stroke.Points[point]);
                    }
                }

                Color visibleColor = stroke.Color;
                visibleColor.a *= sample.Alpha;
                stroke.Line.startColor = visibleColor;
                stroke.Line.endColor = visibleColor;
                stroke.Line.enabled = sample.VisualVisible;
            }
        }

        private static Vector3 ViewportToPlane(
            Camera camera,
            Vector2 viewport,
            float planeZ)
        {
            float distance = Mathf.Abs(
                planeZ - camera.transform.position.z);
            Vector3 world = camera.ViewportToWorldPoint(
                new Vector3(viewport.x, viewport.y, distance));
            world.z = planeZ;
            return world;
        }

        private static float GetVisibleHeight(Camera camera, float planeZ)
        {
            if (camera.orthographic)
                return camera.orthographicSize * 2f;
            float distance = Mathf.Abs(
                planeZ - camera.transform.position.z);
            return 2f * distance * Mathf.Tan(
                camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }
    }
}
