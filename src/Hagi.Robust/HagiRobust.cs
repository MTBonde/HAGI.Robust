using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Hagi.Robust;

/// <summary>
/// Standalone utility class for dependency waiting without requiring Generic Host or Dependency Injection.
/// Provides the same resilience logic as the integrated DependencyWaiter but for simple console applications.
/// </summary>
public static class HagiRobust
{
    /// <summary>
    /// Waits for all startup dependencies to become ready using the same resilience logic as integrated mode.
    /// Uses Polly retry (1s, 3s, 9s) and circuit breaker to ensure dependencies are available before continuing.
    /// </summary>
    /// <param name="probes">Collection of startup probes to check (RabbitMQ, Redis, HTTP endpoints, etc.)</param>
    /// <param name="cancellationToken">Cancellation token to stop waiting</param>
    /// <param name="logger">Optional logger for diagnostic messages. If null, logs to console.</param>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested</exception>
    /// <exception cref="Exception">Thrown if dependencies cannot become ready after all retries</exception>
    public static async Task WaitForDependenciesAsync(
        IEnumerable<IStartupProbe> probes,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        var probeList = probes.ToList();

        if (probeList.Count == 0)
        {
            LogInformation(logger, "No startup probes registered; dependencies are ready.");
            return;
        }

        LogInformation(logger, $"Waiting for {probeList.Count} startup dependencies to become ready...");

        var resiliencePipeline = BuildResiliencePipeline(logger);
        var ready = false;

        while (!cancellationToken.IsCancellationRequested && !ready)
        {
            try
            {
                await resiliencePipeline.ExecuteAsync(async ct =>
                {
                    foreach (var probe in probeList)
                    {
                        await probe.ProbeAsync(ct);
                    }
                }, cancellationToken);

                ready = true;
                LogInformation(logger, "All startup dependencies are ready.");
            }
            catch (BrokenCircuitException)
            {
                LogWarning(logger, "Circuit breaker opened; waiting before retry...");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (Exception ex)
            {
                LogWarning(logger, $"Probes failed after retries: {ex.Message}. Will attempt again.");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Dependency waiting was cancelled.", cancellationToken);
        }
    }

    private static ResiliencePipeline BuildResiliencePipeline(ILogger? logger)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                OnRetry = args =>
                {
                    LogWarning(logger, $"Startup probe retry {args.AttemptNumber + 1} after {args.RetryDelay}. Error: {args.Outcome.Exception?.Message}");
                    return ValueTask.CompletedTask;
                },
                DelayGenerator = args =>
                {
                    var delay = args.AttemptNumber switch
                    {
                        0 => TimeSpan.FromSeconds(1),
                        1 => TimeSpan.FromSeconds(3),
                        2 => TimeSpan.FromSeconds(9),
                        _ => TimeSpan.FromSeconds(9)
                    };
                    return ValueTask.FromResult<TimeSpan?>(delay);
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                OnOpened = args =>
                {
                    LogError(logger, $"Startup circuit opened for {args.BreakDuration}. Error: {args.Outcome.Exception?.Message}");
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    LogInformation(logger, "Startup circuit reset.");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    LogInformation(logger, "Startup circuit half-open; next run is a trial.");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    private static void LogInformation(ILogger? logger, string message)
    {
        if (logger != null)
            logger.LogInformation(message);
        else
            Console.WriteLine($"[HAGI.Robust] {message}");
    }

    private static void LogWarning(ILogger? logger, string message)
    {
        if (logger != null)
            logger.LogWarning(message);
        else
            Console.WriteLine($"[HAGI.Robust] WARNING: {message}");
    }

    private static void LogError(ILogger? logger, string message)
    {
        if (logger != null)
            logger.LogError(message);
        else
            Console.WriteLine($"[HAGI.Robust] ERROR: {message}");
    }
}
