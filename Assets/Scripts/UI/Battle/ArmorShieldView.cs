using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 纸盾护甲视觉组件。覆盖生命球中央数字，显示优先消耗的护甲值。
    /// 获得护甲时弹出动画，被击中时抖动，耗尽时撕裂消失。
    /// 同时将 HP 条染色为土褐色（方案B）。
    /// </summary>
    public sealed class ArmorShieldView : MonoBehaviour
    {
        [Header("护甲盾")]
        public Image shieldImage;
        public Image shieldBorder;
        public Text armorText;

        [Header("配色")]
        public Color shieldColor = new Color(0.55f, 0.42f, 0.28f, 0.88f);
        public Color borderColor = new Color(0.32f, 0.22f, 0.12f, 0.9f);
        public Color textColor = new Color(1f, 0.95f, 0.82f);
        public Color armoredHpColor = new Color(0.72f, 0.55f, 0.35f, 1f);

        [Header("获得护甲转场")]
        [Tooltip("护甲轮廓在出牌区域成形所需时间。")]
        public float transferRevealDuration = 0.09f;
        [Tooltip("护甲轮廓飞向 HUD 所需时间。")]
        public float transferFlightDuration = 0.28f;
        public Color transferColor = new Color(1f, 0.88f, 0.58f, 0.96f);

        private Image _hpFullImage;
        private Text _hpValueText;
        private Color _originalHpColor;
        private int _currentArmor;
        private int _previousArmor;
        private bool _hasArmorSnapshot;
        private bool _hpColorStored;
        private Coroutine _activeAnim;
        private Coroutine _gainTransferAnim;
        private Image _gainTransferImage;

        /// <summary>
        /// 初始化，传入 HP 条前景 Image 用于变色。
        /// </summary>
        public void Initialize(Image hpFullImage, Text hpValueText = null)
        {
            SetHpNumberCovered(false);
            _hpValueText = hpValueText;
            SetHpNumberCovered(false);
            _hpFullImage = hpFullImage;
            if (_hpFullImage != null)
            {
                _originalHpColor = _hpFullImage.color;
                _hpColorStored = true;
            }
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Releases only presentation state. The next authoritative snapshot is
        /// applied as an initial value, without a synthetic gain/depletion.
        /// </summary>
        public void ResetEncounterPresentation()
        {
            CancelGainTransfer();
            StopActiveAnim();
            _hasArmorSnapshot = false;
            _currentArmor = 0;
            _previousArmor = 0;
            UpdateHpBarColor(false);
            UpdateText(0);
            SetArmorVisualActive(false);
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 更新护甲显示。自动检测增减并播放对应动画。
        /// </summary>
        public void UpdateArmor(int armor)
        {
            armor = Mathf.Max(0, armor);
            if (!_hasArmorSnapshot)
            {
                _hasArmorSnapshot = true;
                _previousArmor = armor;
                _currentArmor = armor;
                ApplyInitialArmorSnapshot(armor);
                return;
            }

            _previousArmor = _currentArmor;
            _currentArmor = armor;

            if (armor > 0)
            {
                bool wasZero = _previousArmor == 0;
                if (wasZero || armor > _previousArmor)
                {
                    PlayGainTransfer(wasZero);
                }
                else if (armor < _previousArmor)
                {
                    CancelGainTransfer();
                    SetArmorVisualActive(true);
                    UpdateHpBarColor(true);
                    UpdateText(armor);
                    PlayConsumeAnimation();
                }
            }
            else
            {
                CancelGainTransfer();
                UpdateHpBarColor(false);
                if (_previousArmor > 0)
                {
                    UpdateText(0);
                    PlayDepleteAnimation();
                }
                else
                {
                    gameObject.SetActive(false);
                }
            }
        }

        private void ApplyInitialArmorSnapshot(int armor)
        {
            CancelGainTransfer();
            UpdateHpBarColor(armor > 0);
            if (armor <= 0)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            SetArmorVisualActive(true);
            ResetVisual();
            UpdateText(armor);
        }

        /// <summary>
        /// Reuses the exact HUD shield sprite as a text-free transfer shape.
        /// It forms above the play area, flies to this view, then reveals the
        /// authoritative armor value. No card name or glyph text is involved.
        /// </summary>
        private void PlayGainTransfer(bool hideUntilArrival)
        {
            CancelGainTransfer();
            StopActiveAnim();
            gameObject.SetActive(true);
            if (hideUntilArrival)
                SetArmorVisualActive(false);

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null || shieldImage == null ||
                shieldImage.sprite == null)
            {
                CompleteGainTransfer();
                return;
            }

            _gainTransferAnim = StartCoroutine(
                GainTransferRoutine(canvas));
        }

        private IEnumerator GainTransferRoutine(Canvas canvas)
        {
            Canvas.ForceUpdateCanvases();
            RectTransform canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null)
            {
                CompleteGainTransfer();
                yield break;
            }

            var transferObject = new GameObject(
                "ArmorGainTransfer",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            transferObject.transform.SetParent(canvas.transform, false);
            transferObject.transform.SetAsLastSibling();
            _gainTransferImage = transferObject.GetComponent<Image>();
            _gainTransferImage.raycastTarget = false;
            _gainTransferImage.preserveAspect = true;
            _gainTransferImage.sprite = shieldImage.sprite;
            _gainTransferImage.color = new Color(
                transferColor.r,
                transferColor.g,
                transferColor.b,
                0f);

            RectTransform transferRect = _gainTransferImage.rectTransform;
            transferRect.anchorMin = new Vector2(0.5f, 0.5f);
            transferRect.anchorMax = new Vector2(0.5f, 0.5f);
            transferRect.pivot = new Vector2(0.5f, 0.5f);
            Vector2 targetSize = shieldImage.rectTransform.rect.size;
            if (targetSize.x <= 1f || targetSize.y <= 1f)
                targetSize = new Vector2(90f, 120f);
            transferRect.sizeDelta = targetSize * (PowerUpGlyphConfig.GainGlyphSize /
                Mathf.Max(targetSize.x, targetSize.y));

            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;
            Vector2 targetScreen = RectTransformUtility.WorldToScreenPoint(
                uiCamera,
                shieldImage.rectTransform.position);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    targetScreen,
                    uiCamera,
                    out Vector2 destination))
            {
                destination = Vector2.zero;
            }

            Rect canvasBounds = canvasRect.rect;
            Vector2 start = new Vector2(
                Mathf.Lerp(canvasBounds.xMin, canvasBounds.xMax, 0.5f),
                Mathf.Lerp(canvasBounds.yMin, canvasBounds.yMax, 0.2f));
            transferRect.localPosition = new Vector3(start.x, start.y, 0f);

            float revealDuration = Mathf.Max(0.01f, transferRevealDuration);
            float elapsed = 0f;
            while (elapsed < revealDuration)
            {
                float t = Mathf.Clamp01(elapsed / revealDuration);
                float eased = t * t * (3f - 2f * t);
                float scale = Mathf.Lerp(0.72f, 1f, eased);
                transferRect.localScale = new Vector3(scale, scale, 1f);
                Color color = transferColor;
                color.a *= eased;
                _gainTransferImage.color = color;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Vector2 control = (start + destination) * 0.5f +
                Vector2.up * Mathf.Clamp(
                    Mathf.Abs(destination.x - start.x) * 0.16f + 70f,
                    70f,
                    170f);
            float flightDuration = Mathf.Max(0.01f, transferFlightDuration);
            elapsed = 0f;
            while (elapsed < flightDuration)
            {
                float t = Mathf.Clamp01(elapsed / flightDuration);
                float eased = t * t * (3f - 2f * t);
                float inverse = 1f - eased;
                Vector2 position =
                    inverse * inverse * start +
                    2f * inverse * eased * control +
                    eased * eased * destination;
                transferRect.localPosition = new Vector3(
                    position.x,
                    position.y,
                    0f);
                float scale = Mathf.Lerp(1f, 0.25f, eased);
                transferRect.localScale = new Vector3(scale, scale, 1f);
                transferRect.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    Mathf.Lerp(-7f, 0f, eased));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _gainTransferAnim = null;
            _gainTransferImage = null;
            Destroy(transferObject);
            CompleteGainTransfer();
        }

        private void CompleteGainTransfer()
        {
            _gainTransferAnim = null;
            if (_currentArmor <= 0)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            SetArmorVisualActive(true);
            UpdateHpBarColor(true);
            ResetVisual();
            UpdateText(_currentArmor);
            PlayGainAnimation();
        }

        private void CancelGainTransfer()
        {
            if (_gainTransferAnim != null)
            {
                StopCoroutine(_gainTransferAnim);
                _gainTransferAnim = null;
            }
            if (_gainTransferImage != null)
            {
                _gainTransferImage.gameObject.SetActive(false);
                Destroy(_gainTransferImage.gameObject);
                _gainTransferImage = null;
            }
        }

        private void SetArmorVisualActive(bool active)
        {
            SetHpNumberCovered(active && _currentArmor > 0);
            if (shieldImage != null)
                shieldImage.gameObject.SetActive(active);
            if (shieldBorder != null)
                shieldBorder.gameObject.SetActive(active);
            if (armorText != null)
                armorText.gameObject.SetActive(active);
        }

        private void OnEnable()
        {
            if (!_hasArmorSnapshot || _currentArmor <= 0)
            {
                SetArmorVisualActive(false);
                return;
            }
            SetArmorVisualActive(true);
            UpdateHpBarColor(true);
            UpdateText(_currentArmor);
            ResetVisual();
        }

        private void OnDisable()
        {
            CancelGainTransfer();
            StopActiveAnim();
            SetHpNumberCovered(false);
        }

        private void OnDestroy()
        {
            CancelGainTransfer();
            StopActiveAnim();
            SetHpNumberCovered(false);
        }

        // This owner alone controls numerical coverage. HP text/fill snapshots
        // remain owned by BattleHudView, including during a gain or depletion.
        private void SetHpNumberCovered(bool covered)
        {
            if (_hpValueText != null)
                _hpValueText.enabled = !covered;
        }

        private void UpdateText(int armor)
        {
            if (armorText != null)
                armorText.text = armor > 0 ? armor.ToString() : "";
        }

        private void UpdateHpBarColor(bool armored)
        {
            if (_hpFullImage == null || !_hpColorStored) return;
            _hpFullImage.color = armored ? armoredHpColor : _originalHpColor;
        }

        private void ResetVisual()
        {
            if (shieldImage != null)
            {
                var rt = shieldImage.rectTransform;
                rt.localPosition = Vector3.zero;
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
                shieldImage.color = shieldColor;
            }
            if (shieldBorder != null)
            {
                var rt = shieldBorder.rectTransform;
                rt.localPosition = Vector3.zero;
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
                shieldBorder.color = borderColor;
            }
        }

        private void PlayGainAnimation()
        {
            StopActiveAnim();
            _activeAnim = StartCoroutine(GainRoutine());
        }

        private void PlayConsumeAnimation()
        {
            StopActiveAnim();
            _activeAnim = StartCoroutine(ConsumeRoutine());
        }

        private void PlayDepleteAnimation()
        {
            StopActiveAnim();
            _activeAnim = StartCoroutine(DepleteRoutine());
        }

        private void StopActiveAnim()
        {
            if (_activeAnim != null)
            {
                StopCoroutine(_activeAnim);
                _activeAnim = null;
            }
            ResetVisual();
        }

        private IEnumerator GainRoutine()
        {
            if (shieldImage == null) yield break;
            var rt = shieldImage.rectTransform;
            float duration = 0.3f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float scale;
                if (t < 0.6f)
                    scale = Mathf.Lerp(0.5f, 1.15f, t / 0.6f);
                else
                    scale = Mathf.Lerp(1.15f, 1f, (t - 0.6f) / 0.4f);
                rt.localScale = new Vector3(scale, scale, 1f);
                elapsed += Time.deltaTime;
                yield return null;
            }
            rt.localScale = Vector3.one;
            _activeAnim = null;
        }

        private IEnumerator ConsumeRoutine()
        {
            if (shieldImage == null) yield break;
            var rt = shieldImage.rectTransform;
            float duration = 0.18f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float shakeX = Mathf.Sin(t * Mathf.PI * 8f) * 6f * (1f - t);
                rt.localPosition = new Vector3(shakeX, 0f, 0f);
                elapsed += Time.deltaTime;
                yield return null;
            }
            rt.localPosition = Vector3.zero;
            _activeAnim = null;
        }

        private IEnumerator DepleteRoutine()
        {
            if (shieldImage == null) yield break;
            var rt = shieldImage.rectTransform;
            var img = shieldImage;
            float duration = 0.25f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                rt.localScale = new Vector3(
                    1f + t * 0.2f,
                    1f - t * 0.95f,
                    1f);
                var c = img.color;
                c.a = shieldColor.a * (1f - t);
                img.color = c;
                rt.localRotation = Quaternion.Euler(0, 0, t * 8f);
                elapsed += Time.deltaTime;
                yield return null;
            }

            gameObject.SetActive(false);
            ResetVisual();
            _activeAnim = null;
        }
    }
}
