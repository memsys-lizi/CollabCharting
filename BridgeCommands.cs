using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CollabCharting
{
    internal static class BridgeCommands
    {
        public static object GetBridgeInfo(object? bridge)
        {
            object? options = bridge?.GetType().GetProperty("Options")?.GetValue(bridge);
            return new
            {
                modId = options?.GetType().GetProperty("ModId")?.GetValue(options),
                displayName = options?.GetType().GetProperty("DisplayName")?.GetValue(options),
                mode = options?.GetType().GetProperty("Mode")?.GetValue(options)?.ToString(),
                port = bridge?.GetType().GetProperty("Port")?.GetValue(bridge)
            };
        }

        public static object GetStatus()
        {
            return new
            {
                mod = "Collab Charting",
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                frame = Time.frameCount,
                time = Time.time,
                ready = true
            };
        }

        public static object Echo(JToken? parameters)
        {
            string message = parameters?["message"]?.ToString() ?? string.Empty;
            return new
            {
                message,
                receivedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            };
        }

        public static object CreateMessageEvent(JToken? parameters)
        {
            string message = parameters?["message"]?.ToString() ?? "Hello from C#";
            return new
            {
                mod = "Collab Charting",
                message,
                frame = Time.frameCount,
                sentAt = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            };
        }

        public static string CreateSampleSvg()
        {
            return @"<svg xmlns=""http://www.w3.org/2000/svg"" width=""640"" height=""360"" viewBox=""0 0 640 360"">
  <rect width=""640"" height=""360"" fill=""#101923""/>
  <circle cx=""170"" cy=""180"" r=""88"" fill=""#2e8b57""/>
  <circle cx=""470"" cy=""180"" r=""88"" fill=""#2a4365""/>
  <path d=""M170 180 C260 70 380 290 470 180"" fill=""none"" stroke=""#f7fbff"" stroke-width=""18"" stroke-linecap=""round""/>
  <text x=""320"" y=""310"" text-anchor=""middle"" font-family=""Segoe UI, sans-serif"" font-size=""30"" fill=""#f7fbff"">Collab Charting Resource</text>
</svg>";
        }
    }
}
