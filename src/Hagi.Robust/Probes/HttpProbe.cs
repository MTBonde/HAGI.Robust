namespace Hagi.Robust.Probes;

/// <summary>
/// Startup probe that checks HTTP endpoint availability by sending a GET request.
/// Throws an exception if the endpoint is not reachable or returns an error status code.
/// </summary>
public class HttpProbe : IStartupProbe
{
    private readonly string _url;
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;

    /// <summary>
    /// Initializes a new instance of the HttpProbe class with a URL.
    /// Creates an internal HttpClient that will be disposed when the probe is disposed.
    /// </summary>
    /// <param name="url">HTTP endpoint URL to check (e.g., "http://localhost:8080/health")</param>
    public HttpProbe(string url)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _disposeHttpClient = true;
    }

    /// <summary>
    /// Initializes a new instance of the HttpProbe class with a URL and custom HttpClient.
    /// The provided HttpClient will NOT be disposed by this probe.
    /// </summary>
    /// <param name="url">HTTP endpoint URL to check</param>
    /// <param name="httpClient">Custom HttpClient to use for requests</param>
    public HttpProbe(string url, HttpClient httpClient)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _disposeHttpClient = false;
    }

    /// <summary>
    /// Probes HTTP endpoint availability by sending a GET request.
    /// Throws an exception if the endpoint is not available or returns an error status code.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="Exception">Thrown when HTTP endpoint is not reachable</exception>
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(_url, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            throw new Exception($"HTTP probe failed for '{_url}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Disposes the internal HttpClient if it was created by this probe.
    /// </summary>
    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient?.Dispose();
        }
    }
}
