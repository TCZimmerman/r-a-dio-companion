using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace RadioCompanion;

public sealed class SseClient : IAsyncDisposable
{
    private readonly HttpClient _http = new();
    private readonly Uri _endpoint;
    private readonly CancellationTokenSource _stop = new();
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

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
                request.Headers.Accept.ParseAdd("text/event-stream");
                using var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    _stop.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                ConnectionChanged?.Invoke(true);

                await using var stream = await response.Content.ReadAsStreamAsync(_stop.Token).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                string eventName = "message";
                var data = new StringBuilder();

                while (!_stop.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false);
                    if (line is null) break;

                    if (line.Length == 0)
                    {
                        if (data.Length > 0)
                        {
                            EventReceived?.Invoke(eventName, data.ToString().TrimEnd('\n'));
                            data.Clear();
                        }
                        eventName = "message";
                        continue;
                    }

                    if (line.StartsWith(':')) continue;
                    if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                    {
                        eventName = line[6..].Trim();
                    }
                    else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        data.AppendLine(line[5..].TrimStart());
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Reconnect below.
            }
            finally
            {
                ConnectionChanged?.Invoke(false);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), _stop.Token).ConfigureAwait(false);
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
        if (_worker is not null)
        {
            try { await _worker.ConfigureAwait(false); } catch { }
        }
        _http.Dispose();
        _stop.Dispose();
    }
}
