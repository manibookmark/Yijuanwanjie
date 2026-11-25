using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Owns the loopback HTTP admission, bounded queue and request lifetime. No
    /// worker invokes the game delegate: only ProcessPending, on the constructing
    /// thread, may do so. A timeout can cancel queued work, never roll back work
    /// that has started. Dispose also belongs to that owner thread.
    /// </summary>
    public sealed class AgentGameHttpTransport : IDisposable
    {
        public const int DefaultPort = 17641;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly object _gate = new object();
        private readonly Func<string, string> _execute;
        private readonly string _protocolVersion;
        private readonly int _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        private readonly Limits _limits;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly LinkedList<Pending> _queue = new LinkedList<Pending>();
        private readonly HashSet<HttpListenerContext> _connections = new HashSet<HttpListenerContext>();
        private HttpListener _listener;
        private volatile bool _listening;
        private bool _disposed, _pumping;
        private int _receivers, _port = DefaultPort;

        public string GatewayInstanceId { get; } = Guid.NewGuid().ToString("N");
        // Only the local connection-file owner should expose this credential.
        public string BearerToken { get; }
        public string Endpoint => "http://127.0.0.1:" + _port + "/v1/game";
        public bool IsRunning => _listening;

        public AgentGameHttpTransport(Func<string, string> execute, string protocolVersion)
            : this(execute, protocolVersion, new Limits()) { }

        internal AgentGameHttpTransport(Func<string, string> execute, string protocolVersion, Limits limits)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            if (string.IsNullOrWhiteSpace(protocolVersion)) throw new ArgumentException("Protocol is required.");
            _protocolVersion = protocolVersion;
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
            _limits.Validate();
            var bytes = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            BearerToken = Convert.ToBase64String(bytes);
        }

        /// <summary>Explicitly opens an IPv4 loopback listener; never changes URL ACLs.</summary>
        public void Start(int port = DefaultPort)
        {
            RequireOwner();
            if (_disposed) throw new ObjectDisposedException(nameof(AgentGameHttpTransport));
            if (_listener != null) throw new InvalidOperationException("This transport cannot be started twice.");
            if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            try { _listener.Start(); }
            catch { _listener.Close(); _listener = null; throw; }
            _listening = true;
            _ = Task.Run(AcceptLoopAsync);
        }

        /// <summary>
        /// Executes at most maxRequests opaque JSON requests. One game action is
        /// atomic from this adapter's perspective; it cannot be time-sliced.
        /// </summary>
        public int ProcessPending(int maxRequests = 4)
        {
            RequireOwner();
            if (maxRequests < 1) throw new ArgumentOutOfRangeException(nameof(maxRequests));
            if (_pumping) throw new InvalidOperationException("Reentrant dispatch is not supported.");
            int processed = 0;
            _pumping = true;
            try
            {
                while (processed < maxRequests)
                {
                    Pending pending;
                    lock (_gate)
                    {
                        if (_disposed || _queue.Count == 0) break;
                        pending = _queue.First.Value;
                        _queue.RemoveFirst();
                        pending.Node = null;
                        if (pending.Cancellation.IsCancellationRequested)
                        {
                            pending.Completion.TrySetResult(Error(408, "RequestCancelled",
                                "This request was cancelled before dispatch."));
                            continue;
                        }
                        pending.Started = true;
                    }
                    Reply result;
                    try
                    {
                        string json = _execute(pending.Json);
                        result = json == null
                            ? Error(500, "ServiceExecutionError", "The game service returned no response.", true)
                            : new Reply(200, json);
                    }
                    catch
                    {
                        // Exception text may contain content, local paths or secrets.
                        result = Error(500, "ServiceExecutionError",
                            "The service failed after dispatch; execution was not rolled back.", true);
                    }
                    pending.Completion.TrySetResult(result);
                    processed++;
                }
                return processed;
            }
            finally { _pumping = false; }
        }

        public void Dispose()
        {
            RequireOwner();
            HttpListenerContext[] connections;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _listening = false;
                connections = new HttpListenerContext[_connections.Count];
                _connections.CopyTo(connections);
            }
            _stop.Cancel(); // Wakes receivers and cancels queued requests before service disposal.
            try { _listener?.Close(); }
            finally
            {
                foreach (HttpListenerContext context in connections)
                {
                    try { context.Response.Abort(); } catch { }
                    try { context.Request.InputStream.Close(); } catch { }
                }
            }
            // The source has no timer or wait handle; outstanding async receivers
            // still use it until their bounded cancellation cleanup finishes.
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    HttpListenerContext context = await _listener.GetContextAsync().ConfigureAwait(false);
                    bool accepted;
                    lock (_gate)
                    {
                        accepted = !_disposed && _connections.Count < _limits.MaxConcurrentRequests;
                        if (accepted) _connections.Add(context);
                    }
                    if (accepted) _ = ServeAsync(context);
                    else
                    {
                        // Do not create another worker when connection admission is full.
                        try
                        {
                            PrepareResponse(context.Response, Error(503, "GatewayBusy", "Connection limit reached."));
                            context.Response.Close(Utf8.GetBytes(Error(503, "GatewayBusy",
                                "Connection limit reached.").Json), false);
                        }
                        catch { context.Response.Abort(); }
                    }
                }
            }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                _listening = false;
                _stop.Cancel();
            }
        }

        private async Task ServeAsync(HttpListenerContext context)
        {
            try
            {
                HttpListenerRequest request = context.Request;
                var input = new Request
                {
                    Method = request.HttpMethod, Path = request.RawUrl,
                    Host = request.Headers["Host"], Origin = request.Headers["Origin"],
                    Authorization = request.Headers["Authorization"],
                    GatewayId = request.Headers["X-Agent-Gateway-Id"],
                    ContentType = request.ContentType, ContentEncoding = request.Headers["Content-Encoding"],
                    ContentLength = request.ContentLength64,
                    IsLoopback = request.RemoteEndPoint != null && IPAddress.IsLoopback(request.RemoteEndPoint.Address)
                };
                Reply reply = await ReceiveAsync(input, request.InputStream).ConfigureAwait(false);
                PrepareResponse(context.Response, reply);
                byte[] bytes = Utf8.GetBytes(reply.Json);
                context.Response.ContentLength64 = bytes.Length;
                using (var writeTimeout = new CancellationTokenSource(_limits.WriteTimeoutMilliseconds))
                using (writeTimeout.Token.Register(() => { try { context.Response.Abort(); } catch { } }))
                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length,
                        writeTimeout.Token).ConfigureAwait(false);
            }
            catch { /* The response may be lost; the service remains the owner of deduplication. */ }
            finally
            {
                try { context.Response.Close(); } catch { }
                try { context.Request.InputStream.Close(); } catch { }
                lock (_gate) _connections.Remove(context);
            }
        }

        // This is the same HTTP admission path used by the socket listener. Tests
        // may supply streams without opening ports or creating any Unity objects.
        internal async Task<Reply> ReceiveAsync(Request request, Stream body,
            CancellationToken cancellation = default)
        {
            lock (_gate)
            {
                if (_disposed || _stop.IsCancellationRequested)
                    return Error(503, "GatewayStopped", "This gateway instance has stopped.");
                if (_receivers >= _limits.MaxConcurrentRequests)
                    return Error(503, "GatewayBusy", "Concurrent request limit reached.");
                _receivers++;
            }
            try
            {
                Reply rejected = Validate(request);
                if (rejected != null) return rejected;
                if (request.Path == "/health")
                    return new Reply(200, "{\"protocolVersion\":" + Quote(_protocolVersion) + ",\"running\":true}");
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellation))
                {
                    deadline.CancelAfter(_limits.RequestTimeoutMilliseconds);
                    string json;
                    try { json = await ReadBodyAsync(body, deadline.Token).ConfigureAwait(false); }
                    catch (BodyTooLargeException) { return Error(413, "RequestTooLarge", "Request body limit exceeded."); }
                    catch (DecoderFallbackException) { return Error(400, "InvalidEncoding", "Request must be valid UTF-8."); }
                    catch (Exception) when (deadline.IsCancellationRequested)
                    { return Error(408, "RequestCancelled", "Request stopped before dispatch."); }
                    catch (IOException) { return Error(400, "InvalidBody", "Request body could not be read."); }
                    if (string.IsNullOrWhiteSpace(json)) return Error(400, "InvalidBody", "JSON body is required.");
                    Pending pending;
                    lock (_gate)
                    {
                        if (_disposed || _stop.IsCancellationRequested)
                            return Error(503, "GatewayStopped", "This gateway instance has stopped.");
                        if (deadline.IsCancellationRequested)
                            return Error(408, "RequestCancelled", "Request stopped before dispatch.");
                        if (_queue.Count >= _limits.MaxQueuedRequests)
                            return Error(503, "GatewayBusy", "Main-thread queue limit reached.");
                        pending = new Pending(json, deadline.Token);
                        pending.Node = _queue.AddLast(pending);
                    }
                    using (deadline.Token.Register(() => Cancel(pending)))
                        return await pending.Completion.Task.ConfigureAwait(false);
                }
            }
            finally { lock (_gate) _receivers--; }
        }

        private Reply Validate(Request request)
        {
            if (request == null || !request.IsLoopback ||
                !string.Equals(request.Host, "127.0.0.1:" + _port, StringComparison.Ordinal))
                return Error(403, "InvalidHost", "Only the configured IPv4 loopback host is accepted.");
            if (request.Origin != null)
                return Error(403, "OriginNotAllowed", "Browser Origin requests are not accepted.");
            if (!FixedEquals("Bearer " + BearerToken, request.Authorization))
                return Error(401, "Unauthorized", "A valid local bearer credential is required.");
            if (!FixedEquals(GatewayInstanceId, request.GatewayId))
                return Error(409, "GatewayInstanceChanged", "Read connection details; do not recreate an old session silently.");
            bool health = request.Path == "/health";
            if (!health && request.Path != "/v1/game") return Error(404, "NotFound", "Unknown endpoint.");
            if (request.Method != (health ? "GET" : "POST")) return Error(405, "MethodNotAllowed", "Unsupported method.");
            if (health) return null;
            string[] mediaType = (request.ContentType ?? string.Empty).Split(';');
            if (!string.Equals(mediaType[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
                return Error(415, "UnsupportedMediaType", "Use application/json with UTF-8 encoding.");
            for (int i = 1; i < mediaType.Length; i++)
                if (!string.Equals(mediaType[i].Trim(), "charset=utf-8", StringComparison.OrdinalIgnoreCase))
                    return Error(415, "UnsupportedMediaType", "Only UTF-8 JSON is accepted.");
            if (!string.IsNullOrEmpty(request.ContentEncoding) && request.ContentEncoding != "identity")
                return Error(415, "UnsupportedEncoding", "Compressed request bodies are not accepted.");
            if (request.ContentLength > _limits.MaxRequestBytes)
                return Error(413, "RequestTooLarge", "Request body limit exceeded.");
            return null;
        }

        private async Task<string> ReadBodyAsync(Stream body, CancellationToken cancellation)
        {
            using (cancellation.Register(() => { try { body.Close(); } catch { } }))
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[4096];
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int count = await body.ReadAsync(chunk, 0, chunk.Length, cancellation).ConfigureAwait(false);
                    if (count == 0) break;
                    if (buffer.Length + count > _limits.MaxRequestBytes) throw new BodyTooLargeException();
                    buffer.Write(chunk, 0, count);
                }
                return Utf8.GetString(buffer.ToArray());
            }
        }

        private void Cancel(Pending pending)
        {
            lock (_gate)
            {
                if (pending.Completion.Task.IsCompleted) return;
                if (pending.Node != null) { _queue.Remove(pending.Node); pending.Node = null; }
                pending.Completion.TrySetResult(pending.Started
                    ? Error(504, "ExecutionStatusUnknown",
                        "Dispatch started. Retry the identical requestId and body or observe; do not assume rollback.", true)
                    : Error(_stop.IsCancellationRequested ? 503 : 408,
                        _stop.IsCancellationRequested ? "GatewayStopped" : "RequestCancelled",
                        "This request was cancelled before dispatch."));
            }
        }

        private void PrepareResponse(HttpListenerResponse response, Reply reply)
        {
            response.StatusCode = reply.StatusCode;
            response.ContentType = "application/json; charset=utf-8";
            response.KeepAlive = false;
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["X-Agent-Gateway-Id"] = GatewayInstanceId;
        }

        private Reply Error(int status, string code, string message, bool started = false) => new Reply(status,
            "{\"protocolVersion\":" + Quote(_protocolVersion) + ",\"success\":false,\"error\":{\"code\":" +
            Quote(code) + ",\"message\":" + Quote(message) + "},\"executionState\":\"" +
            (started ? "started_unknown" : "not_started") + "\"}");

        private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";

        private static bool FixedEquals(string expected, string actual)
        {
            if (actual == null || actual.Length != expected.Length) return false;
            int different = 0;
            for (int i = 0; i < expected.Length; i++) different |= expected[i] ^ actual[i];
            return different == 0;
        }

        private void RequireOwner()
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
                throw new InvalidOperationException("Gateway lifecycle and dispatch require the constructing thread.");
        }

        internal sealed class Limits
        {
            internal int MaxRequestBytes = 256 * 1024, MaxQueuedRequests = 8, MaxConcurrentRequests = 16;
            internal int RequestTimeoutMilliseconds = 30000, WriteTimeoutMilliseconds = 5000;
            internal void Validate()
            {
                if (MaxRequestBytes < 1 || MaxQueuedRequests < 1 || MaxConcurrentRequests < 1 ||
                    RequestTimeoutMilliseconds < 1 || WriteTimeoutMilliseconds < 1)
                    throw new ArgumentOutOfRangeException(nameof(Limits));
            }
        }

        internal sealed class Request
        {
            internal string Method, Path, Host, Origin, Authorization, GatewayId, ContentType, ContentEncoding;
            internal long ContentLength;
            internal bool IsLoopback;
        }

        internal sealed class Reply
        {
            internal readonly int StatusCode;
            internal readonly string Json;
            internal Reply(int statusCode, string json) { StatusCode = statusCode; Json = json; }
        }

        private sealed class Pending
        {
            internal readonly string Json;
            internal readonly CancellationToken Cancellation;
            internal readonly TaskCompletionSource<Reply> Completion =
                new TaskCompletionSource<Reply>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal LinkedListNode<Pending> Node;
            internal bool Started;
            internal Pending(string json, CancellationToken cancellation)
            { Json = json; Cancellation = cancellation; }
        }

        private sealed class BodyTooLargeException : Exception { }
    }
}
