using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Actions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ADOFAIWebBridge
{
    public sealed class WebBridge : IDisposable
    {
        private readonly CommandRegistry commands = new CommandRegistry();
        private readonly Action<string>? log;
        private WebServer? server;
        private RpcWebSocketModule? rpcModule;
        private CancellationTokenSource? cancellation;
        private Task? serverTask;
        private bool disposed;

        private WebBridge(WebBridgeOptions options, string? modPath, Action<string>? log)
        {
            Options = options.Normalize(modPath);
            this.log = log;
            Token = Guid.NewGuid().ToString("N");
        }

        public WebBridgeOptions Options { get; }

        public int Port { get; private set; }

        public string Token { get; }

        public string LocalServerUrl => $"http://{Options.Host}:{Port}/";

        public string PublicServerUrl =>
            $"http://{(string.IsNullOrWhiteSpace(Options.PublicHostName) ? Options.Host : Options.PublicHostName)}:{Port}/";

        public string OverlayUrl =>
            AppendBridgeToken(Options.Mode == BridgeMode.Development ? Options.DevServerUrl : PublicServerUrl);

        public static WebBridge Create(WebBridgeOptions options)
        {
            return new WebBridge(options, null, null);
        }

        public static WebBridge ForUMM(object modEntry, WebBridgeOptions options)
        {
            string? modPath = UmmAdapter.GetModPath(modEntry);
            options.ModId = string.IsNullOrWhiteSpace(options.ModId)
                ? UmmAdapter.GetModId(modEntry) ?? string.Empty
                : options.ModId;

            return new WebBridge(options, modPath, UmmAdapter.CreateLogger(modEntry));
        }

        public WebBridge RegisterCommands(Type type)
        {
            commands.Register(type);
            return this;
        }

        public WebBridge RegisterCommands(object target)
        {
            commands.Register(target);
            return this;
        }

        public WebBridge RegisterCommand(string name, Func<Newtonsoft.Json.Linq.JToken?, WebCommandContext, object?> handler)
        {
            commands.RegisterDelegate(name, handler);
            return this;
        }

        public WebBridge RegisterCommand(string name, Func<Newtonsoft.Json.Linq.JToken?, object?> handler)
        {
            commands.RegisterDelegate(name, (parameters, context) => handler(parameters));
            return this;
        }

        public void Start()
        {
            ThrowIfDisposed();
            if (server != null)
            {
                return;
            }

            Port = PortAllocator.FindAvailable(Options.Host, Options.PreferredPort, Options.PortProbeCount);
            cancellation = new CancellationTokenSource();
            rpcModule = new RpcWebSocketModule(this);

            var webServer = new WebServer(o => o
                    .WithUrlPrefix(LocalServerUrl)
                    .WithMode(HttpListenerMode.EmbedIO))
                .WithModule(rpcModule);

            if (Options.Mode == BridgeMode.Production && Directory.Exists(Options.WebRoot))
            {
                webServer.WithModule(new ActionModule(ServeWebUiAsync));
                Log($"ADOFAIWebBridge serving WebRoot: {Options.WebRoot}");
            }
            else if (Options.Mode == BridgeMode.Production)
            {
                Log($"ADOFAIWebBridge WebRoot not found: {Options.WebRoot}");
            }

            server = webServer;
            serverTask = server.RunAsync(cancellation.Token);
            Log($"ADOFAIWebBridge listening at {LocalServerUrl}");
        }

        public void Stop()
        {
            if (server == null)
            {
                return;
            }

            cancellation?.Cancel();
            try
            {
                serverTask?.Wait(1000);
            }
            catch
            {
                // Ignore shutdown races in Unity/UMM unload paths.
            }

            server.Dispose();
            cancellation?.Dispose();
            server = null;
            rpcModule = null;
            cancellation = null;
            serverTask = null;
        }

        public void OpenSteamOverlay()
        {
            string url = OverlayUrl;
            if (Options.UseSteamOverlay && SteamOverlayLauncher.TryOpen(url, Log))
            {
                return;
            }

            SteamOverlayLauncher.OpenInSystemBrowser(url);
        }

        public void Emit(string eventName, object? data)
        {
            ThrowIfDisposed();
            string payload = JsonConvert.SerializeObject(new
            {
                @event = eventName,
                data
            });

            rpcModule?.BroadcastEventAsync(payload);
        }

        internal bool IsTokenAccepted(Uri requestUri)
        {
            if (!Options.RequireToken)
            {
                return true;
            }

            string? supplied = GetQueryValue(requestUri, "token");
            return string.Equals(supplied, Token, StringComparison.Ordinal);
        }

        internal string CreateReadyEvent()
        {
            return JsonConvert.SerializeObject(new
            {
                @event = "bridge.ready",
                data = new
                {
                    modId = Options.ModId,
                    displayName = Options.DisplayName,
                    mode = Options.Mode.ToString(),
                    port = Port
                }
            });
        }

        internal async Task<string> HandleRpcMessageAsync(string raw)
        {
            RpcRequest? request = null;
            try
            {
                Log($"ADOFAIWebBridge RPC <= {raw}");
                request = JsonConvert.DeserializeObject<RpcRequest>(raw);
                if (request == null || string.IsNullOrWhiteSpace(request.Method))
                {
                    throw new WebBridgeException("invalid_request", "RPC request must include a method.");
                }

                string method = request.Method ?? string.Empty;
                var context = new WebCommandContext(this, request.Id?.ToString() ?? string.Empty, method);
                object? result = await commands.InvokeAsync(method, request.Params, context).ConfigureAwait(false);
                string response = JsonConvert.SerializeObject(new { id = request.Id, result });
                Log($"ADOFAIWebBridge RPC => {response}");
                return response;
            }
            catch (Exception ex)
            {
                RpcError error = ToRpcError(ex);
                string response = JsonConvert.SerializeObject(new { id = request?.Id, error });
                Log($"ADOFAIWebBridge RPC !! {response}");
                return response;
            }
        }

        private Task ServeWebUiAsync(IHttpContext context)
        {
            if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(context.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 405;
                return context.SendStringAsync("Method Not Allowed", "text/plain", System.Text.Encoding.UTF8);
            }

            string webRoot = Path.GetFullPath(Options.WebRoot);
            string path = context.Request.Url.LocalPath.TrimStart('/');
            string filePath = string.IsNullOrEmpty(path)
                ? Path.Combine(webRoot, "index.html")
                : Path.Combine(webRoot, path.Replace('/', Path.DirectorySeparatorChar));

            filePath = Path.GetFullPath(filePath);
            if (!filePath.StartsWith(webRoot, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 403;
                return context.SendStringAsync("Forbidden", "text/plain", System.Text.Encoding.UTF8);
            }

            if (!File.Exists(filePath))
            {
                filePath = Path.Combine(webRoot, "index.html");
            }

            if (!File.Exists(filePath))
            {
                context.Response.StatusCode = 404;
                return context.SendStringAsync("ADOFAIWebBridge: webui/dist/index.html not found.", "text/plain", System.Text.Encoding.UTF8);
            }

            context.Response.ContentType = GetContentType(Path.GetExtension(filePath));
            if (string.Equals(context.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            byte[] bytes = File.ReadAllBytes(filePath);
            return context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }

        private static string GetContentType(string extension)
        {
            switch (extension.ToLowerInvariant())
            {
                case ".html":
                    return "text/html; charset=utf-8";
                case ".js":
                    return "application/javascript; charset=utf-8";
                case ".css":
                    return "text/css; charset=utf-8";
                case ".json":
                    return "application/json; charset=utf-8";
                case ".svg":
                    return "image/svg+xml";
                case ".png":
                    return "image/png";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".ico":
                    return "image/x-icon";
                default:
                    return "application/octet-stream";
            }
        }

        private static string? GetQueryValue(Uri uri, string key)
        {
            string query = uri.Query;
            if (string.IsNullOrEmpty(query))
            {
                return null;
            }

            foreach (string part in query.TrimStart('?').Split('&'))
            {
                string[] pieces = part.Split(new[] { '=' }, 2);
                if (pieces.Length == 0)
                {
                    continue;
                }

                string name = Uri.UnescapeDataString(pieces[0]);
                if (!string.Equals(name, key, StringComparison.Ordinal))
                {
                    continue;
                }

                return pieces.Length == 2 ? Uri.UnescapeDataString(pieces[1]) : string.Empty;
            }

            return null;
        }

        private string AppendBridgeToken(string url)
        {
            if (!Options.RequireToken)
            {
                return url;
            }

            string separator = url.Contains("?") ? "&" : "?";
            return $"{url}{separator}bridgeToken={Uri.EscapeDataString(Token)}";
        }

        private static RpcError ToRpcError(Exception ex)
        {
            Exception actual = ex is System.Reflection.TargetInvocationException && ex.InnerException != null
                ? ex.InnerException
                : ex;

            if (actual is WebBridgeException bridgeException)
            {
                return new RpcError { Code = bridgeException.Code, Message = bridgeException.Message };
            }

            return new RpcError { Code = "command_failed", Message = actual.Message };
        }

        private void Log(string message)
        {
            log?.Invoke(message);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(WebBridge));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Stop();
            disposed = true;
        }
    }
}
