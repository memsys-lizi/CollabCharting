using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using EmbedIO.WebSockets;

namespace ADOFAIWebBridge
{
    internal sealed class RpcWebSocketModule : WebSocketModule
    {
        private readonly WebBridge bridge;
        private readonly HashSet<string> acceptedContextIds = new HashSet<string>();

        public RpcWebSocketModule(WebBridge bridge)
            : base("/rpc", true)
        {
            this.bridge = bridge;
            MaxMessageSize = 1024 * 1024;
        }

        public Task BroadcastEventAsync(string payload)
        {
            return BroadcastAsync(payload, context => acceptedContextIds.Contains(context.Id));
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            if (!context.IsLocal || !bridge.IsTokenAccepted(context.RequestUri))
            {
                await CloseAsync(context).ConfigureAwait(false);
                return;
            }

            acceptedContextIds.Add(context.Id);
            await SendAsync(context, bridge.CreateReadyEvent()).ConfigureAwait(false);
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
        {
            acceptedContextIds.Remove(context.Id);
            return Task.CompletedTask;
        }

        protected override async Task OnMessageReceivedAsync(
            IWebSocketContext context,
            byte[] rxBuffer,
            IWebSocketReceiveResult rxResult)
        {
            if (!acceptedContextIds.Contains(context.Id))
            {
                await CloseAsync(context).ConfigureAwait(false);
                return;
            }

            string request = Encoding.UTF8.GetString(rxBuffer, 0, rxResult.Count);
            string response = await bridge.HandleRpcMessageAsync(request).ConfigureAwait(false);
            await SendAsync(context, response).ConfigureAwait(false);
        }
    }
}
