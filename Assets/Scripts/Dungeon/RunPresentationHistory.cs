using System;
using System.Collections.Generic;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// 当前关卡 Run 内角色演出历史的唯一所有者。演出先登记为活动，只有自然完成后才记为已看；
    /// 场景销毁或中断会取消活动登记，避免把未播放完的演出误判为已完成。
    /// </summary>
    public sealed class RunPresentationHistory
    {
        private readonly HashSet<string> _activeIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _completedIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool TryBegin(string presentationContentId, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(presentationContentId))
            {
                error = "角色演出 contentId 不能为空。";
                return false;
            }
            if (_completedIds.Contains(presentationContentId))
            {
                error = $"角色演出已在当前 Run 完成：{presentationContentId}。";
                return false;
            }
            if (!_activeIds.Add(presentationContentId))
            {
                error = $"角色演出正在播放：{presentationContentId}。";
                return false;
            }
            return true;
        }

        public bool Complete(string presentationContentId)
        {
            if (string.IsNullOrWhiteSpace(presentationContentId) ||
                !_activeIds.Remove(presentationContentId))
                return false;

            _completedIds.Add(presentationContentId);
            return true;
        }

        public void Cancel(string presentationContentId)
        {
            if (!string.IsNullOrWhiteSpace(presentationContentId))
                _activeIds.Remove(presentationContentId);
        }

        public bool HasCompleted(string presentationContentId)
        {
            return !string.IsNullOrWhiteSpace(presentationContentId) &&
                   _completedIds.Contains(presentationContentId);
        }

        internal List<string> CaptureCompletedIds()
        {
            var result = new List<string>(_completedIds);
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        internal void RestoreCompletedIds(IEnumerable<string> contentIds)
        {
            _activeIds.Clear();
            _completedIds.Clear();
            if (contentIds == null)
                return;
            foreach (string contentId in contentIds)
            {
                if (!string.IsNullOrWhiteSpace(contentId))
                    _completedIds.Add(contentId);
            }
        }
    }
}
