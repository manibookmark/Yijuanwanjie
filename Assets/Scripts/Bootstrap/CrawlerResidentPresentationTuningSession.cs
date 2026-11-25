using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// F7 中每名驻场英灵专属表现参数的会话所有者。它按稳定
    /// crawlerContentId 解析资产，隐藏切换英灵、保存基线和退出 Play
    /// 时回滚未保存值的细节。面板只读写当前偏移。
    /// </summary>
    internal sealed class CrawlerResidentPresentationTuningSession
    {
        private sealed class Entry
        {
            public CrawlerResidentPresentationProfile Profile;
            public Vector2 BaselineAuraOffset;
        }

        private readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private Entry _current;

        public bool HasCurrent => _current?.Profile != null;

        public CrawlerResidentPresentationProfile CurrentProfile =>
            _current?.Profile;

        public Vector2 AuraOffset => HasCurrent
            ? _current.Profile.hoverAuraOffset
            : Vector2.zero;

        public bool Select(string crawlerContentId)
        {
            _current = null;
            if (string.IsNullOrWhiteSpace(crawlerContentId) ||
                !CrawlerResidentPresentationCatalog.TryGet(
                    crawlerContentId,
                    out CrawlerResidentPresentationProfile profile))
            {
                return false;
            }

            if (!_entries.TryGetValue(crawlerContentId, out Entry entry))
            {
                entry = new Entry
                {
                    Profile = profile,
                    BaselineAuraOffset = profile.hoverAuraOffset
                };
                _entries.Add(crawlerContentId, entry);
            }

            _current = entry;
            return true;
        }

        public void SetAuraOffsetX(float value)
        {
            if (!HasCurrent)
                return;

            Vector2 offset = _current.Profile.hoverAuraOffset;
            offset.x = ClampOffset(value);
            _current.Profile.hoverAuraOffset = offset;
        }

        public void SetAuraOffsetY(float value)
        {
            if (!HasCurrent)
                return;

            Vector2 offset = _current.Profile.hoverAuraOffset;
            offset.y = ClampOffset(value);
            _current.Profile.hoverAuraOffset = offset;
        }

        public void RestoreCurrent()
        {
            if (HasCurrent)
            {
                _current.Profile.hoverAuraOffset =
                    _current.BaselineAuraOffset;
            }
        }

        public VfxProfileSaveResult SaveCurrent()
        {
            if (!HasCurrent)
            {
                return VfxProfileSaveResult.Failed(
                    "当前英灵没有可保存的驻场表现资产。");
            }

            VfxProfileSaveResult result =
                VfxTestProfileSavePort.SaveAsset(_current.Profile);
            if (result.Success)
            {
                _current.BaselineAuraOffset =
                    _current.Profile.hoverAuraOffset;
            }
            return result;
        }

        public void RestoreAll()
        {
            foreach (Entry entry in _entries.Values)
            {
                if (entry?.Profile != null)
                {
                    entry.Profile.hoverAuraOffset =
                        entry.BaselineAuraOffset;
                }
            }
        }

        private static float ClampOffset(float value)
        {
            return Mathf.Clamp(
                value,
                CrawlerResidentPresentationProfile.MinHoverAuraOffset,
                CrawlerResidentPresentationProfile.MaxHoverAuraOffset);
        }
    }
}
