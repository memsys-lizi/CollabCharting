using System;

namespace ADOFAIWebBridge
{
    internal sealed class ExposedResource
    {
        public string Id { get; set; } = string.Empty;

        public string ContentType { get; set; } = "application/octet-stream";

        public string? FilePath { get; set; }

        public byte[]? Bytes { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }
    }
}
