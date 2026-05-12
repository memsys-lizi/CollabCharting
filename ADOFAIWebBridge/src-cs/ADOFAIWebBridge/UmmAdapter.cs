using System;
using System.Reflection;

namespace ADOFAIWebBridge
{
    internal static class UmmAdapter
    {
        public static string? GetModPath(object modEntry)
        {
            return GetStringProperty(modEntry, "Path");
        }

        public static string? GetModId(object modEntry)
        {
            object? info = GetProperty(modEntry, "Info");
            return info == null ? null : GetStringProperty(info, "Id");
        }

        public static Action<string>? CreateLogger(object modEntry)
        {
            object? logger = GetProperty(modEntry, "Logger");
            if (logger == null)
            {
                return null;
            }

            MethodInfo? log = logger.GetType().GetMethod("Log", new[] { typeof(string) });
            if (log == null)
            {
                return null;
            }

            return message => log.Invoke(logger, new object[] { message });
        }

        private static object? GetProperty(object target, string name)
        {
            return target.GetType().GetProperty(name)?.GetValue(target);
        }

        private static string? GetStringProperty(object target, string name)
        {
            return GetProperty(target, name) as string;
        }
    }
}
