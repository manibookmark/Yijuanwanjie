using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 卡牌世界空间自作用演出的窄分派与旧视觉族。旧族唯一拥有几何、动画与清理；
    /// VisualEffectManager 只提供一个窄播放入口，不需要知道五种图形的细节。
    /// 规模审查：文件超过 500 行是因为五种样式共享同一个瞬态层级、材质、
    /// 透明度和协程生命周期；拆开会泄漏 EffectInstance 状态。新增第六种样式
    /// 或第二套生命周期前必须重新审查并优先拆出独立视觉族。镜、支援法器与
    /// 引雷法器已各自封装，本分派只传递意图，不持有它们的几何或动画状态。
    /// 乱魂蛊仅复用已有闭合形状粒子显形族，不新增几何或第二套生命周期。
    /// </summary>
    public sealed class CardSelfEffectPlayer : MonoBehaviour
    {
        private sealed class EffectInstance
        {
            public GameObject Root;
            public Vector3 Origin;
            public CardSelfEffectStyle Style;
            public Color PrimaryColor;
            public Color SecondaryColor;
            public readonly List<Mesh> Meshes = new List<Mesh>();
            public readonly List<MeshRenderer> Renderers =
                new List<MeshRenderer>();
            public readonly List<LineRenderer> Lines =
                new List<LineRenderer>();
            public readonly List<Color> LineColors = new List<Color>();
            public readonly List<Transform> Features = new List<Transform>();
            public readonly List<Transform> Motes = new List<Transform>();
        }

        private static Material _material;
        private readonly HashSet<EffectInstance> _active = new HashSet<EffectInstance>();
        private MaterialPropertyBlock _propertyBlock;
        private MirrorSelfEffectPlayer _shapeArtifactPlayer;
        private SupportArtifactSelfEffectPlayer _supportArtifactPlayer;
        private LightningArtifactSelfEffectPlayer _lightningArtifactPlayer;
        private GlyphSupportEffectPlayer _glyphSupportPlayer;

        private void Awake()
        {
            // MaterialPropertyBlock owns a native object. Tuanjie rejects its
            // construction from a MonoBehaviour field initializer, leaving the
            // field null and making the first opacity update fail. Awake is the
            // earliest supported lifecycle boundary for this allocation.
            EnsurePropertyBlock();
        }

        /// <summary>Requests one support artifact; its owner returns the actual
        /// world origin once the shape contracts into the HUD gain glyph.
        /// False means no manifestation was started and the HUD can act directly.</summary>
        public bool PlayArtifact(Vector3 worldPosition, VfxProfile profile,
            int manaGained, Action<Vector3> onManifested)
        {
            if (!isActiveAndEnabled || profile == null) return false;
            switch (profile.selfEffectStyle)
            {
                case CardSelfEffectStyle.TianjiMirror:
                case CardSelfEffectStyle.MuddleSigil:
                    if (_shapeArtifactPlayer == null)
                        _shapeArtifactPlayer = gameObject.AddComponent<MirrorSelfEffectPlayer>();
                    return _shapeArtifactPlayer.PlayArtifact(worldPosition, profile, onManifested);
                case CardSelfEffectStyle.QuantityMechanism:
                case CardSelfEffectStyle.WindOrb:
                    if (_supportArtifactPlayer == null)
                        _supportArtifactPlayer = gameObject.AddComponent<SupportArtifactSelfEffectPlayer>();
                    return _supportArtifactPlayer.PlayArtifact(worldPosition, profile, onManifested);
                case CardSelfEffectStyle.ManaBook:
                case CardSelfEffectStyle.DealScript:
                case CardSelfEffectStyle.SandSpirit:
                    if (_glyphSupportPlayer == null)
                        _glyphSupportPlayer = gameObject.AddComponent<GlyphSupportEffectPlayer>();
                    return _glyphSupportPlayer.PlayArtifact(worldPosition, profile, manaGained, onManifested);
                default:
                    return false;
            }
        }

        public void Play(
            Vector3 worldPosition,
            VfxProfile profile,
            string gainLabel = null,
            IReadOnlyList<Vector3> destinations = null,
            int manaGained = 0)
        {
            if (!isActiveAndEnabled || profile == null ||
                profile.selfEffectStyle == CardSelfEffectStyle.None)
            {
                return;
            }

            // Legacy callers retain their signature while all support subjects
            // use the one manifestation path, without world text or transfers.
            if (profile.selfEffectStyle == CardSelfEffectStyle.TianjiMirror ||
                profile.selfEffectStyle == CardSelfEffectStyle.MuddleSigil ||
                profile.selfEffectStyle == CardSelfEffectStyle.QuantityMechanism ||
                profile.selfEffectStyle == CardSelfEffectStyle.WindOrb ||
                profile.selfEffectStyle == CardSelfEffectStyle.ManaBook ||
                profile.selfEffectStyle == CardSelfEffectStyle.DealScript ||
                profile.selfEffectStyle == CardSelfEffectStyle.SandSpirit)
            {
                PlayArtifact(worldPosition, profile, manaGained, null);
                return;
            }

            if (profile.selfEffectStyle == CardSelfEffectStyle.LightningRing ||
                profile.selfEffectStyle == CardSelfEffectStyle.ThunderWoodSword)
            {
                if (_lightningArtifactPlayer == null)
                    _lightningArtifactPlayer = gameObject.AddComponent<LightningArtifactSelfEffectPlayer>();
                _lightningArtifactPlayer.Play(worldPosition, profile);
                return;
            }

            EnsurePropertyBlock();

            float scale = Mathf.Clamp(profile.selfEffectScale, 0.4f, 2.5f);
            if (profile.selfEffectStyle == CardSelfEffectStyle.JadeSpiral)
                // Bag silhouette and its mouth, excluding orbiting jade shards.
                scale = CardCastPlacement.SupportScale(worldPosition, profile, new Vector2(0.8f, 0.8f));
            else if (profile.selfEffectStyle == CardSelfEffectStyle.FortuneBloom)
                // Four petals define the body; the halo and pollen are ornaments.
                scale = CardCastPlacement.SupportScale(worldPosition, profile, new Vector2(0.92f, 0.92f));
            worldPosition = CardCastPlacement.AboveCard(worldPosition, profile, scale);
            EffectInstance instance = Build(
                worldPosition,
                profile);
            StartCoroutine(Animate(
                instance,
                Mathf.Clamp(profile.selfEffectDuration, 0.25f, 2f),
                scale));
        }

        private EffectInstance Build(
            Vector3 worldPosition,
            VfxProfile profile)
        {
            var instance = new EffectInstance
            {
                Root = new GameObject(
                    $"CardSelfEffect_{profile.selfEffectStyle}"),
                Origin = worldPosition,
                Style = profile.selfEffectStyle,
                PrimaryColor = profile.selfEffectPrimaryColor,
                SecondaryColor = profile.selfEffectSecondaryColor
            };
            instance.Root.transform.SetParent(transform, false);
            instance.Root.transform.position = worldPosition;
            _active.Add(instance);

            try
            {
                int motes = Mathf.Clamp(profile.selfEffectParticleCount, 0, 32);
                switch (profile.selfEffectStyle)
                {
                    case CardSelfEffectStyle.ArmorWard:
                        BuildArmorWard(instance, motes);
                        break;
                    case CardSelfEffectStyle.VitalityPill:
                        BuildVitalityPill(instance, motes);
                        break;
                    case CardSelfEffectStyle.ManaScript:
                        BuildManaScript(instance, motes);
                        break;
                    case CardSelfEffectStyle.JadeSpiral:
                        BuildJadeSpiral(instance, motes);
                        break;
                    case CardSelfEffectStyle.FortuneBloom:
                        BuildFortuneBloom(instance, motes);
                        break;
                }
                return instance;
            }
            catch
            {
                Cleanup(instance);
                throw;
            }
        }

        private IEnumerator Animate(
            EffectInstance instance,
            float duration,
            float configuredScale)
        {
            try
            {
                float elapsed = 0f;
                while (elapsed < duration && instance.Root != null && _active.Contains(instance))
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float appear = Smooth01(Mathf.Clamp01(t / 0.18f));
                    float fade = 1f - Smooth01(Mathf.Clamp01(
                        (t - 0.72f) / 0.28f));
                    float envelope = appear * fade;
                    float pulse = 1f + Mathf.Sin(t * Mathf.PI * 5f) * 0.045f;
                    instance.Root.transform.localScale = Vector3.one *
                        configuredScale * envelope * pulse;

                    AnimateStyle(instance, t);
                    SetOpacity(instance, envelope);
                    yield return null;
                }
            }
            finally
            {
                Cleanup(instance);
            }
        }

        private static void AnimateStyle(EffectInstance instance, float t)
        {
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            switch (instance.Style)
            {
                case CardSelfEffectStyle.ArmorWard:
                    instance.Root.transform.rotation = Quaternion.Euler(
                        0f, 0f, Mathf.Lerp(-4f, 0f, ease));
                    AnimateArmorWard(instance.Features, ease);
                    AnimateMotes(instance.Motes, t, 0.52f, 0.34f);
                    break;

                case CardSelfEffectStyle.VitalityPill:
                    instance.Root.transform.position = instance.Origin +
                        Vector3.up * Mathf.Lerp(-0.12f, 0.46f, ease);
                    if (instance.Features.Count > 0)
                    {
                        instance.Features[0].localRotation =
                            Quaternion.Euler(
                                0f,
                                0f,
                                Mathf.Sin(t * Mathf.PI * 3f) * 8f);
                    }
                    AnimateMotes(instance.Motes, t, 0.34f, 0.72f);
                    break;

                case CardSelfEffectStyle.ManaScript:
                    instance.Root.transform.position = instance.Origin +
                        Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.16f;
                    instance.Root.transform.rotation = Quaternion.Euler(
                        0f, 0f, Mathf.Sin(t * Mathf.PI * 2f) * 3f);
                    AnimateOrbiters(
                        instance.Motes,
                        0.58f + Mathf.Sin(t * Mathf.PI) * 0.12f,
                        120f * t,
                        spin: 180f * t);
                    break;

                case CardSelfEffectStyle.JadeSpiral:
                    AnimateOrbiters(
                        instance.Motes,
                        Mathf.Lerp(1.15f, 0.08f, ease),
                        320f * t,
                        spin: 540f * t);
                    if (instance.Features.Count > 0)
                    {
                        instance.Features[0].localScale = Vector3.one *
                            (0.86f + Mathf.Sin(t * Mathf.PI * 4f) * 0.08f);
                    }
                    break;

                case CardSelfEffectStyle.FortuneBloom:
                    instance.Root.transform.rotation = Quaternion.Euler(
                        0f, 0f, Mathf.Lerp(-10f, 16f, ease));
                    float bloom = 0.8f + Mathf.Sin(t * Mathf.PI) * 0.28f;
                    for (int i = 0; i < instance.Features.Count; i++)
                        instance.Features[i].localScale = Vector3.one * bloom;
                    AnimateOrbiters(
                        instance.Motes,
                        Mathf.Lerp(0.2f, 1f, ease),
                        55f * t,
                        spin: 130f * t);
                    break;
            }
        }

        private static void BuildArmorWard(EffectInstance instance, int count)
        {
            int plates = Mathf.Clamp(count / 2, 6, 10);
            for (int i = 0; i < plates; i++)
            {
                Transform plate = AddPart(
                    instance,
                    $"ArmorPlate_{i}",
                    ShieldVertices(),
                    instance.PrimaryColor,
                    34);
                instance.Features.Add(plate);
            }
            AddMotes(instance, Mathf.Clamp(count / 3, 3, 7),
                instance.SecondaryColor, 0.035f, "ArmorSpark");
        }

        private static void AnimateArmorWard(
            IReadOnlyList<Transform> plates,
            float ease)
        {
            if (plates == null || plates.Count == 0) return;
            float radius = Mathf.Lerp(1.02f, 0.58f, ease);
            for (int i = 0; i < plates.Count; i++)
            {
                Transform plate = plates[i];
                if (plate == null) continue;
                float ratio = plates.Count == 1
                    ? 0.5f
                    : (float)i / (plates.Count - 1);
                float angle = Mathf.Lerp(158f, 22f, ratio);
                float radians = angle * Mathf.Deg2Rad;
                plate.localPosition = new Vector3(
                    Mathf.Cos(radians) * radius,
                    Mathf.Sin(radians) * radius * 0.72f - 0.06f,
                    0f);
                plate.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    angle - 90f);
            }
        }

        private static void BuildVitalityPill(
            EffectInstance instance,
            int count)
        {
            AddRing(instance, "VitalityPulse", instance.SecondaryColor,
                0.54f, 0.035f, 28, 31, serrated: false);
            Transform pill = AddPart(
                instance,
                "GubenPill",
                EllipseVertices(0.2f, 0.38f, 24),
                instance.PrimaryColor,
                34);
            instance.Features.Add(pill);
            Transform band = AddPart(
                instance,
                "PillSealBand",
                QuadVertices(-0.21f, -0.055f, 0.21f, 0.055f),
                instance.SecondaryColor,
                35);
            band.SetParent(pill, false);
            AddMotes(instance, Mathf.Clamp(count, 5, 14),
                instance.SecondaryColor, 0.04f, "VitalityMote");
        }

        private static void BuildManaScript(EffectInstance instance, int count)
        {
            AddRing(instance, "ManaRuneRing", instance.PrimaryColor,
                0.62f, 0.025f, 20, 31, serrated: true);
            Transform left = AddPart(
                instance,
                "ScriptLeftPage",
                new[]
                {
                    new Vector2(-0.5f, -0.28f),
                    new Vector2(-0.04f, -0.17f),
                    new Vector2(-0.04f, 0.3f),
                    new Vector2(-0.46f, 0.2f)
                },
                instance.SecondaryColor,
                33);
            Transform right = AddPart(
                instance,
                "ScriptRightPage",
                new[]
                {
                    new Vector2(0.04f, -0.17f),
                    new Vector2(0.5f, -0.28f),
                    new Vector2(0.46f, 0.2f),
                    new Vector2(0.04f, 0.3f)
                },
                instance.SecondaryColor,
                33);
            instance.Features.Add(left);
            instance.Features.Add(right);
            AddLine(instance, "ScriptInkLeft", instance.PrimaryColor,
                new[]
                {
                    new Vector3(-0.39f, 0.08f, 0f),
                    new Vector3(-0.14f, 0.13f, 0f),
                    new Vector3(-0.36f, -0.04f, 0f),
                    new Vector3(-0.13f, 0.01f, 0f)
                },
                0.018f,
                35,
                loop: false);
            AddLine(instance, "ScriptInkRight", instance.PrimaryColor,
                new[]
                {
                    new Vector3(0.13f, 0.13f, 0f),
                    new Vector3(0.39f, 0.08f, 0f),
                    new Vector3(0.13f, 0.01f, 0f),
                    new Vector3(0.36f, -0.04f, 0f)
                },
                0.018f,
                35,
                loop: false);
            AddMotes(instance, Mathf.Clamp(count, 4, 12),
                instance.PrimaryColor, 0.042f, "ManaRune");
        }

        private static void BuildJadeSpiral(EffectInstance instance, int count)
        {
            AddRing(instance, "JadeBagMouth", instance.SecondaryColor,
                0.38f, 0.04f, 24, 32, serrated: false);
            Transform bag = AddPart(
                instance,
                "JadeBagSilhouette",
                new[]
                {
                    new Vector2(-0.28f, -0.36f),
                    new Vector2(0.28f, -0.36f),
                    new Vector2(0.38f, -0.05f),
                    new Vector2(0.22f, 0.26f),
                    new Vector2(-0.22f, 0.26f),
                    new Vector2(-0.38f, -0.05f)
                },
                new Color(
                    instance.PrimaryColor.r * 0.55f,
                    instance.PrimaryColor.g * 0.55f,
                    instance.PrimaryColor.b * 0.55f,
                    instance.PrimaryColor.a),
                31);
            instance.Features.Add(bag);
            AddMotes(instance, Mathf.Clamp(count, 8, 20),
                instance.PrimaryColor, 0.075f, "JadeShard",
                diamond: true);
        }

        private static void BuildFortuneBloom(
            EffectInstance instance,
            int count)
        {
            AddRing(instance, "FortuneHalo", instance.SecondaryColor,
                0.62f, 0.026f, 28, 31, serrated: true);
            Vector2[] leaf =
            {
                new Vector2(0f, 0f),
                new Vector2(0.12f, 0.18f),
                new Vector2(0.34f, 0.2f),
                new Vector2(0.46f, 0f),
                new Vector2(0.34f, -0.2f),
                new Vector2(0.12f, -0.18f)
            };
            for (int i = 0; i < 4; i++)
            {
                Transform petal = AddPart(
                    instance,
                    $"FortuneLeaf_{i}",
                    leaf,
                    instance.PrimaryColor,
                    34);
                petal.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
                instance.Features.Add(petal);
            }
            AddPart(
                instance,
                "FortuneCore",
                EllipseVertices(0.105f, 0.105f, 18),
                instance.SecondaryColor,
                35);
            AddMotes(instance, Mathf.Clamp(count, 6, 18),
                instance.SecondaryColor, 0.035f, "FortunePollen");
        }

        private static void AddMotes(
            EffectInstance instance,
            int count,
            Color color,
            float radius,
            string prefix,
            bool diamond = false)
        {
            for (int i = 0; i < count; i++)
            {
                Vector2[] vertices = diamond
                    ? new[]
                    {
                        new Vector2(-radius, 0f),
                        new Vector2(0f, radius * 1.45f),
                        new Vector2(radius, 0f),
                        new Vector2(0f, -radius * 1.45f)
                    }
                    : EllipseVertices(radius, radius, 10);
                Transform mote = AddPart(
                    instance,
                    $"{prefix}_{i}",
                    vertices,
                    color,
                    36);
                instance.Motes.Add(mote);
            }
        }

        private static Transform AddPart(
            EffectInstance instance,
            string name,
            Vector2[] vertices,
            Color color,
            int sortingOrder)
        {
            var child = new GameObject(
                name,
                typeof(MeshFilter),
                typeof(MeshRenderer));
            child.transform.SetParent(instance.Root.transform, false);
            Mesh mesh = CreateConvexMesh(name, vertices, color);
            child.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetMaterial();
            renderer.sortingOrder = sortingOrder;
            instance.Meshes.Add(mesh);
            instance.Renderers.Add(renderer);
            return child.transform;
        }

        private static void AddRing(
            EffectInstance instance,
            string name,
            Color color,
            float radius,
            float width,
            int segments,
            int sortingOrder,
            bool serrated)
        {
            var positions = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                float scale = serrated && i % 2 != 0 ? 0.9f : 1f;
                positions[i] = new Vector3(
                    Mathf.Cos(angle) * radius * scale,
                    Mathf.Sin(angle) * radius * scale,
                    0f);
            }
            AddLine(instance, name, color, positions, width,
                sortingOrder, loop: true);
        }

        private static void AddLine(
            EffectInstance instance,
            string name,
            Color color,
            Vector3[] positions,
            float width,
            int sortingOrder,
            bool loop)
        {
            var child = new GameObject(name, typeof(LineRenderer));
            child.transform.SetParent(instance.Root.transform, false);
            LineRenderer line = child.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = loop;
            line.positionCount = positions.Length;
            line.SetPositions(positions);
            line.widthMultiplier = width;
            line.startColor = color;
            line.endColor = color;
            line.sharedMaterial = GetMaterial();
            line.sortingOrder = sortingOrder;
            instance.Lines.Add(line);
            instance.LineColors.Add(color);
        }

        private static void AnimateOrbiters(
            IReadOnlyList<Transform> transforms,
            float radius,
            float angularOffset,
            float spin)
        {
            if (transforms == null || transforms.Count == 0) return;
            for (int i = 0; i < transforms.Count; i++)
            {
                Transform item = transforms[i];
                if (item == null) continue;
                float angle = angularOffset + 360f * i / transforms.Count;
                float radians = angle * Mathf.Deg2Rad;
                item.localPosition = new Vector3(
                    Mathf.Cos(radians) * radius,
                    Mathf.Sin(radians) * radius,
                    0f);
                item.localRotation = Quaternion.Euler(0f, 0f, angle + spin);
            }
        }

        private static void AnimateMotes(
            IReadOnlyList<Transform> motes,
            float t,
            float radius,
            float height)
        {
            if (motes == null || motes.Count == 0) return;
            for (int i = 0; i < motes.Count; i++)
            {
                float phase = (float)i / motes.Count;
                float x = Mathf.Sin((t * 2.2f + phase) * Mathf.PI * 2f) *
                    radius * (0.4f + phase * 0.6f);
                float y = Mathf.Repeat(t * 1.4f + phase, 1f) * height -
                    height * 0.35f;
                motes[i].localPosition = new Vector3(x, y, 0f);
                motes[i].localRotation = Quaternion.Euler(
                    0f, 0f, (t * 240f + i * 31f));
            }
        }

        private void SetOpacity(EffectInstance instance, float opacity)
        {
            MaterialPropertyBlock propertyBlock = EnsurePropertyBlock();
            propertyBlock.Clear();
            propertyBlock.SetColor(
                "_Color",
                new Color(1f, 1f, 1f, Mathf.Clamp01(opacity)));
            for (int i = 0; i < instance.Renderers.Count; i++)
            {
                MeshRenderer renderer = instance.Renderers[i];
                if (renderer != null)
                    renderer.SetPropertyBlock(propertyBlock);
            }
            for (int i = 0; i < instance.Lines.Count; i++)
            {
                LineRenderer line = instance.Lines[i];
                if (line == null) continue;
                Color baseColor = instance.LineColors[i];
                baseColor.a *= Mathf.Clamp01(opacity);
                line.startColor = baseColor;
                line.endColor = baseColor;
            }
        }

        private MaterialPropertyBlock EnsurePropertyBlock()
        {
            if (_propertyBlock == null)
                _propertyBlock = new MaterialPropertyBlock();
            return _propertyBlock;
        }

        private static Mesh CreateConvexMesh(
            string name,
            Vector2[] source,
            Color color)
        {
            int triangleCount = Mathf.Max(0, source.Length - 2);
            var triangles = new int[triangleCount * 3];
            for (int i = 0; i < triangleCount; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
            var vertices = new Vector3[source.Length];
            var colors = new Color[source.Length];
            var uv = new Vector2[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                vertices[i] = source[i];
                colors[i] = color;
                uv[i] = source[i] + Vector2.one * 0.5f;
            }
            var mesh = new Mesh
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                colors = colors,
                uv = uv,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector2[] ShieldVertices()
        {
            return new[]
            {
                new Vector2(-0.12f, 0.2f),
                new Vector2(0.12f, 0.2f),
                new Vector2(0.16f, 0.04f),
                new Vector2(0f, -0.24f),
                new Vector2(-0.16f, 0.04f)
            };
        }

        private static Vector2[] QuadVertices(
            float minX,
            float minY,
            float maxX,
            float maxY)
        {
            return new[]
            {
                new Vector2(minX, minY),
                new Vector2(minX, maxY),
                new Vector2(maxX, maxY),
                new Vector2(maxX, minY)
            };
        }

        private static Vector2[] EllipseVertices(
            float radiusX,
            float radiusY,
            int segments)
        {
            var vertices = new Vector2[Mathf.Max(3, segments)];
            for (int i = 0; i < vertices.Length; i++)
            {
                float angle = Mathf.PI * 2f * i / vertices.Length;
                vertices[i] = new Vector2(
                    Mathf.Cos(angle) * radiusX,
                    Mathf.Sin(angle) * radiusY);
            }
            return vertices;
        }

        private static Material GetMaterial()
        {
            if (_material != null) return _material;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return null;
            _material = new Material(shader)
            {
                name = "CardSelfEffectMaterial",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = Texture2D.whiteTexture
            };
            return _material;
        }

        private static float Smooth01(float value)
        {
            return value * value * (3f - 2f * value);
        }

        private void Cleanup(EffectInstance instance)
        {
            if (instance == null || !_active.Remove(instance)) return;
            for (int i = 0; i < instance.Meshes.Count; i++)
            {
                Mesh mesh = instance.Meshes[i];
                if (mesh == null) continue;
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
            instance.Meshes.Clear();
            if (instance.Root == null) return;
            instance.Root.SetActive(false);
            if (Application.isPlaying) Destroy(instance.Root);
            else DestroyImmediate(instance.Root);
            instance.Root = null;
        }

        private void CancelAll()
        {
            StopAllCoroutines();
            foreach (EffectInstance instance in new List<EffectInstance>(_active))
                Cleanup(instance);
        }

        private void OnDisable() => CancelAll();
        private void OnDestroy() => CancelAll();
    }
}
