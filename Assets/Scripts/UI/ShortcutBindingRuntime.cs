using UnityEngine;
using WanjieScroll.Persistence;

namespace WanjieScroll.UI
{
    public enum ShortcutAction
    {
        EndTurn = 0
    }

    /// <summary>
    /// 首版正式键盘快捷键的运行时解析器。消费者只查询语义动作，
    /// 不再保存默认 KeyCode；系统确认/返回键不属于本运行时。
    /// </summary>
    public static class ShortcutBindingRuntime
    {
        public const KeyCode RecommendedEndTurnKey =
            KeyboardShortcutSettings.RecommendedEndTurnKey;

        private static KeyCode _endTurnKey = RecommendedEndTurnKey;

        public static KeyCode GetBinding(ShortcutAction action)
        {
            return action == ShortcutAction.EndTurn
                ? _endTurnKey
                : KeyCode.None;
        }

        public static void Apply(KeyCode endTurnKey)
        {
            _endTurnKey = TryValidate(endTurnKey, out _)
                ? endTurnKey
                : RecommendedEndTurnKey;
        }

        public static bool WasPressed(ShortcutAction action)
        {
            KeyCode key = GetBinding(action);
            return key != KeyCode.None && Input.GetKeyDown(key);
        }

        public static bool TryValidate(KeyCode candidate, out string error)
        {
            return KeyboardShortcutSettings.TryValidate(candidate, out error);
        }

        public static string FormatKey(KeyCode key)
        {
            int code = (int)key;
            // 字母、数字与功能键保留真实键帽；其他按键不显示引擎枚举名。
            if (code >= (int)KeyCode.A && code <= (int)KeyCode.Z)
                return ((char)('A' + code - (int)KeyCode.A)).ToString();
            if (code >= (int)KeyCode.Alpha0 && code <= (int)KeyCode.Alpha9)
                return (code - (int)KeyCode.Alpha0).ToString();
            if (code >= (int)KeyCode.Keypad0 && code <= (int)KeyCode.Keypad9)
                return "小键盘 " + (code - (int)KeyCode.Keypad0);
            if (code >= (int)KeyCode.F1 && code <= (int)KeyCode.F15)
                return "F" + (code - (int)KeyCode.F1 + 1);

            return key switch
            {
                KeyCode.None => "未设置",
                KeyCode.Backspace => "退格",
                KeyCode.Tab => "制表",
                KeyCode.Clear => "清除",
                KeyCode.Space => "空格",
                KeyCode.Return => "回车",
                KeyCode.KeypadEnter => "小键盘回车",
                KeyCode.Escape => "退出",
                KeyCode.Pause => "暂停",
                KeyCode.Delete => "删除",
                KeyCode.Insert => "插入",
                KeyCode.Home => "行首",
                KeyCode.End => "行尾",
                KeyCode.PageUp => "上翻页",
                KeyCode.PageDown => "下翻页",
                KeyCode.LeftArrow => "←",
                KeyCode.RightArrow => "→",
                KeyCode.UpArrow => "↑",
                KeyCode.DownArrow => "↓",
                KeyCode.Numlock => "数字锁",
                KeyCode.CapsLock => "大写锁",
                KeyCode.ScrollLock => "滚动锁",
                KeyCode.LeftShift => "左换档",
                KeyCode.RightShift => "右换档",
                KeyCode.LeftControl => "左控制",
                KeyCode.RightControl => "右控制",
                KeyCode.LeftAlt => "左替换",
                KeyCode.RightAlt => "右替换",
                KeyCode.LeftCommand => "左系统",
                KeyCode.RightCommand => "右系统",
                KeyCode.LeftWindows => "左系统",
                KeyCode.RightWindows => "右系统",
                KeyCode.AltGr => "组合换档",
                KeyCode.Help => "帮助",
                KeyCode.Print => "打印",
                KeyCode.SysReq => "系统请求",
                KeyCode.Break => "中断",
                KeyCode.Menu => "菜单",
                KeyCode.KeypadPeriod => "小键盘 .",
                KeyCode.KeypadDivide => "小键盘 /",
                KeyCode.KeypadMultiply => "小键盘 *",
                KeyCode.KeypadMinus => "小键盘 -",
                KeyCode.KeypadPlus => "小键盘 +",
                KeyCode.KeypadEquals => "小键盘 =",
                _ when code >= 33 && code <= 126 => ((char)code).ToString(),
                _ => "其他按键"
            };
        }
    }
}
