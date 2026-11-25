using System.Collections;
using UnityEngine;

namespace WanjieScroll.UI.Battle
{
    /// <summary>
    /// 敌人区域顶部云层的单一生命周期所有者。关 BOSS 落地时把原 Sprite
    /// 临时切成左右两半，向两侧震开并淡出；不依赖 Effects 程序集。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TopFogDispersalView : MonoBehaviour
    {
        private SpriteRenderer _source;
        private GameObject _splitRoot;
        private SpriteRenderer _left;
        private SpriteRenderer _right;
        private Sprite _leftSprite;
        private Sprite _rightSprite;
        private Coroutine _routine;
        private Color _originalColor;
        private bool _originalEnabled;
        private Vector3 _originalLocalPosition;
        private Vector3 _originalLocalScale;
        private bool _bound;
        private float _seamLocalX;

        public bool IsDismissed { get; private set; }

        public void Bind(SpriteRenderer source)
        {
            if (source == null)
                return;
            Restore();
            _source = source;
            _originalColor = source.color;
            _originalEnabled = source.enabled;
            _originalLocalPosition = source.transform.localPosition;
            _originalLocalScale = source.transform.localScale;
            _bound = true;
        }

        public void Dismiss(float duration, float distanceRatio)
        {
            if (!_bound || _source == null || IsDismissed || !isActiveAndEnabled)
                return;
            Restore();
            _routine = StartCoroutine(AnimateDismissal(
                Mathf.Max(0.05f, duration),
                Mathf.Max(0.05f, distanceRatio)));
        }

        public void DismissUpward(float duration, float riseRatio)
        {
            if (!_bound || _source == null || IsDismissed || !isActiveAndEnabled)
                return;
            Restore();
            _routine = StartCoroutine(AnimateUpwardDismissal(
                Mathf.Max(0.05f, duration),
                Mathf.Clamp(riseRatio, 0.1f, 0.8f)));
        }

        public void Restore()
        {
            CancelDismissal();
            IsDismissed = false;
            if (!_bound || _source == null) return;
            _source.color = _originalColor;
            _source.enabled = _originalEnabled;
            _source.transform.localPosition = _originalLocalPosition;
            _source.transform.localScale = _originalLocalScale;
        }

        private void CancelDismissal()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            ReleaseSplit();
        }

