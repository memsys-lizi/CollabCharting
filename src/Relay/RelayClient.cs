using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CollabCharting
{
    internal sealed class RelayClient : IDisposable
    {
        private readonly ConcurrentQueue<RelayServerEvent> inbox = new ConcurrentQueue<RelayServerEvent>();
        private ClientWebSocket? socket;
        private CancellationTokenSource? cancellation;
        private Task? receiveTask;
        private string relayToken = string.Empty;

        public bool IsConnected => socket != null && socket.State == WebSocketState.Open;

        public void Connect(string token)
        {
            if (IsConnected && string.Equals(relayToken, token, StringComparison.Ordinal))
            {
                return;
            }

            DisposeSocket();
            relayToken = token;
            cancellation = new CancellationTokenSource();
            socket = new ClientWebSocket();
            Uri uri = new Uri(RelayConfig.WebSocketUrl + "?token=" + Uri.EscapeDataString(token));
            socket.ConnectAsync(uri, cancellation.Token).GetAwaiter().GetResult();
            receiveTask = Task.Run(ReceiveLoop);
        }

        public bool TryDequeue(out RelayServerEvent serverEvent)
        {
            return inbox.TryDequeue(out serverEvent);
        }

        public void CreateRoom()
        {
            Send(new { type = "room.create" });
        }

        public void JoinRoom(string roomId)
        {
            Send(new { type = "room.join", roomId });
        }

        public void LeaveRoom()
        {
            if (IsConnected)
            {
                Send(new { type = "room.leave" });
            }
        }

        public void SendToHost(string type, object payload, int revision)
        {
            Send(new
            {
                type = "relay.toHost",
                payload = new RelayPayload { Type = type, Revision = revision, Payload = payload == null ? null : JToken.FromObject(payload) }
            });
        }

        public void SendToUser(string targetUserId, string type, object payload, int revision)
        {
            Send(new
            {
                type = "relay.toUser",
                targetUserId,
                payload = new RelayPayload { Type = type, Revision = revision, Payload = payload == null ? null : JToken.FromObject(payload) }
            });
        }

        public void Broadcast(string type, object payload, int revision)
        {
            Send(new
            {
                type = "relay.broadcast",
                payload = new RelayPayload { Type = type, Revision = revision, Payload = payload == null ? null : JToken.FromObject(payload) }
            });
        }

        public void Dispose()
        {
            DisposeSocket();
        }

        private void Send(object message)
        {
            if (!IsConnected || socket == null)
            {
                throw new InvalidOperationException("Relay WebSocket 尚未连接。");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
            socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        private async Task ReceiveLoop()
        {
            byte[] buffer = new byte[64 * 1024];
            var memory = new MemoryStream();
            try
            {
                while (socket != null && socket.State == WebSocketState.Open && cancellation != null && !cancellation.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    memory.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    string json = Encoding.UTF8.GetString(memory.ToArray());
                    memory.SetLength(0);
                    RelayServerEvent? serverEvent = JsonConvert.DeserializeObject<RelayServerEvent>(json);
                    if (serverEvent != null)
                    {
                        inbox.Enqueue(serverEvent);
                    }
                }
            }
            catch (Exception ex)
            {
                inbox.Enqueue(new RelayServerEvent
                {
                    Type = "error",
                    Payload = JObject.FromObject(new { message = "Relay WebSocket disconnected: " + ex.Message })
                });
            }
            finally
            {
                memory.Dispose();
            }
        }

        private void DisposeSocket()
        {
            try
            {
                cancellation?.Cancel();
                if (socket != null && socket.State == WebSocketState.Open)
                {
                    socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "dispose", CancellationToken.None)
                        .Wait(500);
                }
            }
            catch
            {
            }

            socket?.Dispose();
            cancellation?.Dispose();
            socket = null;
            cancellation = null;
            receiveTask = null;
        }
    }

    internal sealed class RelayServerEvent
    {
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("senderUserId")]
        public string SenderUserId { get; set; } = string.Empty;

        [JsonProperty("payload")]
        public JToken? Payload { get; set; }
    }

    internal sealed class RelayPayload
    {
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("revision")]
        public int Revision { get; set; }

        [JsonProperty("payload")]
        public JToken? Payload { get; set; }
    }

    internal sealed class RelayRoomState
    {
        [JsonProperty("roomId")]
        public string RoomId { get; set; } = string.Empty;

        [JsonProperty("hostUserId")]
        public string HostUserId { get; set; } = string.Empty;

        [JsonProperty("members")]
        public System.Collections.Generic.List<RelayRoomMember> Members { get; set; } = new System.Collections.Generic.List<RelayRoomMember>();
    }

    internal sealed class RelayRoomMember
    {
        [JsonProperty("userId")]
        public string UserId { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("isHost")]
        public bool IsHost { get; set; }
    }
}
