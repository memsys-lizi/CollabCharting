using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Steamworks;

namespace CollabCharting
{
    internal sealed class SteamTransport : IDisposable
    {
        private const int Channel = 7;
        private const int MaxJsonChunkBytes = 45000;
        private const int MaxBinaryChunkBytes = 40000;
        private const int MaxOutgoingPacketsPerPoll = 64;
        private const int BinaryHeaderSize = 12;
        private static readonly byte[] BinaryMagic = { (byte)'C', (byte)'C', (byte)'B', (byte)'2' };
        private readonly Dictionary<string, ChunkBuffer> chunkBuffers = new Dictionary<string, ChunkBuffer>();
        private readonly Dictionary<string, BinaryChunkBuffer> binaryChunkBuffers = new Dictionary<string, BinaryChunkBuffer>();
        private readonly Queue<OutgoingWorkItem> outgoingItems = new Queue<OutgoingWorkItem>();
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
            int total = Math.Max(1, (int)Math.Ceiling(bytes.Length / (double)MaxJsonChunkBytes));

            for (int i = 0; i < total; i++)
            {
                int count = Math.Min(MaxJsonChunkBytes, bytes.Length - i * MaxJsonChunkBytes);
                byte[] chunk = new byte[count];
                Buffer.BlockCopy(bytes, i * MaxJsonChunkBytes, chunk, 0, count);

                var packet = new TransportPacket
                {
                    MessageId = messageId,
                    Index = i,
                    Total = total,
                    Payload = Convert.ToBase64String(chunk)
                };

                byte[] packetBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(packet));
                EnqueuePacket(peer, packetBytes);
            }
        }

        public void SendBytes(CSteamID peer, string type, object payload, byte[] bytes, int revision)
        {
            bytes = bytes ?? Array.Empty<byte>();
            JToken? packetPayload = payload == null ? null : JToken.FromObject(payload);
            string messageId = Guid.NewGuid().ToString("N");
            outgoingItems.Enqueue(new OutgoingBinaryBytesTransfer(peer, type, packetPayload, bytes, revision, messageId));
        }

        public void SendFile(CSteamID peer, string type, object payload, string filePath, int revision)
        {
            JToken? packetPayload = payload == null ? null : JToken.FromObject(payload);
            string messageId = Guid.NewGuid().ToString("N");
            outgoingItems.Enqueue(new OutgoingBinaryFileTransfer(peer, type, packetPayload, filePath, revision, messageId));
        }

        public List<NetEnvelope> Poll()
        {
            var messages = new List<NetEnvelope>();
            if (!SteamIntegration.initialized)
            {
                return messages;
            }

            DrainOutgoingPackets();

            while (SteamNetworking.IsP2PPacketAvailable(out uint packetSize, Channel))
            {
                byte[] buffer = new byte[packetSize];
                if (!SteamNetworking.ReadP2PPacket(buffer, packetSize, out uint bytesRead, out CSteamID sender, Channel))
                {
                    continue;
                }

                int length = (int)bytesRead;
                if (TryReadBinaryPacket(buffer, length, sender, messages))
                {
                    continue;
                }

                TryReadJsonPacket(buffer, length, sender, messages);
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
            binaryChunkBuffers.Clear();
            foreach (OutgoingWorkItem item in outgoingItems)
            {
                item.Dispose();
            }

            outgoingItems.Clear();
        }

        private void EnqueuePacket(CSteamID peer, byte[] packetBytes)
        {
            if (!peer.IsValid() || packetBytes.Length == 0)
            {
                return;
            }

            outgoingItems.Enqueue(new OutgoingPacket(peer, packetBytes));
        }

        private void DrainOutgoingPackets()
        {
            int sent = 0;
            while (sent < MaxOutgoingPacketsPerPoll && outgoingItems.Count > 0)
            {
                OutgoingWorkItem item = outgoingItems.Peek();
                item.SendNext();
                sent++;

                if (!item.Complete)
                {
                    continue;
                }

                item.Dispose();
                outgoingItems.Dequeue();
            }
        }

        private bool TryReadBinaryPacket(byte[] buffer, int length, CSteamID sender, List<NetEnvelope> messages)
        {
            if (!HasBinaryMagic(buffer, length))
            {
                return false;
            }

            try
            {
                int metadataLength = BitConverter.ToInt32(buffer, 4);
                int chunkLength = BitConverter.ToInt32(buffer, 8);
                if (metadataLength < 0 ||
                    chunkLength < 0 ||
                    metadataLength > length - BinaryHeaderSize ||
                    chunkLength > length - BinaryHeaderSize - metadataLength)
                {
                    Main.Mod?.Logger.Warning("Rejected malformed binary Steam P2P packet.");
                    return true;
                }

                string metadataJson = Encoding.UTF8.GetString(buffer, BinaryHeaderSize, metadataLength);
                BinaryTransportPacket? packet = JsonConvert.DeserializeObject<BinaryTransportPacket>(metadataJson);
                if (packet == null ||
                    string.IsNullOrEmpty(packet.MessageId) ||
                    string.IsNullOrEmpty(packet.Type) ||
                    packet.Total <= 0 ||
                    packet.Index < 0 ||
                    packet.Index >= packet.Total)
                {
                    return true;
                }

                byte[] chunk = new byte[chunkLength];
                Buffer.BlockCopy(buffer, BinaryHeaderSize + metadataLength, chunk, 0, chunkLength);

                string key = sender.m_SteamID + ":" + packet.MessageId;
                if (!binaryChunkBuffers.TryGetValue(key, out BinaryChunkBuffer chunkBuffer))
                {
                    chunkBuffer = new BinaryChunkBuffer(packet, sender.m_SteamID.ToString());
                    binaryChunkBuffers[key] = chunkBuffer;
                }

                chunkBuffer.Add(packet.Index, chunk);
                if (!chunkBuffer.Complete)
                {
                    return true;
                }

                binaryChunkBuffers.Remove(key);
                messages.Add(new NetEnvelope
                {
                    Type = chunkBuffer.Type,
                    Sender = chunkBuffer.Sender,
                    Revision = chunkBuffer.Revision,
                    Payload = chunkBuffer.Payload,
                    BinaryPayload = chunkBuffer.Join()
                });
            }
            catch (Exception ex)
            {
                Main.Mod?.Logger.Warning($"Failed to read binary Steam P2P packet: {ex.Message}");
            }

            return true;
        }

        private void TryReadJsonPacket(byte[] buffer, int length, CSteamID sender, List<NetEnvelope> messages)
        {
            try
            {
                string raw = Encoding.UTF8.GetString(buffer, 0, length);
                TransportPacket? packet = JsonConvert.DeserializeObject<TransportPacket>(raw);
                if (packet == null || string.IsNullOrEmpty(packet.MessageId))
                {
                    return;
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
                    return;
                }

                chunkBuffers.Remove(key);
                string json = Encoding.UTF8.GetString(chunkBuffer.Join());
                NetEnvelope? envelope = JsonConvert.DeserializeObject<NetEnvelope>(json);
                if (envelope != null)
                {
                    messages.Add(envelope);
                }
            }
            catch (Exception ex)
            {
                Main.Mod?.Logger.Warning($"Failed to read JSON Steam P2P packet: {ex.Message}");
            }
        }

        private static byte[] CreateBinaryPacket(BinaryTransportPacket packet, byte[] payload, int offset, int count)
        {
            byte[] metadata = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(packet));
            byte[] result = new byte[BinaryHeaderSize + metadata.Length + count];
            Buffer.BlockCopy(BinaryMagic, 0, result, 0, BinaryMagic.Length);
            WriteInt32(result, 4, metadata.Length);
            WriteInt32(result, 8, count);
            Buffer.BlockCopy(metadata, 0, result, BinaryHeaderSize, metadata.Length);
            if (count > 0)
            {
                Buffer.BlockCopy(payload, offset, result, BinaryHeaderSize + metadata.Length, count);
            }

            return result;
        }

        private static void WriteInt32(byte[] target, int offset, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, target, offset, bytes.Length);
        }

        private static bool HasBinaryMagic(byte[] buffer, int length)
        {
            if (length < BinaryHeaderSize)
            {
                return false;
            }

            for (int i = 0; i < BinaryMagic.Length; i++)
            {
                if (buffer[i] != BinaryMagic[i])
                {
                    return false;
                }
            }

            return true;
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

            [JsonIgnore]
            public byte[]? BinaryPayload { get; set; }
        }

        private sealed class TransportPacket
        {
            public string MessageId { get; set; } = string.Empty;

            public int Index { get; set; }

            public int Total { get; set; }

            public string Payload { get; set; } = string.Empty;
        }

        private abstract class OutgoingWorkItem : IDisposable
        {
            protected OutgoingWorkItem(CSteamID peer)
            {
                Peer = peer;
            }

            protected CSteamID Peer { get; }

            public bool Complete { get; protected set; }

            public abstract bool SendNext();

            public virtual void Dispose()
            {
            }
        }

        private sealed class OutgoingPacket : OutgoingWorkItem
        {
            private readonly byte[] bytes;

            public OutgoingPacket(CSteamID peer, byte[] bytes)
                : base(peer)
            {
                this.bytes = bytes;
            }

            public override bool SendNext()
            {
                Complete = true;
                return SteamNetworking.SendP2PPacket(
                    Peer,
                    bytes,
                    (uint)bytes.Length,
                    EP2PSend.k_EP2PSendReliable,
                    Channel);
            }
        }

        private sealed class OutgoingBinaryBytesTransfer : OutgoingWorkItem
        {
            private readonly string type;
            private readonly JToken? payload;
            private readonly byte[] bytes;
            private readonly int revision;
            private readonly string messageId;
            private readonly string sender;
            private readonly int total;
            private int index;

            public OutgoingBinaryBytesTransfer(
                CSteamID peer,
                string type,
                JToken? payload,
                byte[] bytes,
                int revision,
                string messageId)
                : base(peer)
            {
                this.type = type;
                this.payload = payload;
                this.bytes = bytes;
                this.revision = revision;
                this.messageId = messageId;
                sender = SteamUser.GetSteamID().m_SteamID.ToString();
                total = Math.Max(1, (int)Math.Ceiling(bytes.Length / (double)MaxBinaryChunkBytes));
            }

            public override bool SendNext()
            {
                if (Complete)
                {
                    return false;
                }

                int offset = index * MaxBinaryChunkBytes;
                int count = Math.Min(MaxBinaryChunkBytes, bytes.Length - offset);
                byte[] packetBytes = CreateBinaryPacket(CreatePacket(index, total, type, sender, revision, messageId, payload), bytes, offset, count);
                bool sent = SteamNetworking.SendP2PPacket(Peer, packetBytes, (uint)packetBytes.Length, EP2PSend.k_EP2PSendReliable, Channel);
                index++;
                Complete = index >= total;
                return sent;
            }
        }

        private sealed class OutgoingBinaryFileTransfer : OutgoingWorkItem
        {
            private readonly string type;
            private readonly JToken? payload;
            private readonly string filePath;
            private readonly int revision;
            private readonly string messageId;
            private readonly string sender;
            private FileStream? stream;
            private int total;
            private int index;
            private bool opened;

            public OutgoingBinaryFileTransfer(
                CSteamID peer,
                string type,
                JToken? payload,
                string filePath,
                int revision,
                string messageId)
                : base(peer)
            {
                this.type = type;
                this.payload = payload;
                this.filePath = filePath;
                this.revision = revision;
                this.messageId = messageId;
                sender = SteamUser.GetSteamID().m_SteamID.ToString();
            }

            public override bool SendNext()
            {
                if (Complete)
                {
                    return false;
                }

                if (!EnsureOpen())
                {
                    Complete = true;
                    return false;
                }

                byte[] chunk = new byte[MaxBinaryChunkBytes];
                int count = stream == null ? 0 : stream.Read(chunk, 0, chunk.Length);
                byte[] packetBytes = CreateBinaryPacket(CreatePacket(index, total, type, sender, revision, messageId, payload), chunk, 0, count);
                bool sent = SteamNetworking.SendP2PPacket(Peer, packetBytes, (uint)packetBytes.Length, EP2PSend.k_EP2PSendReliable, Channel);
                index++;
                Complete = index >= total;
                return sent;
            }

            public override void Dispose()
            {
                stream?.Dispose();
                stream = null;
            }

            private bool EnsureOpen()
            {
                if (opened)
                {
                    return stream != null;
                }

                opened = true;
                try
                {
                    stream = File.OpenRead(filePath);
                    total = Math.Max(1, (int)Math.Ceiling(stream.Length / (double)MaxBinaryChunkBytes));
                    return true;
                }
                catch (Exception ex)
                {
                    Main.Mod?.Logger.Warning($"Failed to open collab resource for binary transfer {filePath}: {ex.Message}");
                    return false;
                }
            }
        }

        private static BinaryTransportPacket CreatePacket(
            int index,
            int total,
            string type,
            string sender,
            int revision,
            string messageId,
            JToken? payload)
        {
            return new BinaryTransportPacket
            {
                MessageId = messageId,
                Index = index,
                Total = total,
                Type = type,
                Sender = sender,
                Revision = revision,
                Payload = payload
            };
        }

        private sealed class BinaryTransportPacket
        {
            public string MessageId { get; set; } = string.Empty;

            public int Index { get; set; }

            public int Total { get; set; }

            public string Type { get; set; } = string.Empty;

            public string Sender { get; set; } = string.Empty;

            public int Revision { get; set; }

            public JToken? Payload { get; set; }
        }

        private sealed class BinaryChunkBuffer
        {
            private readonly byte[][] chunks;
            private int received;

            public BinaryChunkBuffer(BinaryTransportPacket packet, string sender)
            {
                Type = packet.Type;
                Sender = sender;
                Revision = packet.Revision;
                Payload = packet.Payload?.DeepClone();
                chunks = new byte[Math.Max(1, packet.Total)][];
            }

            public string Type { get; }

            public string Sender { get; }

            public int Revision { get; }

            public JToken? Payload { get; }

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
