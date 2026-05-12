namespace ADOFAIWebBridge
{
    public sealed class WebCommandContext
    {
        public WebBridge Bridge { get; }

        public string RequestId { get; }

        public string Method { get; }

        internal WebCommandContext(WebBridge bridge, string requestId, string method)
        {
            Bridge = bridge;
            RequestId = requestId;
            Method = method;
        }
    }
}
