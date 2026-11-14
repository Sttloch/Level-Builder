using UnityEngine;
#if LEVELBUILDER_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LevelBuilder
{
    /// <summary>
    /// Interact-key query that works with the legacy Input Manager and/or the new Input System.
    /// With only the new Input System active, <see cref="KeyCode"/>s outside A–Z, 0–9, Space and Return are not
    /// mapped (they never trigger), and the gamepad West button (X / Square) always triggers Interact, whatever the key.
    /// </summary>
    public static class DoorInput
    {
        public static bool InteractPressed(KeyCode key)
        {
            bool pressed = false;
#if LEVELBUILDER_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && TryMap(key, out var k) && kb[k].wasPressedThisFrame) pressed = true;
            var pad = Gamepad.current;
            if (pad != null && pad.buttonWest.wasPressedThisFrame) pressed = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(key)) pressed = true;
#endif
            return pressed;
        }

#if LEVELBUILDER_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        static bool TryMap(KeyCode code, out Key key)
        {
            key = Key.None;
            if (code >= KeyCode.A && code <= KeyCode.Z) { key = Key.A + (code - KeyCode.A); return true; }
            if (code >= KeyCode.Alpha1 && code <= KeyCode.Alpha9) { key = Key.Digit1 + (code - KeyCode.Alpha1); return true; }
            switch (code)
            {
                case KeyCode.Alpha0: key = Key.Digit0; return true;
                case KeyCode.Space: key = Key.Space; return true;
                case KeyCode.Return: key = Key.Enter; return true;
            }
            return false;
        }
#endif
    }
}
