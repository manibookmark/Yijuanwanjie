using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Meta;
using WanjieScroll.Core;
using WanjieScroll.Persistence;

namespace WanjieScroll.UI.Daozang
{
    /// <summary>
    /// 道藏业务控制器：处理修习、返璞等数据操作。
    /// View 只负责显示，用户意图通过 Controller 执行。
    /// </summary>
    public sealed class DaozangController
    {
        private readonly MetaProgression _meta;
        private readonly GameSaveService _saveService;

        public event System.Action OnMetaChanged;

        public DaozangController(MetaProgression meta, GameSaveService saveService)
        {
            _meta = meta;
            _saveService = saveService;
        }

        public MetaProgression Meta => _meta;

        public int Lingyu => _meta?.lingyu ?? 0;

        public int GetLevel(string jingjueName)
            => DaozangConfig.GetLevel(_meta, jingjueName);

        public bool IsLocked(UnlockTier tier)
            => DaozangConfig.IsLocked(_meta, tier);

        public string GetUnlockHint(UnlockTier tier)
            => DaozangProgressionRules.GetUnlockHint(_meta, tier);

        public bool CanStudy(DaozangConfig.JingjueInfo info)
        {
            if (_meta == null) return false;
            int level = GetLevel(info.Name);
            bool locked = IsLocked(info.Tier);
            bool maxed = level >= info.MaxLevel;
            bool canAfford = _meta.lingyu >= DaozangConfig.GetLevelPrice(info, level + 1);
            return !locked && !maxed && canAfford;
        }

        public int GetNextPrice(DaozangConfig.JingjueInfo info)
            => DaozangConfig.GetLevelPrice(info, GetLevel(info.Id) + 1);

        public bool TryStudy(DaozangConfig.JingjueInfo info)
        {
            if (!CanStudy(info)) return false;
            var cfg = DaozangConfig.CreatePowerUpConfig(info);
            bool success = _meta.BuyPowerUp(cfg);
            Object.Destroy(cfg);
            if (success)
            {
                SaveAndNotify();
                return true;
            }
            return false;
        }

        public void RefundAll()
        {
            if (_meta == null) return;

            var allConfigs = new List<PowerUpConfig>();
            foreach (var info in DaozangConfig.AllJingjue())
                allConfigs.Add(DaozangConfig.CreatePowerUpConfig(info));

            _meta.RefundAll(allConfigs);

            foreach (var cfg in allConfigs)
                Object.Destroy(cfg);

            SaveAndNotify();
        }

        private void SaveAndNotify()
        {
            if (_saveService != null && _meta != null)
            {
                _saveService.Save(_meta);
                GameSession.Current.PlayerLingyu = _meta.lingyu;
            }
            OnMetaChanged?.Invoke();
        }
    }
}
