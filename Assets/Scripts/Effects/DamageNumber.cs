using System;
using System.Collections;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 浮动伤害数字（World Space 版）
    /// 出现在被击中目标两侧，向外上方漂浮并淡出
    /// 使用 TextMesh 渲染在世界空间
    /// </summary>
    public class DamageNumber : MonoBehaviour
    {
        [Header("动画参数")]
        public float floatDuration = 0.9f;
        public float floatHeight = 0.75f;
        public float scaleStart = 0.575f;
        public float scaleEnd = 0.45f;

        [Header("散开参数")]
        [Tooltip("连续伤害数字相对命中点的最小水平距离。")]
        public float horizontalOffsetMin = 0.14f;
        [Tooltip("连续伤害数字相对命中点的最大水平距离。")]
        public float horizontalOffsetMax = 0.34f;
        [Tooltip("上浮期间继续向出生侧轻微散开的距离。")]
        public float horizontalDrift = 0.08f;

        private Vector3 _startPos;
        private float _timer = 0f;
        private TextMesh _textMesh;
        private float _activeDuration;
        private float _activeFloatHeight;
        private float _activeScaleStart;
        private float _activeScaleEnd;
        private float _activeHorizontalDrift;
        private TextMesh[] _outlineMeshes;

        private static readonly Vector2[] OutlineOffsets =
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

        private static readonly Color OutlineColor =
            new Color(0.08f, 0.045f, 0.025f, 0.96f);

        private void Awake()
        {
            _textMesh = GetComponent<TextMesh>();
            _startPos = transform.localPosition;
            ConfigureTypography();
        }

        /// <summary>
        /// 设置伤害数字并开始动画
        /// </summary>
        public void Show(int damage, bool isCrit = false)
        {
            ShowStyled(
                damage,
                isCrit
                    ? new Color(0.92f, 0.12f, 0.06f, 1f)
                    : new Color(0.96f, 0.93f, 0.82f, 1f),
                isCrit ? 52 : 48,
                isCrit ? 0.112f : 0.102f,
                isCrit ? 1.08f : 1f,
                1f,
                0,
                GetInstanceID());
        }

        /// <summary>
        /// 按权威 DamageEvent 展示本次实际扣除的护甲与生命。
        /// 一次事件只生成一个数字；余威不计入扣除数值。
        /// 因此请求 30 点伤害击杀 12 HP 目标时显示 -12。
        /// </summary>
        public void Show(DamageEvent damageEvent, int sequenceOrdinal = 0)
        {
            bool shieldOnly = damageEvent.ShieldDamage > 0 &&
                              damageEvent.HpDamage <= 0;
            bool splash = damageEvent.Kind == DamageKind.Splash;

            Color color = shieldOnly
                ? new Color(0.25f, 0.68f, 1f, 1f)
                : damageEvent.IsCritical
                    ? new Color(0.92f, 0.12f, 0.06f, 1f)
                    : new Color(0.96f, 0.93f, 0.82f, 1f);
            int fontSize = damageEvent.IsCritical ? 52 : splash ? 44 : 48;
            float characterSize = damageEvent.IsCritical
                ? 0.112f
                : splash ? 0.088f : 0.102f;
            float scaleMultiplier = damageEvent.IsCritical
                ? 1.08f
                : splash ? 0.9f : 1f;
            float durationMultiplier = splash ? 0.78f : 1f;

            ShowStyled(
                damageEvent.AppliedDamage,
                color,
                fontSize,
                characterSize,
                scaleMultiplier,
                durationMultiplier,
                sequenceOrdinal,
                GetTargetKey(damageEvent));
        }

        private void ShowStyled(
            int damage,
            Color color,
            int fontSize,
            float characterSize,
            float scaleMultiplier,
            float durationMultiplier,
            int sequenceOrdinal,
            int targetKey)
        {
            _timer = 0f;
            float horizontalOffset = CalculateHorizontalOffset(
                sequenceOrdinal,
                targetKey);
            _startPos = transform.position +
                Vector3.right * horizontalOffset;
            transform.position = _startPos;
            _activeDuration = floatDuration * durationMultiplier;
            _activeFloatHeight = floatHeight * scaleMultiplier;
            _activeScaleStart = scaleStart * scaleMultiplier;
            _activeScaleEnd = scaleEnd * scaleMultiplier;
            _activeHorizontalDrift = Mathf.Sign(horizontalOffset) *
                Mathf.Max(0f, horizontalDrift);
            transform.localScale = Vector3.one * _activeScaleStart;

            string text = damage > 0 ? $"-{damage}" : "MISS";

            if (_textMesh != null)
            {
                _textMesh.text = text;
                _textMesh.color = color;
                _textMesh.fontSize = fontSize;
                _textMesh.characterSize = characterSize;
            }
            for (int i = 0;
                 _outlineMeshes != null && i < _outlineMeshes.Length;
                 i++)
            {
                TextMesh outline = _outlineMeshes[i];
                outline.text = text;
                outline.color = OutlineColor;
                outline.fontSize = fontSize;
                outline.characterSize = characterSize;
            }

            // 对象池可能在上一轮 Animate 收尾前先停用对象，Unity 此时会中止
            // 协程。每次 Show 都显式重启动画，不依赖上轮能否写回结束标记。
            StopAllCoroutines();
            StartCoroutine(Animate());
        }

        private IEnumerator Animate()
        {
            while (_timer < _activeDuration)
            {
                _timer += Time.deltaTime;
                float t = _timer / _activeDuration;

                // 在命中点两侧的独立轨道上上浮，并继续向外轻微散开。
                float yOffset = _activeFloatHeight *
                    Mathf.Sin(t * Mathf.PI * 0.5f);
                float xOffset = _activeHorizontalDrift *
                    Mathf.Sin(t * Mathf.PI * 0.5f);
                transform.position = _startPos +
                    new Vector3(xOffset, yOffset, 0f);

                // 缩放
                float scale = Mathf.Lerp(
                    _activeScaleStart,
                    _activeScaleEnd,
                    t);
                transform.localScale = Vector3.one * scale;

                // 淡出（后半程）
                if (_textMesh != null)
                {
                    var c = _textMesh.color;
                    c.a = t < 0.6f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.6f) / 0.4f);
                    _textMesh.color = c;
                    for (int i = 0;
                         _outlineMeshes != null && i < _outlineMeshes.Length;
                         i++)
                    {
                        Color outlineColor = _outlineMeshes[i].color;
                        outlineColor.a = c.a * OutlineColor.a;
                        _outlineMeshes[i].color = outlineColor;
                    }
                }

                yield return null;
            }

            gameObject.SetActive(false);
        }

        /// <summary>
        /// 同一目标的连续数字左右交替，并在三个距离带内做稳定扰动。
        /// 这样既保留自然感，也避免纯随机连续落到同一位置。
        /// </summary>
        private float CalculateHorizontalOffset(
            int sequenceOrdinal,
            int targetKey)
        {
            int safeOrdinal = Mathf.Max(0, sequenceOrdinal);
            float min = Mathf.Max(0f, horizontalOffsetMin);
            float max = Mathf.Max(min, horizontalOffsetMax);
            int distanceBand = (safeOrdinal / 2) % 3;
            float bandWidth = (max - min) / 3f;

            int hash = unchecked(
                targetKey * 397 ^
                (safeOrdinal + 1) * 7919 ^
                GetInstanceID());
            float jitter01 = (hash & 0x3ff) / 1023f;
            float magnitude = min + bandWidth *
                (distanceBand + Mathf.Lerp(0.25f, 0.75f, jitter01));

            bool firstOnRight = (targetKey & 1) == 0;
            bool onRight = (safeOrdinal & 1) == 0
                ? firstOnRight
                : !firstOnRight;
            return onRight ? magnitude : -magnitude;
        }

        private static int GetTargetKey(DamageEvent damageEvent)
            => damageEvent.TargetEnemyRuntimeId > 0
                ? damageEvent.TargetEnemyRuntimeId
                : -(damageEvent.TargetIndex + 1);

        private void ConfigureTypography()
        {
            if (_textMesh == null) return;

            Font font = Resources.Load<Font>("Fonts/NotoSerifSC-Heavy");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            }

            ConfigureTextMesh(_textMesh, font, Color.white, 40);
            _outlineMeshes = new TextMesh[OutlineOffsets.Length];
            for (int i = 0; i < _outlineMeshes.Length; i++)
            {
                var outlineObject = new GameObject(
                    $"Outline_{i}",
                    typeof(TextMesh));
                outlineObject.transform.SetParent(transform, false);
                outlineObject.transform.localPosition = OutlineOffsets[i];
                TextMesh outline = outlineObject.GetComponent<TextMesh>();
                ConfigureTextMesh(outline, font, OutlineColor, 39);
                _outlineMeshes[i] = outline;
            }

            if (font != null && font.dynamic)
            {
                font.RequestCharactersInTexture(
                    "0123456789-MISS",
                    52,
                    FontStyle.Normal);
            }
        }

        private static void ConfigureTextMesh(
            TextMesh text,
            Font font,
            Color color,
            int sortingOrder)
        {
            text.font = font;
            text.fontSize = 48;
            text.characterSize = 0.102f;
            text.fontStyle = FontStyle.Normal;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.richText = false;
            text.color = color;

            MeshRenderer renderer = text.GetComponent<MeshRenderer>();
            renderer.sortingOrder = sortingOrder;
            if (font != null && font.material != null)
                renderer.sharedMaterial = font.material;
        }
    }
}
