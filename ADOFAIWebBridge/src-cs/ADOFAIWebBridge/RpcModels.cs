using Newtonsoft.Json.Linq;

namespace ADOFAIWebBridge
{
    internal sealed class RpcRequest
    {
        public JToken? Id { get; set; }

        public string? Method { get; set; }

        public JToken? Params { get; set; }
    }

    internal sealed class RpcError
    {
        public string Code { get; set; } = "error";

        public string Message { get; set; } = string.Empty;
    }
}
