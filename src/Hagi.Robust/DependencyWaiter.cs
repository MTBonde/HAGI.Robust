using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Hagi.Robust
{
    /// <summary>
    /// Background service that delays readiness until all registered probes succeed once.
    ///
    /// This class implements retry and circuit breaker for STARTUP concerns (RabbitMQ, Database readiness).
    /// It controls when the service reports itself as "ready" via the /health/ready endpoint.
    ///
    /// NOTE: This is separate from AddResilientHttpClient which handles RUNTIME HTTP resilience.
    /// Two different concerns require two different resilience pipelines:
    /// - DependencyWaiter: Ensures dependencies are available before service starts accepting traffic
    /// - AddResilientHttpClient: Protects individual HTTP calls during normal operation
    /// </summary>
    internal sealed class DependencyWaiter : BackgroundService
    {
        /// <summary>
        /// Global readiness flag toggled true after all probes have succeeded once.
        /// Read by ReadinessHealthCheck to report service readiness.
        /// </summary>
        internal static volatile bool Ready = false;

        // Hold probes and logger for diagnostics.
        private readonly IEnumerable<IStartupProbe> _probes;
        private readonly ILogger<DependencyWaiter> _logger;

        // Pre-built startup resilience pipeline (retry and circuit breaker) per slides.
        private readonly ResiliencePipeline _resiliencePipeline;

        /// <summary>
        /// Initializes the dependency waiter with probes and logging.
        /// </summary>
        public DependencyWaiter(IEnumerable<IStartupProbe> probes, ILogger<DependencyWaiter> logger)
        {
            _probes = probes;
            _logger = logger;

            // Build resilience pipeline with retry and circuit breaker using Polly 8 API.
            _resiliencePipeline = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    // Configure retry with custom exponential backoff delays: 1s, 3s, 9s
                    MaxRetryAttempts = 3,
                    ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                    OnRetry = args =>
                    {
                        // Log transient startup failures to aid diagnosis.
                        _logger.LogWarning(args.Outcome.Exception, "Startup probe retry {Attempt} after {Delay}.",
                            args.AttemptNumber + 1, args.RetryDelay);
                        return ValueTask.CompletedTask;
                    },
                    DelayGenerator = args =>
                    {
                        // Custom delay: 1s, 3s, 9s
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
                    // Configure circuit breaker to open after 5 consecutive failures, break for 30 seconds
                    FailureRatio = 1.0,
                    MinimumThroughput = 5,
                    SamplingDuration = TimeSpan.FromSeconds(10),
                    BreakDuration = TimeSpan.FromSeconds(30),
                    ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                    OnOpened = args =>
                    {
                        _logger.LogError(args.Outcome.Exception, "Startup circuit opened for {Duration}.", args.BreakDuration);
                        return ValueTask.CompletedTask;
                    },
                    OnClosed = args =>
                    {
                        _logger.LogInformation("Startup circuit reset.");
                        return ValueTask.CompletedTask;
                    },
                    OnHalfOpened = args =>
                    {
                        _logger.LogInformation("Startup circuit half-open; next run is a trial.");
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();
        }

        /// <summary>
        /// Executes all probes under retry + breaker until they pass once, then marks Ready.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested && !Ready)
            {
                try
                {
                    // Execute probes under the resilience pipeline (retry + circuit breaker).
                    await _resiliencePipeline.ExecuteAsync(async ct =>
                    {
                        // Run each probe; any exception indicates "not ready yet".
                        foreach (var probe in _probes)
                        {
                            await probe.ProbeAsync(ct);
                        }
                    }, stoppingToken);

                    // If we get here, all probes passed in the same pass.
                    Ready = true;
                    _logger.LogInformation("All startup dependencies are ready; service is now ready.");
                }
                catch (BrokenCircuitException)
                {
                    // Circuit is open; wait briefly before trying again.
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (Exception ex)
                {
                    // Probes failed after retries; small delay before looping.
                    _logger.LogWarning(ex, "Probes failed after retries; will attempt again.");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
        }
    }
}
