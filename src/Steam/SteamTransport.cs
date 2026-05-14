using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Steamworks;

namespace CollabCharting
{
    internal sealed class SteamTransport : IDisposable
    {
        private const int Channel = 7;
        private const int MaxChunkBytes = 45000;
        private readonly Dictionary<string, ChunkBuffer> chunkBuffers = new Dictionary<string, ChunkBuffer>();
        private Callback<P2PSessionRequest_t>? sessionRequest;
        private Callback<P2PSessionConnectFail_t>? sessionFail;

        public void Start()
        {
            if (!SteamIntegration.initialized || sessionRequest != null)
            {
                return;
            }

            SteamNetworking.AllowP2PPacketRelay(true);
            sessionRequest = Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
            sessionFail = Callback<P2PSessionConnectFail_t>.Create(OnSessionFail);
        }

        public void Send(CSteamID peer, string type, object payload, int revision)
        {
            var envelope = new NetEnvelope
            {
                Type = type,
                Sender = SteamUser.GetSteamID().m_SteamID.ToString(),
                Revision = revision,
                Payload = payload == null ? null : JToken.FromObject(payload)
            };

            string json = JsonConvert.SerializeObject(envelope);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            string messageId = Guid.NewGuid().ToString("N");
            int total = Math.Max(1, (int)Math.Ceiling(bytes.Length / (double)MaxChunkBytes));

            for (int i = 0; i < total; i++)
            {
                int count = Math.Min(MaxChunkBytes, bytes.Length - i * MaxChunkBytes);
                byte[] chunk = new byte[count];
                Buffer.BlockCopy(bytes, i * MaxChunkBytes, chunk, 0, count);

                var packet = new TransportPacket
                {
                    MessageId = messageId,
                    Index = i,
                    Total = total,
                    Payload = Convert.ToBase64String(chunk)
                };

                byte[] packetBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(packet));
                SteamNetworking.SendP2PPacket(peer, packetBytes, (uint)packetBytes.Length, EP2PSend.k_EP2PSendReliable, Channel);
            }
        }

        public List<NetEnvelope> Poll()
        {
            var messages = new List<NetEnvelope>();
            if (!SteamIntegration.initialized)
            {
                return messages;
            }

            while (SteamNetworking.IsP2PPacketAvailable(out uint packetSize, Channel))
            {
                byte[] buffer = new byte[packetSize];
                if (!SteamNetworking.ReadP2PPacket(buffer, packetSize, out uint bytesRead, out CSteamID sender, Channel))
                {
                    continue;
                }

                string raw = Encoding.UTF8.GetString(buffer, 0, (int)bytesRead);
                TransportPacket? packet = JsonConvert.DeserializeObject<TransportPacket>(raw);
                if (packet == null || string.IsNullOrEmpty(packet.MessageId))
                {
                    continue;
                }

                string key = sender.m_SteamID + ":" + packet.MessageId;
                if (!chunkBuffers.TryGetValue(key, out ChunkBuffer chunkBuffer))
                {
                    chunkBuffer = new ChunkBuffer(packet.Total);
                    chunkBuffers[key] = chunkBuffer;
                }

                chunkBuffer.Add(packet.Index, Convert.FromBase64String(packet.Payload));
                if (!chunkBuffer.Complete)
                {
                    continue;
                }

                chunkBuffers.Remove(key);
                string json = Encoding.UTF8.GetString(chunkBuffer.Join());
                NetEnvelope? envelope = JsonConvert.DeserializeObject<NetEnvelope>(json);
                if (envelope != null)
                {
                    messages.Add(envelope);
                }
            }

            return messages;
        }

        public void Dispose()
        {
            sessionRequest?.Dispose();
            sessionFail?.Dispose();
            sessionRequest = null;
            sessionFail = null;
            chunkBuffers.Clear();
        }

        private static void OnSessionRequest(P2PSessionRequest_t request)
        {
            SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote);
        }

        private static void OnSessionFail(P2PSessionConnectFail_t fail)
        {
            Main.Mod?.Logger.Warning($"Steam P2P session failed: {fail.m_steamIDRemote} / {fail.m_eP2PSessionError}");
        }

        internal sealed class NetEnvelope
        {
            public string Type { get; set; } = string.Empty;

            public string Sender { get; set; } = string.Empty;

            public int Revision { get; set; }

            public JToken? Payload { get; set; }
        }

        private sealed class TransportPacket
        {
            public string MessageId { get; set; } = string.Empty;

            public int Index { get; set; }

            public int Total { get; set; }

            public string Payload { get; set; } = string.Empty;
        }

        private sealed class ChunkBuffer
        {
            private readonly byte[][] chunks;
            private int received;

            public ChunkBuffer(int total)
            {
                chunks = new byte[Math.Max(1, total)][];
            }

            public bool Complete => received == chunks.Length;

            public void Add(int index, byte[] bytes)
            {
                if (index < 0 || index >= chunks.Length || chunks[index] != null)
                {
                    return;
                }

                chunks[index] = bytes;
                received++;
            }

            public byte[] Join()
            {
                int size = 0;
                foreach (byte[] chunk in chunks)
                {
                    size += chunk.Length;
                }

                byte[] result = new byte[size];
                int offset = 0;
                foreach (byte[] chunk in chunks)
                {
                    Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                    offset += chunk.Length;
                }

                return result;
            }
        }
    }
}
