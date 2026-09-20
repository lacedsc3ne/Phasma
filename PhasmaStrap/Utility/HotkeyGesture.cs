using System.Windows.Input;

namespace PhasmaStrap.Utility
{
    public static class HotkeyGesture
    {
        public static string Format(ModifierKeys modifiers, Key key)
        {
            var parts = new List<string>(5);

            if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");

            parts.Add(KeyName(key));
            return string.Join("+", parts);
        }

        public static bool TryParse(string? text, out ModifierKeys modifiers, out Key key)
        {
            modifiers = ModifierKeys.None;
            key = Key.None;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string[] tokens = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
                return false;

            for (int i = 0; i < tokens.Length - 1; i++)
            {
                switch (tokens[i].ToLowerInvariant())
                {
                    case "ctrl": case "control": modifiers |= ModifierKeys.Control; break;
                    case "alt": modifiers |= ModifierKeys.Alt; break;
                    case "shift": modifiers |= ModifierKeys.Shift; break;
                    case "win": case "windows": modifiers |= ModifierKeys.Windows; break;
                    default: return false;
                }
            }

            return TryParseKey(tokens[^1], out key) && key != Key.None;
        }

        public static bool TryFromVirtualKey(ModifierKeys modifiers, int virtualKey, out string text)
        {
            text = "";

            Key key = KeyInterop.KeyFromVirtualKey(virtualKey);
            if (key == Key.None || IsModifierKey(key))
                return false;

            text = Format(modifiers, key);

            return TryParse(text, out ModifierKeys parsedModifiers, out Key parsedKey) && parsedModifiers == modifiers && parsedKey == key;
        }

        public static bool IsTypingGesture(ModifierKeys modifiers, Key key)
        {
            if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
                return false;

            return (key >= Key.A && key <= Key.Z)
                || (key >= Key.D0 && key <= Key.D9)
                || (key >= Key.NumPad0 && key <= Key.NumPad9)
                || key is Key.Space or Key.Return or Key.Back or Key.Tab
                    or Key.Multiply or Key.Add or Key.Subtract or Key.Divide or Key.Decimal
                    or Key.OemPlus or Key.OemMinus or Key.OemComma or Key.OemPeriod or Key.OemQuestion or Key.OemTilde
                    or Key.OemOpenBrackets or Key.OemCloseBrackets or Key.OemPipe or Key.OemSemicolon or Key.OemQuotes
                    or Key.Oem8 or Key.OemBackslash;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint MapVirtualKeyW(uint code, uint mapType);

        public static string ToDisplay(string? text)
        {
            if (!TryParse(text, out ModifierKeys modifiers, out Key key))
                return text ?? "";

            bool punctuation = key is Key.OemPlus or Key.OemMinus or Key.OemComma or Key.OemPeriod or Key.OemQuestion or Key.OemTilde
                or Key.OemOpenBrackets or Key.OemCloseBrackets or Key.OemPipe or Key.OemSemicolon or Key.OemQuotes or Key.Oem8 or Key.OemBackslash;

            if (!punctuation)
                return text!;

            uint mapped = MapVirtualKeyW((uint)KeyInterop.VirtualKeyFromKey(key), 2) & 0x7FFFFFFF;
            if (mapped < 0x21 || mapped > 0xFFFF)
                return text!;

            string prefix = Format(modifiers, Key.A);
            return prefix[..^1] + char.ToUpperInvariant((char)mapped);
        }

        public static bool IsModifierKey(Key key) =>
            key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

        private static string KeyName(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9)
                return ((int)(key - Key.D0)).ToString();

            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return "Num" + (int)(key - Key.NumPad0);

            return key switch
            {
                Key.OemPlus => "=",
                Key.OemMinus => "-",
                Key.OemComma => ",",
                Key.OemPeriod => ".",
                Key.OemQuestion => "/",
                Key.OemTilde => "`",
                Key.OemOpenBrackets => "[",
                Key.OemCloseBrackets => "]",
                Key.OemPipe => "\\",
                Key.OemSemicolon => ";",
                Key.OemQuotes => "'",
                Key.Return => "Enter",
                Key.Back => "Backspace",
                Key.Prior => "PageUp",
                Key.Next => "PageDown",
                Key.Snapshot => "PrintScreen",
                Key.Capital => "CapsLock",
                Key.Scroll => "ScrollLock",
                _ => key.ToString(),
            };
        }

        private static bool TryParseKey(string token, out Key key)
        {
            key = Key.None;
            string t = token.Trim();

            if (t.Length == 1 && char.IsDigit(t[0]))
            {
                key = Key.D0 + (t[0] - '0');
                return true;
            }

            if (t.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && t.Length == 4 && char.IsDigit(t[3]))
            {
                key = Key.NumPad0 + (t[3] - '0');
                return true;
            }

            switch (t)
            {
                case "=": key = Key.OemPlus; return true;
                case "-": key = Key.OemMinus; return true;
                case ",": key = Key.OemComma; return true;
                case ".": key = Key.OemPeriod; return true;
                case "/": key = Key.OemQuestion; return true;
                case "`": key = Key.OemTilde; return true;
                case "[": key = Key.OemOpenBrackets; return true;
                case "]": key = Key.OemCloseBrackets; return true;
                case "\\": key = Key.OemPipe; return true;
                case ";": key = Key.OemSemicolon; return true;
                case "'": key = Key.OemQuotes; return true;
            }

            switch (t.ToLowerInvariant())
            {
                case "enter": key = Key.Return; return true;
                case "backspace": key = Key.Back; return true;
                case "pageup": key = Key.Prior; return true;
                case "pagedown": key = Key.Next; return true;
                case "printscreen": key = Key.Snapshot; return true;
                case "capslock": key = Key.Capital; return true;
                case "scrolllock": key = Key.Scroll; return true;
                case "esc": key = Key.Escape; return true;
            }

            return Enum.TryParse(t, true, out key);
        }
    }
}
