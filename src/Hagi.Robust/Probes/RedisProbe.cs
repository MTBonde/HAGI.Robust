using StackExchange.Redis;

namespace Hagi.Robust.Probes;

/// <summary>
/// Startup probe that checks Redis connectivity by attempting to ping the server.
/// Throws an exception if Redis is not reachable, which signals to the resilience pipeline to retry.
/// </summary>
public class RedisProbe : IStartupProbe
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the RedisProbe class.
    /// </summary>
    /// <param name="connectionString">Redis connection string (e.g., "localhost:6379" or "redis:6379,password=secret")</param>
    public RedisProbe(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <summary>
    /// Initializes a new instance of the RedisProbe class with host and port.
    /// </summary>
    /// <param name="host">Redis host (e.g., "localhost" or "redis")</param>
    /// <param name="port">Redis port (default: 6379)</param>
    public RedisProbe(string host, int port = 6379)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host cannot be null or empty.", nameof(host));

        _connectionString = $"{host}:{port}";
    }

    /// <summary>
    /// Probes Redis connectivity by attempting to connect and ping the server.
    /// Throws an exception if Redis is not available, which triggers retry logic.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="Exception">Thrown when Redis is not reachable</exception>
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        IConnectionMultiplexer? connection = null;

        try
        {
            var options = ConfigurationOptions.Parse(_connectionString);
            options.ConnectTimeout = 5000;
            options.SyncTimeout = 5000;
            options.AbortOnConnectFail = false;

            connection = await ConnectionMultiplexer.ConnectAsync(options);

            if (!connection.IsConnected)
            {
                throw new Exception($"Redis connection failed for '{_connectionString}': Not connected");
            }

            var server = connection.GetServer(connection.GetEndPoints().First());
            await server.PingAsync();
        }
        catch (Exception ex)
        {
            throw new Exception($"Redis probe failed for '{_connectionString}': {ex.Message}", ex);
        }
        finally
        {
            if (connection != null)
            {
                await connection.CloseAsync();
                connection.Dispose();
            }
        }
    }
}