        private IEnumerator AnimateUpwardDismissal(
            float duration,
            float riseRatio)
        {
            ReleaseSplit();
            _source.enabled = true;
            float height = _source.sprite != null
                ? _source.sprite.bounds.size.y
                : 1f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float ease = 1f - Mathf.Pow(1f - t, 3f);
                float fade = 1f - Mathf.SmoothStep(0.04f, 1f, t);
                _source.transform.localPosition = _originalLocalPosition +
                    Vector3.up * (height * riseRatio * ease);
                _source.transform.localScale = _originalLocalScale *
                    Mathf.Lerp(1f, 1.04f, ease);
                Color color = _originalColor;
                color.a *= fade;
                _source.color = color;
                yield return null;
            }
            _source.transform.localPosition = _originalLocalPosition;
            _source.transform.localScale = _originalLocalScale;
            _source.color = _originalColor;
            _source.enabled = false;
            _routine = null;
            IsDismissed = true;
        }

        private IEnumerator AnimateDismissal(
            float duration,
            float distanceRatio)
        {
            bool split = TryCreateSplit();
            Vector3 originalScale = _originalLocalScale;
            Color originalColor = _originalColor;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float ease = 1f - Mathf.Pow(1f - t, 3f);
                float fade = 1f - Mathf.SmoothStep(0.08f, 1f, t);
                if (split)
                    ApplySplitFrame(ease, fade, distanceRatio);
                else
                {
                    float shock = Mathf.Sin(t * Mathf.PI * 5f) *
                                  (1f - t) * 0.025f;
                    _source.transform.localScale = new Vector3(
                        originalScale.x * (1f + ease * 0.34f + shock),
                        originalScale.y * (1f - ease * 0.12f),
                        originalScale.z);
                    Color color = originalColor;
                    color.a *= fade;
                    _source.color = color;
                }
                yield return null;
            }

            _source.transform.localScale = originalScale;
            _source.transform.localPosition = _originalLocalPosition;
            _source.color = originalColor;
            _source.enabled = false;
            ReleaseSplit();
            _routine = null;
            IsDismissed = true;
        }

        private bool TryCreateSplit()
        {
            Sprite sourceSprite = _source.sprite;
            if (sourceSprite == null || sourceSprite.texture == null ||
                sourceSprite.pixelsPerUnit <= 0f ||
                (sourceSprite.packed &&
                 sourceSprite.packingRotation != SpritePackingRotation.None))
            {
                return false;
            }

            Rect textureRect = sourceSprite.textureRect;
            int leftWidth = Mathf.FloorToInt(textureRect.width * 0.5f);
            int rightWidth = Mathf.RoundToInt(textureRect.width) - leftWidth;
            if (leftWidth < 2 || rightWidth < 2 || textureRect.height < 2f)
                return false;

            Rect leftRect = new Rect(
                textureRect.x,
                textureRect.y,
                leftWidth,
                textureRect.height);
            Rect rightRect = new Rect(
                textureRect.x + leftWidth,
                textureRect.y,
                rightWidth,
                textureRect.height);
            float pivotY = Mathf.Clamp01(
                (sourceSprite.pivot.y - sourceSprite.textureRectOffset.y) /
                Mathf.Max(1f, textureRect.height));
            _leftSprite = Sprite.Create(
                sourceSprite.texture,
                leftRect,
                new Vector2(1f, pivotY),
                sourceSprite.pixelsPerUnit,
                0u,
                SpriteMeshType.FullRect);
            _rightSprite = Sprite.Create(
                sourceSprite.texture,
                rightRect,
                new Vector2(0f, pivotY),
                sourceSprite.pixelsPerUnit,
                0u,
                SpriteMeshType.FullRect);
            _leftSprite.name = $"{sourceSprite.name}_DismissLeft";
            _rightSprite.name = $"{sourceSprite.name}_DismissRight";

            _splitRoot = new GameObject("TopFogDispersal");
            Transform root = _splitRoot.transform;
            root.SetParent(_source.transform.parent, false);
            root.localPosition = _source.transform.localPosition;
            root.localRotation = _source.transform.localRotation;
            root.localScale = _source.transform.localScale;

            _seamLocalX =
                (sourceSprite.textureRectOffset.x + leftWidth -
                 sourceSprite.pivot.x) / sourceSprite.pixelsPerUnit;
            _left = CreateHalf("TopFogLeft", _leftSprite, _seamLocalX);
            _right = CreateHalf("TopFogRight", _rightSprite, _seamLocalX);
            _source.enabled = false;
            return _left != null && _right != null;
        }

        private SpriteRenderer CreateHalf(
            string objectName,
            Sprite sprite,
            float seamLocalX)
        {
            var child = new GameObject(objectName, typeof(SpriteRenderer));
            child.transform.SetParent(_splitRoot.transform, false);
            child.transform.localPosition = new Vector3(seamLocalX, 0f, 0f);
            SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = _source.sharedMaterial;
            renderer.color = _originalColor;
            renderer.sortingLayerID = _source.sortingLayerID;
            renderer.sortingOrder = _source.sortingOrder;
            renderer.flipX = _source.flipX;
            renderer.flipY = _source.flipY;
            renderer.maskInteraction = _source.maskInteraction;
            return renderer;
        }

        private void ApplySplitFrame(
            float ease,
            float fade,
            float distanceRatio)
        {
            if (_left == null || _right == null || _source.sprite == null)
                return;
            float width = _source.sprite.bounds.size.x;
            float height = _source.sprite.bounds.size.y;
            float seam = _seamLocalX;
            float distance = width * distanceRatio * ease;
            float shock = Mathf.Sin(ease * Mathf.PI * 6f) *
                          (1f - ease) * width * 0.018f;
            _left.transform.localPosition = new Vector3(
                seam - distance - shock,
                height * 0.07f * ease,
                0f);
            _right.transform.localPosition = new Vector3(
                seam + distance + shock,
                height * 0.045f * ease,
                0f);
            _left.transform.localRotation = Quaternion.Euler(
                0f, 0f, 7f * ease);
            _right.transform.localRotation = Quaternion.Euler(
                0f, 0f, -6f * ease);
            float stretch = 1f + ease * 0.12f;
            _left.transform.localScale = new Vector3(stretch, 1f, 1f);
            _right.transform.localScale = new Vector3(stretch, 1f, 1f);
            Color color = _originalColor;
            color.a *= fade;
            _left.color = color;
            _right.color = color;
        }

        private void ReleaseSplit()
        {
            if (_splitRoot != null)
            {
                _splitRoot.SetActive(false);
                if (Application.isPlaying)
                    Destroy(_splitRoot);
                else
                    DestroyImmediate(_splitRoot);
            }
            _splitRoot = null;
            _left = null;
            _right = null;
            DestroyRuntimeSprite(ref _leftSprite);
            DestroyRuntimeSprite(ref _rightSprite);
        }

        private static void DestroyRuntimeSprite(ref Sprite sprite)
        {
            if (sprite == null)
                return;
            if (Application.isPlaying)
                Destroy(sprite);
            else
                DestroyImmediate(sprite);
            sprite = null;
        }

        private void OnDisable()
        {
            // A completed dismissal remains dismissed. An interrupted one
            // restores the source and never claims a natural completion.
            if (_routine != null) Restore();
            else ReleaseSplit();
        }

        private void OnDestroy() => CancelDismissal();
    }
}
