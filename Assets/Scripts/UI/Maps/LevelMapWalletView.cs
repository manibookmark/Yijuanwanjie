using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Maps
{
    /// <summary>
    /// Owns the shared map wallet's balance projection and content-driven
    /// width. The prefab authors its bounds, padding and typography; this view
    /// measures the same live Text that it displays, only when balance changes.
    /// It never credits, saves or retains a separate wallet balance.
    /// </summary>
    public sealed class LevelMapWalletView : MonoBehaviour
    {
        public const string PrefabResourcePath = "UI/Maps/LevelMapWallet";
        [SerializeField] private Text balanceText;
        [SerializeField] private RectTransform walletRoot;
        [SerializeField] private float fixedContentWidth = 76f;
        [SerializeField] private float minimumDigitsWidth = 64f;
        [SerializeField] private int balanceFontSize = 22;
        private Func<int> _readBalance;
        private int? _displayedBalance;

        public static LevelMapWalletView Create(Transform parent, Func<int> readBalance)
        {
            if (readBalance == null) throw new ArgumentNullException(nameof(readBalance));
            GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
            if (prefab == null)
                throw new InvalidOperationException("Missing shared map wallet prefab.");
            var view = Instantiate(prefab, parent, false).GetComponent<LevelMapWalletView>();
            view._readBalance = readBalance;
            view.RefreshBalance();
            return view;
        }

        private void OnEnable()
        {
            _displayedBalance = null;
            RefreshBalance();
        }

        private void LateUpdate() => RefreshBalance();

        private void RefreshBalance()
        {
            if (_readBalance == null || balanceText == null) return;
            int balance = _readBalance();
            if (_displayedBalance == balance) return;
            _displayedBalance = balance;
            // Literal integer digits: no grouping comma, abbreviation or rounding.
            balanceText.text = balance.ToString(CultureInfo.InvariantCulture);
            FitBalance();
        }

        private void FitBalance()
        {
            if (walletRoot == null) return;
            balanceText.resizeTextForBestFit = false;
            balanceText.fontSize = Mathf.Max(1, balanceFontSize);
            float textWidth = Mathf.Ceil(balanceText.preferredWidth) + 4f;
            float width = fixedContentWidth + Mathf.Max(minimumDigitsWidth, textWidth);
            // Right-anchored prefab: a larger number grows the case leftward.
            walletRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }
    }
}
