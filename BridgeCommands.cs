using Newtonsoft.Json.Linq;

namespace CollabCharting
{
    internal static class BridgeCommands
    {
        public static object SayHello(JToken? parameters)
        {
            string name = parameters?["name"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "匿名制谱师";
            }

            return new
            {
                message = $"你好，{name}"
            };
        }
    }
}
