using System;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace CollabCharting
{
    internal static class RelayHttpClient
    {
        public static CollabAuthStart StartAuth()
        {
            return GetJson<CollabAuthStart>("/api/auth/start", string.Empty);
        }

        public static CollabAuthPoll PollAuth(string loginId)
        {
            return GetJson<CollabAuthPoll>("/api/auth/poll?loginId=" + Uri.EscapeDataString(loginId), string.Empty);
        }

        public static void UploadResource(string roomId, string relayToken, ResourceManifestEntry file, string fullPath)
        {
            byte[] bytes = File.ReadAllBytes(fullPath);
            HttpWebRequest request = CreateRequest(
                $"/api/rooms/{Uri.EscapeDataString(roomId)}/resources/{Uri.EscapeDataString(file.Sha256)}",
                relayToken);
            request.Method = "PUT";
            request.ContentType = "application/octet-stream";
            request.ContentLength = bytes.Length;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                if ((int)response.StatusCode >= 300)
                {
                    throw new InvalidOperationException($"上传资源失败：{response.StatusCode}");
                }
            }
        }

        public static byte[] DownloadResource(string roomId, string relayToken, ResourceManifestEntry file)
        {
            HttpWebRequest request = CreateRequest(
                $"/api/rooms/{Uri.EscapeDataString(roomId)}/resources/{Uri.EscapeDataString(file.Sha256)}",
                relayToken);
            request.Method = "GET";
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                byte[] bytes = memory.ToArray();
                string actualHash = ResourceSync.ComputeSha256(bytes);
                if (bytes.LongLength != file.Size ||
                    !string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"资源校验失败：{file.RelativePath}");
                }

                return bytes;
            }
        }

        private static T GetJson<T>(string pathAndQuery, string relayToken)
        {
            HttpWebRequest request = CreateRequest(pathAndQuery, relayToken);
            request.Method = "GET";
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string json = reader.ReadToEnd();
                return JsonConvert.DeserializeObject<T>(json) ?? throw new InvalidDataException("Relay response is empty.");
            }
        }

        private static HttpWebRequest CreateRequest(string pathAndQuery, string relayToken)
        {
            var request = (HttpWebRequest)WebRequest.Create(RelayConfig.ServerBaseUrl.TrimEnd('/') + pathAndQuery);
            request.Timeout = 15000;
            request.ReadWriteTimeout = 30000;
            if (!string.IsNullOrWhiteSpace(relayToken))
            {
                request.Headers[HttpRequestHeader.Authorization] = "Bearer " + relayToken;
            }

            return request;
        }
    }
}
