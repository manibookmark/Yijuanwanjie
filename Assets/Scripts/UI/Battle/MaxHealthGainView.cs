using System;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 生命收益的 HUD 因果转场，保留既有组件名称与资源身份。只拥有 HP
    /// 目标绑定和回执；回血与最大生命共用 ManaGainView 的群粒子、时序
    /// 和取消机制，并注入生命专属外观，不自行修改生命或生命上限。
    /// </summary>
    public sealed class MaxHealthGainView : MonoBehaviour
    {
        private Image _hpFill;
        private ManaGainView _transfer;

        public bool IsAwaitingArrival =>
            _transfer != null && _transfer.IsAwaitingArrival;

        public void Bind(Image hpFill)
        {
            _hpFill = hpFill;
        }

        public void Play(int amount, Action onArrived)
        {
            if (amount <= 0 || !isActiveAndEnabled || _hpFill == null)
            {
                onArrived?.Invoke();
                return;
            }

            ManaGainView transfer = EnsureTransfer();
            transfer.Configure(ResourceGainPresentation.Health);
            transfer.Bind(_hpFill);
            transfer.Play(amount, onArrived);
        }

        private ManaGainView EnsureTransfer()
        {
            if (_transfer != null)
                return _transfer;

            var runner = new GameObject(
                "HealthGainTransferRunner",
                typeof(RectTransform));
            runner.transform.SetParent(transform, false);
            _transfer = runner.AddComponent<ManaGainView>();
            return _transfer;
        }
    }
}
