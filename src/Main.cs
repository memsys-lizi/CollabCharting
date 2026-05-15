using System;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace CollabCharting
{
    public static class Main
    {
        private static Harmony? harmony;
        private static bool enabled;

        public static UnityModManager.ModEntry? Mod { get; private set; }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            ModDependencyResolver.Install(modEntry.Path);
            Settings = Settings.Load(modEntry);

            CollabToastOverlay.Ensure();
            CollabSyncBlockingOverlay.Ensure();
            CollabRuntime.Initialize();
            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            modEntry.OnToggle = OnToggle;
            modEntry.OnUpdate = OnUpdate;
            modEntry.OnUnload = OnUnload;
            modEntry.OnGUI = Settings.OnGUI;
            modEntry.OnSaveGUI = Settings.OnSaveGUI;

            modEntry.Logger.Log("Collab Charting loaded with UMM panel controls.");
            return true;
        }

        public static Settings Settings { get; private set; } = null!;

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            enabled = value;
            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry modEntry, float dt)
        {
            if (!enabled)
            {
                return;
            }

            MainThreadDispatcher.Pump();
            Settings.TickAuth(dt);
            CollabRuntime.Update(dt);
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            CollabRuntime.Shutdown();
            harmony?.UnpatchAll(modEntry.Info.Id);
            harmony = null;
            return true;
        }
    }
}
