using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    /// <summary>
    /// Shortcuts must work whether or not a UI element holds focus (clicking the 3D scene clears it).
    /// Each HUD polls the keyboard every frame and also forwards UI Toolkit key events; both paths go
    /// through <see cref="Dispatch"/>, which runs the HUD's own handler at most once per key per frame.
    /// </summary>
    internal static class HotkeyRelay
    {
        private sealed class Memo { public int Frame = -1; public readonly HashSet<KeyCode> Keys = new HashSet<KeyCode>(); }
        private static readonly ConditionalWeakTable<VisualElement, Memo> Memos = new ConditionalWeakTable<VisualElement, Memo>();
        private static readonly (Key key, KeyCode code)[] Shortcuts =
        {
            (Key.Escape, KeyCode.Escape), (Key.M, KeyCode.M), (Key.B, KeyCode.B), (Key.J, KeyCode.J),
            (Key.S, KeyCode.S), (Key.D, KeyCode.D)
        };

        public static bool Dispatch(VisualElement root, KeyCode key, Func<KeyCode, bool> handle)
        {
            if (GameSceneLoader.IsLoading) return true;
            var memo = Memos.GetOrCreateValue(root);
            if (memo.Frame != Time.frameCount) { memo.Frame = Time.frameCount; memo.Keys.Clear(); }
            if (!memo.Keys.Add(key)) return true;
            return handle(key);
        }

        public static void Poll(VisualElement root, Func<KeyCode, bool> handle)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || root?.panel == null) return;
            // Typing into a text field must not trigger shortcuts.
            if (root.panel.focusController.focusedElement is TextField) return;
            foreach (var shortcut in Shortcuts)
                if (keyboard[shortcut.key].wasPressedThisFrame) Dispatch(root, shortcut.code, handle);
        }
    }
}
