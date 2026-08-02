using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace RadioCompanion;

public sealed class SseClient : IAsyncDisposable
{
    private readonly HttpClient _http = new();
    private readonly Uri _endpoint;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _reconnectLock = new();

    private CancellationTokenSource _reconnect = new();
    private Task? _worker;

    public event Action<string, string>? EventReceived;
    public event Action<bool>? ConnectionChanged;

    public SseClient(string endpoint)
    {
        _endpoint = new Uri(endpoint);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RadioCompanion", "1.0"));
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public void Start() => _worker ??= Task.Run(RunAsync);
    public void Restart()
    {
        lock (_reconnectLock)
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }

            _reconnect.Cancel();
        }
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            CancellationTokenSource reconnectTokenSource;

            lock (_reconnectLock)
            {
                if (_reconnect.IsCancellationRequested)
                {
                    _reconnect.Dispose();
                    _reconnect = new CancellationTokenSource();
                }

                reconnectTokenSource = _reconnect;
            }

            using var connectionTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(
                    _stop.Token,
                    reconnectTokenSource.Token);

            var connectionToken = connectionTokenSource.Token;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
                request.Headers.Accept.ParseAdd("text/event-stream");

                using var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    connectionToken).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();
                ConnectionChanged?.Invoke(true);

                await using var stream = await response.Content
                    .ReadAsStreamAsync(connectionToken)
                    .ConfigureAwait(false);

                using var reader = new StreamReader(stream, Encoding.UTF8);

                string eventName = "message";
                var data = new StringBuilder();

                while (!connectionToken.IsCancellationRequested)
                {
                    var line = await reader
                        .ReadLineAsync(connectionToken)
                        .ConfigureAwait(false);

                    if (line is null)
                    {
                        break;
                    }

                    if (line.Length == 0)
                    {
                        if (data.Length > 0)
                        {
                            EventReceived?.Invoke(
                                eventName,
                                data.ToString().TrimEnd('\n'));

                            data.Clear();
                        }

                        eventName = "message";
                        continue;
                    }

                    if (line.StartsWith(':'))
                    {
                        continue;
                    }

                    if (line.StartsWith(
                        "event:",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        eventName = line[6..].Trim();
                    }
                    else if (line.StartsWith(
                        "data:",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        data.AppendLine(line[5..].TrimStart());
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // The current connection was deliberately restarted.
            }
            catch
            {
                // Reconnect below.
            }
            finally
            {
                ConnectionChanged?.Invoke(false);
            }

            if (reconnectTokenSource.IsCancellationRequested)
            {
                // An explicit restart should reconnect immediately.
                continue;
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(3),
                    _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();

        lock (_reconnectLock)
        {
            _reconnect.Cancel();
        }

        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        lock (_reconnectLock)
        {
            _reconnect.Dispose();
        }

        _http.Dispose();
        _stop.Dispose();
    }
}
