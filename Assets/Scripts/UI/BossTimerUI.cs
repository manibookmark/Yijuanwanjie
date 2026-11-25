using UnityEngine;
using UnityEngine.UI;
using TMPro;
using WanjieScroll.Core;

namespace WanjieScroll.UI
{
    /// <summary>
    /// Boss 天劫计时器 UI
    /// 显示劫眼进度，全开时预警闪烁
    /// </summary>
    public class BossTimerUI : MonoBehaviour
    {
        [Header("References")]
        public Transform eyeContainer;
        public GameObject eyeOpenPrefab;
        public GameObject eyeClosedPrefab;
        public TMP_Text timerText;
        public Image warningFlash;

        [Header("Effects")]
        public float flashSpeed = 2f;
        public Color warningColor = new Color(0.75f, 0.22f, 0.17f, 0.5f);

        private BossTimer _bossTimer;
        private bool _warningActive;

        public void Setup(BossTimer timer)
        {
            _bossTimer = timer;
            _bossTimer.OnEyesChanged += UpdateEyes;
            _bossTimer.OnTianjieTriggered += OnTianjie;
            UpdateEyes();
        }

        void UpdateEyes()
        {
            if (_bossTimer == null || eyeContainer == null) return;

            for (int i = eyeContainer.childCount - 1; i >= 0; i--)
                Destroy(eyeContainer.GetChild(i).gameObject);

            for (int i = 0; i < _bossTimer.TotalEyes; i++)
            {
                bool open = i < _bossTimer.OpenEyes;
                var prefab = open ? eyeOpenPrefab : eyeClosedPrefab;
                if (prefab != null)
                    Instantiate(prefab, eyeContainer);
            }

            if (timerText != null)
                timerText.text = _bossTimer.GetSummary();

            // 预警：只剩 1 眼时闪烁
            bool shouldWarn = _bossTimer.OpenEyes >= _bossTimer.TotalEyes - 1;
            if (shouldWarn != _warningActive)
            {
                _warningActive = shouldWarn;
            }
        }

        void OnTianjie()
        {
            if (warningFlash != null)
            {
                warningFlash.color = warningColor;
                warningFlash.gameObject.SetActive(true);
            }
        }

        void Update()
        {
            if (_warningActive && warningFlash != null)
            {
                var color = warningFlash.color;
                color.a = Mathf.PingPong(Time.time * flashSpeed, 0.5f) + 0.2f;
                warningFlash.color = color;
            }
            else if (warningFlash != null && !_warningActive)
            {
                warningFlash.gameObject.SetActive(false);
            }
        }
    }
}
