using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace Erenshor.XTarget
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class XTargetPlugin : BaseUnityPlugin
    {
        // ─────────────────────────────────────────────────────────────────────
        // Plugin metadata
        // ─────────────────────────────────────────────────────────────────────
        internal static class PluginInfo
        {
            public const string PLUGIN_GUID    = "com.erenshor.xtarget";
            public const string PLUGIN_NAME    = "Extended Target Window";
            public const string PLUGIN_VERSION = "1.0.0";
        }

        // ─────────────────────────────────────────────────────────────────────
        // Singleton
        // ─────────────────────────────────────────────────────────────────────
        internal static XTargetPlugin Instance { get; private set; }
        internal static ManualLogSource Log     { get; private set; }

        // ─────────────────────────────────────────────────────────────────────
        // Config entries
        // ─────────────────────────────────────────────────────────────────────
        internal static ConfigEntry<KeyCode> ToggleKey;
        internal static ConfigEntry<float>   WindowX;
        internal static ConfigEntry<float>   WindowY;
        internal static ConfigEntry<int>     MaxSlots;
        internal static ConfigEntry<bool>    Locked;
        internal static ConfigEntry<bool>    AutoHide;

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────
        private void Awake()
        {
            Instance = this;
            Log      = base.Logger;

            BindConfig();
            XTargetUI.Initialize();

            Log.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Config binding
        // ─────────────────────────────────────────────────────────────────────
        private void BindConfig()
        {
            ToggleKey = Config.Bind(
                "General", "ToggleKey", KeyCode.F11,
                "Key to show / hide the Extended Target Window");

            MaxSlots = Config.Bind(
                "General", "MaxSlots", 8,
                "Maximum number of enemy slots to display (1–20)");

            WindowX = Config.Bind(
                "Window", "PositionX", 10f,
                "Saved X position of the window");

            WindowY = Config.Bind(
                "Window", "PositionY", -10f,
                "Saved Y position of the window (negative = from top)");

            Locked = Config.Bind(
                "Window", "Locked", false,
                "Whether the window position is locked");

            AutoHide = Config.Bind(
                "Window", "AutoHide", true,
                "Hide window chrome when nothing has aggro on you or your group");
        }
    }
}