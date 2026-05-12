using System;

namespace ADOFAIWebBridge
{
    public sealed class WebBridgeException : Exception
    {
        public string Code { get; }

        public WebBridgeException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public WebBridgeException(string code, string message, Exception innerException)
            : base(message, innerException)
        {
            Code = code;
        }
    }
}
