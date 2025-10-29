using System.Net.Sockets;

namespace Hagi.Robust.Probes;

/// <summary>
/// Startup probe that checks TCP port connectivity by attempting to establish a connection.
/// Useful for generic port checks when protocol-specific probes are not available.
/// Throws an exception if the port is not reachable, which signals to the resilience pipeline to retry.
/// </summary>
public class TcpProbe : IStartupProbe
{
    private readonly string _host;
    private readonly int _port;

    /// <summary>
    /// Initializes a new instance of the TcpProbe class.
    /// </summary>
    /// <param name="host">Host to connect to (e.g., "localhost" or "database")</param>
    /// <param name="port">TCP port to check</param>
    public TcpProbe(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host cannot be null or empty.", nameof(host));

        if (port <= 0 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");

        _host = host;
        _port = port;
    }

    /// <summary>
    /// Probes TCP port connectivity by attempting to establish a connection.
    /// Throws an exception if the port is not reachable, which triggers retry logic.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="Exception">Thrown when TCP port is not reachable</exception>
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        TcpClient? client = null;

        try
        {
            client = new TcpClient();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await client.ConnectAsync(_host, _port, linkedCts.Token);

            if (!client.Connected)
            {
                throw new Exception($"TCP connection to {_host}:{_port} failed: Not connected");
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"TCP probe failed for {_host}:{_port}: {ex.Message}", ex);
        }
        finally
        {
            client?.Close();
            client?.Dispose();
        }
    }
}
