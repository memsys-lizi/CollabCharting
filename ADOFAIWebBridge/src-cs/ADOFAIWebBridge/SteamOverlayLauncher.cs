using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace ADOFAIWebBridge
{
    internal static class SteamOverlayLauncher
    {
        public static bool TryOpen(string url, Action<string>? log = null)
        {
            try
            {
                Assembly? assembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => a.GetType("Steamworks.SteamFriends") != null);

                Type? friendsType = assembly?.GetType("Steamworks.SteamFriends");
                Type? modeType = assembly?.GetType("Steamworks.EActivateGameOverlayToWebPageMode");

                if (friendsType == null || modeType == null)
                {
                    return false;
                }

                MethodInfo? method = friendsType.GetMethod(
                    "ActivateGameOverlayToWebPage",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), modeType },
                    null);

                if (method == null)
                {
                    return false;
                }

                object mode = Enum.Parse(modeType, "k_EActivateGameOverlayToWebPageMode_Default");
                method.Invoke(null, new[] { url, mode });
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Steam Overlay open failed: {ex.Message}");
                return false;
            }
        }

        public static void OpenInSystemBrowser(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
