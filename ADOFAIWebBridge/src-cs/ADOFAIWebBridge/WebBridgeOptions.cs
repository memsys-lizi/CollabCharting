using System;
using System.IO;

namespace ADOFAIWebBridge
{
    public sealed class WebBridgeOptions
    {
        public string ModId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string WebRoot { get; set; } = "webui/dist";

        public int PreferredPort { get; set; }

        public string DevServerUrl { get; set; } = "http://127.0.0.1:5173/";

        public BridgeMode Mode { get; set; } = BridgeMode.Development;

        public string OpenKey { get; set; } = "F8";

        public bool UseSteamOverlay { get; set; } = true;

        public bool RequireToken { get; set; } = true;

        public string Host { get; set; } = "127.0.0.1";

        public string? PublicHostName { get; set; }

        public int PortProbeCount { get; set; } = 32;

        internal WebBridgeOptions Normalize(string? modPath)
        {
            if (string.IsNullOrWhiteSpace(ModId))
            {
                throw new WebBridgeException("invalid_options", "ModId is required.");
            }

            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                DisplayName = ModId;
            }

            if (PreferredPort <= 0)
            {
                PreferredPort = PortAllocator.GetStablePort(ModId);
            }

            if (PortProbeCount <= 0)
            {
                PortProbeCount = 32;
            }

            if (!string.IsNullOrWhiteSpace(modPath) && !Path.IsPathRooted(WebRoot))
            {
                WebRoot = Path.GetFullPath(Path.Combine(modPath, WebRoot));
            }

            DevServerUrl = EnsureTrailingSlash(DevServerUrl);
            return this;
        }

        private static string EnsureTrailingSlash(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
        }
    }
}
