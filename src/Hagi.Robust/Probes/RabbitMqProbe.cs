using RabbitMQ.Client;

namespace Hagi.Robust.Probes;

/// <summary>
/// Startup probe that checks RabbitMQ connectivity by attempting to create a connection and channel.
/// Throws an exception if RabbitMQ is not reachable, which signals to the resilience pipeline to retry.
/// </summary>
public class RabbitMqProbe : IStartupProbe
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the RabbitMqProbe class.
    /// </summary>
    /// <param name="connectionString">RabbitMQ connection string (e.g., "amqp://user:pass@localhost:5672" or just "localhost")</param>
    public RabbitMqProbe(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <summary>
    /// Initializes a new instance of the RabbitMqProbe class with host and port.
    /// </summary>
    /// <param name="host">RabbitMQ host (e.g., "localhost" or "rabbitmq")</param>
    /// <param name="port">RabbitMQ port (default: 5672)</param>
    public RabbitMqProbe(string host, int port = 5672)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host cannot be null or empty.", nameof(host));

        _connectionString = $"amqp://{host}:{port}";
    }

    /// <summary>
    /// Probes RabbitMQ connectivity by creating a connection and channel.
    /// Throws an exception if RabbitMQ is not available, which triggers retry logic.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="Exception">Thrown when RabbitMQ is not reachable</exception>
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        IConnection? connection = null;
        IChannel? channel = null;

        try
        {
            var connectionFactory = new ConnectionFactory();

            if (_connectionString.StartsWith("amqp://") || _connectionString.StartsWith("amqps://"))
            {
                connectionFactory.Uri = new Uri(_connectionString);
            }
            else
            {
                connectionFactory.HostName = _connectionString;
                connectionFactory.Port = 5672;
            }

            connectionFactory.RequestedConnectionTimeout = TimeSpan.FromSeconds(5);
            connectionFactory.AutomaticRecoveryEnabled = false;

            connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
            channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            throw new Exception($"RabbitMQ probe failed for '{_connectionString}': {ex.Message}", ex);
        }
        finally
        {
            if (channel != null)
            {
                await channel.CloseAsync(cancellationToken);
                await channel.DisposeAsync();
            }

            if (connection != null)
            {
                await connection.CloseAsync(cancellationToken);
                await connection.DisposeAsync();
            }
        }
    }
}
