using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Overlays
{
    /// <summary>
    /// 结算页伤害排行的入场动画驱动器：条形从 0 生长、总伤/次均伤害数字 count-up，
    /// 逐行 stagger。只驱动 Show 时绑定好的行对象，不读取任何业务数据；
    /// 动画使用 unscaledDeltaTime，播放完成后对象状态与静态构建一致。
    /// </summary>
    public sealed class RunSummaryRankingAnimator : MonoBehaviour
    {
        private const float RowStaggerSeconds = 0.07f;
        private const float AnimateSeconds = 0.45f;

        private readonly List<RowAnimation> _rows = new List<RowAnimation>();
        private float _elapsed;
        private float _totalDuration;
        private bool _playing;

        private sealed class RowAnimation
        {
            internal RectTransform Fill;
            internal Text DamageText;
            internal Text AverageText;
            internal float TargetFraction;
            internal int TargetDamage;
            internal float Average;
            internal string DamageFormat;
            internal string AverageFormat;
        }

        /// <summary>
        /// 注册一行并立即置于动画起始状态（条形 0、数字 0）。
        /// 两个数值文本使用调用方提供的格式。
        /// </summary>
        public void RegisterRow(
            RectTransform fill,
            Text damageText,
            Text averageText,
            float targetFraction,
            int targetDamage,
            float average,
            string damageFormat,
            string averageFormat)
        {
            if (fill == null)
                return;
            _rows.Add(new RowAnimation
            {
                Fill = fill,
                DamageText = damageText,
                AverageText = averageText,
                TargetFraction = Mathf.Clamp01(targetFraction),
                TargetDamage = Mathf.Max(0, targetDamage),
                Average = Mathf.Max(0f, average),
                DamageFormat = damageFormat,
                AverageFormat = averageFormat
            });
            fill.anchorMax = new Vector2(0f, 1f);
            _totalDuration = AnimateSeconds +
                RowStaggerSeconds * Mathf.Max(0, _rows.Count - 1);
            _playing = true;
            _elapsed = 0f;
        }

        /// <summary>立即跳到最终状态（面板关闭或重建时调用，避免中途状态残留）。</summary>
        public void CompleteImmediately()
        {
            for (int i = 0; i < _rows.Count; i++)
                ApplyRow(_rows[i], 1f);
            _playing = false;
        }

        private void Update()
        {
            if (!_playing || _rows.Count == 0)
                return;

            _elapsed += Time.unscaledDeltaTime;
            bool allDone = true;
            for (int i = 0; i < _rows.Count; i++)
            {
                float local = _elapsed - i * RowStaggerSeconds;
                if (local < 0f)
                {
                    allDone = false;
                    continue;
                }
                float t = Mathf.Clamp01(local / AnimateSeconds);
                if (t < 1f)
                    allDone = false;
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                ApplyRow(_rows[i], eased);
            }
            if (allDone || _elapsed >= _totalDuration + 0.05f)
            {
                CompleteImmediately();
            }
        }

        private static void ApplyRow(RowAnimation row, float easedT)
        {
            if (row.Fill != null)
            {
                row.Fill.anchorMax = new Vector2(
                    row.TargetFraction * easedT,
                    1f);
            }
            if (row.DamageText != null)
            {
                int value = Mathf.RoundToInt(row.TargetDamage * easedT);
                row.DamageText.text = string.Format(
                    row.DamageFormat, value);
            }
            if (row.AverageText != null)
            {
                row.AverageText.text = string.Format(
                    row.AverageFormat, row.Average * easedT);
            }
        }
    }
}
