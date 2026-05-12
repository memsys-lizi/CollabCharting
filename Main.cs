using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityModManagerNet;

namespace CollabCharting
{
    public static class Main
    {
        private static object? bridge;
        private static Type? bridgeType;
        private static bool enabled;

        public static UnityModManager.ModEntry? Mod { get; private set; }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            ModDependencyResolver.Install(modEntry.Path);
            Settings = Settings.Load(modEntry);

            Assembly sdkAssembly = Assembly.LoadFrom(Path.Combine(modEntry.Path, "ADOFAIWebBridge.dll"));
            Type optionsType = sdkAssembly.GetType("ADOFAIWebBridge.WebBridgeOptions", true);
            Type modeType = sdkAssembly.GetType("ADOFAIWebBridge.BridgeMode", true);
            bridgeType = sdkAssembly.GetType("ADOFAIWebBridge.WebBridge", true);

            object options = Activator.CreateInstance(optionsType);
            Set(options, "ModId", "com.lk130.collab-charting");
            Set(options, "DisplayName", "Collab Charting");
            Set(options, "WebRoot", Path.Combine(modEntry.Path, "webui", "dist"));
            Set(options, "PreferredPort", Settings.Port);
            Set(options, "DevServerUrl", Settings.DevServerUrl);
            Set(options, "Mode", Enum.Parse(modeType, Settings.DevelopmentMode ? "Development" : "Production"));
            Set(options, "OpenKey", Settings.OpenKey.ToString());
            Set(options, "UseSteamOverlay", true);
            Set(options, "RequireToken", true);

            bridge = bridgeType
                .GetMethod("ForUMM", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new[] { modEntry, options });

            RegisterCommand("collabCharting.getBridgeInfo", parameters => BridgeCommands.GetBridgeInfo(bridge));
            RegisterCommand("collabCharting.emitMessage", parameters =>
            {
                InvokeBridge("Emit", "collabCharting.message", BridgeCommands.CreateMessageEvent(parameters));
                return new { ok = true };
            });
            RegisterCommand("collabCharting.getSampleImage", parameters =>
            {
                byte[] bytes = Encoding.UTF8.GetBytes(BridgeCommands.CreateSampleSvg());
                object? url = InvokeBridge("ExposeBytes", bytes, "image/svg+xml", TimeSpan.FromMinutes(5));
                return new { url };
            });
            RegisterCommand("collabCharting.getStatus", parameters => BridgeCommands.GetStatus());
            RegisterCommand("collabCharting.echo", BridgeCommands.Echo);
            InvokeBridge("Start");

            modEntry.OnToggle = OnToggle;
            modEntry.OnUpdate = OnUpdate;
            modEntry.OnGUI = Settings.OnGUI;
            modEntry.OnSaveGUI = Settings.OnSaveGUI;

            modEntry.Logger.Log($"Collab Charting bridge mode: {GetOptionValue("Mode")}");
            modEntry.Logger.Log($"Collab Charting web root: {GetOptionValue("WebRoot")}");
            modEntry.Logger.Log($"Collab Charting port: {GetBridgeValue("Port")}");
            return true;
        }

        public static Settings Settings { get; private set; } = null!;

        public static void OpenOverlay()
        {
            InvokeBridge("OpenSteamOverlay");
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            enabled = value;
            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry modEntry, float dt)
        {
            if (!enabled || bridge == null)
            {
                return;
            }

            if (Settings.EnableHotkey && Input.GetKeyDown(Settings.OpenKey))
            {
                OpenOverlay();
            }
        }

        private static void Set(object target, string property, object value)
        {
            target.GetType().GetProperty(property)?.SetValue(target, value);
        }

        private static object? InvokeBridge(string method, params object[] args)
        {
            if (bridge == null || bridgeType == null)
            {
                return null;
            }

            foreach (MethodInfo candidate in bridgeType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                ParameterInfo[] parameters = candidate.GetParameters();
                if (candidate.Name == method &&
                    parameters.Length == args.Length &&
                    ParametersMatch(parameters, args))
                {
                    return candidate.Invoke(bridge, args);
                }
            }

            throw new MissingMethodException(bridgeType.FullName, method);
        }

        private static void RegisterCommand(string name, Func<Newtonsoft.Json.Linq.JToken?, object> handler)
        {
            if (bridge == null || bridgeType == null)
            {
                return;
            }

            foreach (MethodInfo candidate in bridgeType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                ParameterInfo[] parameters = candidate.GetParameters();
                if (candidate.Name == "RegisterCommand" &&
                    parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(string) &&
                    parameters[1].ParameterType == typeof(Func<Newtonsoft.Json.Linq.JToken, object>))
                {
                    candidate.Invoke(bridge, new object[] { name, handler });
                    return;
                }
            }

            throw new MissingMethodException(bridgeType.FullName, "RegisterCommand");
        }

        private static bool ParametersMatch(ParameterInfo[] parameters, object[] args)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                if (args[i] == null)
                {
                    if (parameters[i].ParameterType.IsValueType)
                    {
                        return false;
                    }

                    continue;
                }

                if (!parameters[i].ParameterType.IsInstanceOfType(args[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static object? GetBridgeValue(string property)
        {
            return bridge?.GetType().GetProperty(property)?.GetValue(bridge);
        }

        private static object? GetOptionValue(string property)
        {
            object? options = GetBridgeValue("Options");
            return options?.GetType().GetProperty(property)?.GetValue(options);
        }
    }
}
