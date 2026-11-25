using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 单个敌人的表现聚合。统一管理主体渲染与伤害预览的生命周期，
    /// 但不负责阵型规则、战斗时序或 Tooltip。
    /// 本类继续唯一拥有脚底阴影的几何、排序与可见性；校准只替换创建时几何。
    /// </summary>
    internal sealed class EnemyActorView
    {
        private static readonly Vector2[] DamageOutlineOffsets =
        {
            new Vector2(-0.014f, 0f),
            new Vector2(0.014f, 0f),
            new Vector2(0f, -0.014f),
            new Vector2(0f, 0.014f),
            new Vector2(-0.01f, -0.01f),
            new Vector2(-0.01f, 0.01f),
            new Vector2(0.01f, -0.01f),
            new Vector2(0.01f, 0.01f)
        };

        private static readonly Color DamageOutlineColor =
            new Color(0.08f, 0.045f, 0.025f, 0.96f);
        private static Sprite _footShadowSprite;

        private readonly Transform _formationRoot;
        private readonly Transform _entranceMotionRoot;
        private readonly TextMesh _damagePreviewLabel;
        private readonly TextMesh[] _damagePreviewOutlines;
        private readonly SpriteRenderer _footShadow;
        private readonly ParticleSystem _entryParticles;
        private readonly ParticleSystemRenderer _entryParticleRenderer;

        private EnemyActorView(
            Transform formationRoot,
            Transform entranceMotionRoot,
            SpriteRenderer renderer,
            EnemySpriteAlignment visual,
            TextMesh damagePreviewLabel,
            TextMesh[] damagePreviewOutlines,
            SpriteRenderer footShadow,
            ParticleSystem entryParticles)
        {
            _formationRoot = formationRoot;
            _entranceMotionRoot = entranceMotionRoot;
            Renderer = renderer;
            Visual = visual;
            _damagePreviewLabel = damagePreviewLabel;
            _damagePreviewOutlines = damagePreviewOutlines;
            _footShadow = footShadow;
            _entryParticles = entryParticles;
            _entryParticleRenderer = entryParticles != null
                ? entryParticles.GetComponent<ParticleSystemRenderer>()
                : null;
        }

        public SpriteRenderer Renderer { get; }
        public EnemySpriteAlignment Visual { get; }
        public bool IsPresentationVisible => Renderer != null &&
            Renderer.enabled && Renderer.gameObject.activeInHierarchy &&
            Renderer.color.a > 0f;
        /// <summary>编队 Presenter 唯一允许写入的定位与整体缩放根。</summary>
        public Transform FormationTransform => _formationRoot;
        /// <summary>BOSS 入场特效唯一允许写入的瞬时运动根。</summary>
        public Transform EntranceMotionTransform => _entranceMotionRoot;
        /// <summary>命中、死亡、序列帧等角色自身表现的目标节点。</summary>
        public Transform EffectTargetTransform =>
            Renderer != null ? Renderer.transform : null;

        /// <summary>
        /// 返回实际阴影的世界落点，不暴露可修改节点。无阴影时使用素材落地点，
        /// 无素材的占位角色则使用编队落点；命中与伤害数字仍使用本体中心。
        /// </summary>
        public bool TryGetGroundAnchor(out Vector3 worldAnchor)
        {
            worldAnchor = default;
            if (_footShadow != null)
            {
                worldAnchor = _footShadow.transform.position;
                return true;
            }
            if (Renderer != null && Renderer.sprite != null)
            {
                Bounds localBounds = Renderer.sprite.bounds;
                Vector2 anchor = Visual != null
                    ? Visual.groundAnchor : new Vector2(0.5f, 0f);
                Vector3 localPoint = new Vector3(
                    localBounds.min.x + localBounds.size.x * anchor.x,
                    localBounds.min.y + localBounds.size.y * anchor.y,
                    0f);
                worldAnchor = Renderer.transform.TransformPoint(localPoint);
                return true;
            }
            if (_formationRoot == null) return false;
            worldAnchor = _formationRoot.position;
            return true;
        }

        public static EnemyActorView Create(
            Transform parent,
            int index,
            Sprite sprite,
            EnemySpriteAlignment visual)
        {
            var formationObject = new GameObject(
                $"Enemy_{index + 1}_FormationRoot");
            formationObject.transform.SetParent(parent, false);

            var entranceMotionObject = new GameObject(
                $"Enemy_{index + 1}_EntranceMotionRoot");
            entranceMotionObject.transform.SetParent(
                formationObject.transform,
                false);

            var enemyObject = new GameObject(
                $"Enemy_{index + 1}",
                typeof(SpriteRenderer));
            enemyObject.transform.SetParent(
                entranceMotionObject.transform,
                false);
            SpriteRenderer renderer =
                enemyObject.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            SpriteRenderer footShadow = CreateFootShadow(
                entranceMotionObject.transform, sprite, renderer, visual);
            ParticleSystem entryParticles = CreateEntryParticles(
                entranceMotionObject.transform, renderer);
            TextMesh damagePreviewLabel = CreateDamagePreviewLabel(
                parent,
                index,
                out TextMesh[] damagePreviewOutlines);
            damagePreviewLabel.gameObject.SetActive(false);

            var actor = new EnemyActorView(
                formationObject.transform,
                entranceMotionObject.transform,
                renderer,
                visual,
                damagePreviewLabel,
                damagePreviewOutlines,
                footShadow,
                entryParticles);
            // Formation presentation grants visibility only after placing the
            // actor; sibling shadows/particles must not leak from staging rows.
            actor.SetPresentationVisibility(false);
            return actor;
        }

        public void ShowDamagePreview(EnemyDamagePreview preview)
        {
            if (Renderer == null || _damagePreviewLabel == null ||
                (preview.AppliedDamage <= 0 && preview.OverkillDamage <= 0 && !preview.IsErased) ||
                !IsPresentationVisible)
            {
                return;
            }

            RefreshDamagePreviewPosition();
            // Unknown draws and compound effects provide an estimate, not a
            // promised hit or kill. The view only consumes the Core marker.
            bool certainKill = preview.WillKill && !preview.IsEstimate;
            string estimate = preview.IsEstimate ? "≈" : string.Empty;
            string damageText = preview.AppliedDamage > 0
                ? estimate + preview.AppliedDamage : string.Empty;
            if (preview.IsErased)
                damageText += (damageText.Length > 0 ? "\n" : string.Empty) + estimate + "击败";
            if (preview.OverkillDamage > 0)
                damageText += (damageText.Length > 0 ? "\n" : string.Empty) +
                    "余威 " + estimate + preview.OverkillDamage;
            _damagePreviewLabel.text = damageText;
            _damagePreviewLabel.color = certainKill
                ? new Color(0.9f, 0.12f, 0.08f, 1f)
                : new Color(1f, 0.82f, 0.28f, 1f);
            _damagePreviewLabel.fontSize = certainKill ? 56 : 48;
            _damagePreviewLabel.characterSize = certainKill ? 0.14f : 0.12f;

            MeshRenderer textRenderer =
                _damagePreviewLabel.GetComponent<MeshRenderer>();
            if (textRenderer != null)
                textRenderer.sortingOrder = Renderer.sortingOrder + 40;

            for (int i = 0; i < _damagePreviewOutlines.Length; i++)
            {
                TextMesh outline = _damagePreviewOutlines[i];
                if (outline == null) continue;

                outline.text = _damagePreviewLabel.text;
                outline.fontSize = _damagePreviewLabel.fontSize;
                outline.characterSize = _damagePreviewLabel.characterSize;
                MeshRenderer outlineRenderer =
                    outline.GetComponent<MeshRenderer>();
                if (outlineRenderer != null)
                    outlineRenderer.sortingOrder = Renderer.sortingOrder + 39;
            }

            _damagePreviewLabel.gameObject.SetActive(true);
        }

        public void HideDamagePreview()
        {
            if (_damagePreviewLabel == null) return;
            _damagePreviewLabel.text = string.Empty;
            _damagePreviewLabel.gameObject.SetActive(false);
        }

        public void PlayEntryParticles()
        {
            if (_entryParticles == null || !IsPresentationVisible) return;
            if (_entryParticleRenderer != null)
                _entryParticleRenderer.enabled = true;
            _entryParticles.Clear(true);
            _entryParticles.Emit(14);
        }

        /// <summary>
        /// 纸人入场的轻量表现：从窄纸片翻回正面，同时略微下落回脚底。
        /// 只改变独立运动根，不触碰编队定位根和战斗状态。
        /// </summary>
        public void SetPaperEntrancePose(float progress, float twistSign)
        {
            if (_entranceMotionRoot == null) return;
            float t = Mathf.Clamp01(progress);
            float eased = t * t * (3f - 2f * t);
            _entranceMotionRoot.localPosition = new Vector3(
                0f, Mathf.Lerp(-0.08f, 0f, eased), 0f);
            _entranceMotionRoot.localRotation = Quaternion.Euler(
                0f, 0f, Mathf.Lerp(2.8f * twistSign, 0f, eased));
            _entranceMotionRoot.localScale = new Vector3(
                Mathf.Lerp(0.12f, 1f, eased),
                Mathf.Lerp(0.92f, 1f, eased),
                1f);
        }

        public void ResetPaperEntrancePose()
        {
            if (_entranceMotionRoot == null) return;
            _entranceMotionRoot.localPosition = Vector3.zero;
            _entranceMotionRoot.localRotation = Quaternion.identity;
            _entranceMotionRoot.localScale = Vector3.one;
        }

        public void RefreshDamagePreviewPosition()
        {
            if (Renderer == null || _damagePreviewLabel == null) return;

            Bounds bounds = Renderer.bounds;
            _damagePreviewLabel.transform.position = new Vector3(
                bounds.center.x,
                bounds.center.y,
                Renderer.transform.position.z);
        }

        public void SetSortingOrder(int sortingOrder)
        {
            if (Renderer != null)
                Renderer.sortingOrder = sortingOrder;
            if (_footShadow != null)
                _footShadow.sortingOrder = sortingOrder - 1;
        }

        /// <summary>
        /// Applies the formation owner's visibility permission to the entire
        /// actor, including sibling renderers. A prepared entrance may keep
        /// the body active at zero alpha without revealing any companion.
        /// </summary>
        public void SetPresentationVisibility(bool visible, float alpha = 1f)
        {
            float presentationAlpha = visible ? Mathf.Clamp01(alpha) : 0f;
            if (Renderer != null)
            {
                Color color = Renderer.color;
                Renderer.color = new Color(
                    color.r, color.g, color.b, presentationAlpha);
                Renderer.enabled = visible;
                Renderer.gameObject.SetActive(visible);
            }

            bool showCompanions = visible && presentationAlpha > 0f;
            if (_footShadow != null)
            {
                Color shadowColor = _footShadow.color;
                shadowColor.a = 0.48f * presentationAlpha;
                _footShadow.color = shadowColor;
                _footShadow.enabled = showCompanions;
            }
            if (_entryParticleRenderer != null)
                _entryParticleRenderer.enabled = showCompanions;
            if (!showCompanions)
            {
                if (_entryParticles != null)
                {
                    _entryParticles.Stop(
                        true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                HideDamagePreview();
            }
        }

        public void HideForState()
        {
            SetPresentationVisibility(false);
        }

        public void Release()
        {
            if (_damagePreviewLabel != null)
                Object.Destroy(_damagePreviewLabel.gameObject);
            if (_formationRoot != null)
                Object.Destroy(_formationRoot.gameObject);
        }

        private static SpriteRenderer CreateFootShadow(
            Transform parent,
            Sprite sprite,
            SpriteRenderer target,
            EnemySpriteAlignment visual)
        {
            if (parent == null || sprite == null || target == null)
                return null;

            GameObject shadowObject = new GameObject(
                "EnemyFootShadow", typeof(SpriteRenderer));
            shadowObject.transform.SetParent(parent, false);
            SpriteRenderer shadow = shadowObject.GetComponent<SpriteRenderer>();
            shadow.sprite = CreateSoftShadowSprite();
            shadow.color = new Color(0.015f, 0.012f, 0.02f, 0.48f);
            shadow.sortingLayerID = target.sortingLayerID;
            shadow.sortingOrder = target.sortingOrder - 1;

            Vector2 shadowSize = shadow.sprite != null
                ? (Vector2)shadow.sprite.bounds.size
                : Vector2.one;
            if (visual != null &&
                visual.TryGetContactShadowRect(sprite, out Rect calibratedRect))
            {
                shadow.transform.localPosition = new Vector3(
                    calibratedRect.center.x, calibratedRect.center.y, 0.01f);
                shadow.transform.localScale = new Vector3(
                    calibratedRect.width / Mathf.Max(0.001f, shadowSize.x),
                    calibratedRect.height / Mathf.Max(0.001f, shadowSize.y),
                    1f);
                return shadow;
            }

            Bounds bounds = sprite.bounds;
            float visibleBottom = bounds.min.y;
            Vector2[] vertices = sprite.vertices;
            if (vertices != null && vertices.Length > 0)
            {
                visibleBottom = vertices[0].y;
                for (int i = 1; i < vertices.Length; i++)
                    visibleBottom = Mathf.Min(visibleBottom, vertices[i].y);
            }
            shadow.transform.localPosition = new Vector3(
                bounds.center.x,
                visibleBottom + Mathf.Max(0.005f, bounds.size.y * 0.012f),
                0.01f);
            shadow.transform.localScale = new Vector3(
                Mathf.Max(0.01f, bounds.size.x * 0.52f / Mathf.Max(0.01f, shadowSize.x)),
                Mathf.Max(0.01f, bounds.size.y * 0.075f / Mathf.Max(0.01f, shadowSize.y)),
                1f);
            return shadow;
        }

        private static Sprite CreateSoftShadowSprite()
        {
            if (_footShadowSprite != null)
                return _footShadowSprite;
            Texture2D texture = new Texture2D(32, 8, TextureFormat.RGBA32, false);
            texture.name = "EnemyFootShadowTexture";
            for (int y = 0; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
            {
                float nx = (x + 0.5f) / texture.width * 2f - 1f;
                float ny = (y + 0.5f) / texture.height * 2f - 1f;
                float edge = Mathf.Clamp01(1f - (nx * nx + ny * ny));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, edge * edge));
            }
            texture.Apply(false, true);
            _footShadowSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), texture.width);
            return _footShadowSprite;
        }

        private static ParticleSystem CreateEntryParticles(
            Transform parent,
            SpriteRenderer target)
        {
            var go = new GameObject("EnemyEntryParticles",
                typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            ParticleSystem system = go.GetComponent<ParticleSystem>();
            // Unity adds the renderer and may start this active system at creation.
            // playOnAwake only controls later activation; stop before setting duration.
            system.Stop(
                true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.42f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.18f, 0.52f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.055f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.72f, 0.94f, 0.88f, 0.82f),
                new Color(0.96f, 0.80f, 0.42f, 0.88f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.12f;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sortingLayerID = target != null ? target.sortingLayerID : 0;
            renderer.sortingOrder = target != null ? target.sortingOrder + 2 : 2;
            Shader shader = Shader.Find("Particles/Standard Unlit");
            if (shader != null)
                renderer.material = new Material(shader);
            return system;
        }

        private static TextMesh CreateDamagePreviewLabel(
            Transform parent,
            int index,
            out TextMesh[] outlines)
        {
            var labelObject = new GameObject(
                $"EnemyDamagePreview_{index + 1}",
                typeof(TextMesh));
            labelObject.transform.SetParent(parent, false);
            TextMesh label = labelObject.GetComponent<TextMesh>();
            Font font = Resources.Load<Font>("Fonts/NotoSerifSC-Heavy");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            }

            ConfigureDamageText(
                label,
                font,
                Color.white,
                48,
                0.12f,
                30);

            outlines = new TextMesh[DamageOutlineOffsets.Length];
            for (int i = 0; i < outlines.Length; i++)
            {
                var outlineObject = new GameObject(
                    $"Outline_{i}",
                    typeof(TextMesh));
                outlineObject.transform.SetParent(
                    labelObject.transform,
                    false);
                outlineObject.transform.localPosition =
                    DamageOutlineOffsets[i];
                outlines[i] = outlineObject.GetComponent<TextMesh>();
                ConfigureDamageText(
                    outlines[i],
                    font,
                    DamageOutlineColor,
                    48,
                    0.12f,
                    29);
            }

            if (font != null && font.dynamic)
            {
                font.RequestCharactersInTexture(
                    "0123456789-≈击败余威 ",
                    56,
                    FontStyle.Normal);
            }

            return label;
        }

        private static void ConfigureDamageText(
            TextMesh text,
            Font font,
            Color color,
            int fontSize,
            float characterSize,
            int sortingOrder)
        {
            text.font = font;
            text.fontSize = fontSize;
            text.characterSize = characterSize;
            text.fontStyle = FontStyle.Normal;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.richText = false;
            text.color = color;
            MeshRenderer meshRenderer = text.GetComponent<MeshRenderer>();
            meshRenderer.sortingOrder = sortingOrder;
            if (font != null && font.material != null)
                meshRenderer.sharedMaterial = font.material;
        }
    }
}
