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

        public static object GetProjectStatus()
        {
            return new
            {
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

        public static object CreateDemoEvent(JToken? parameters)
        {
            string message = parameters?["message"]?.ToString() ?? "Hello from C#";
            return new
            {
                message,
                frame = Time.frameCount,
                sentAt = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            };
        }
    }
}
