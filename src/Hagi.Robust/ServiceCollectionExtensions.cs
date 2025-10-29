using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;


namespace Hagi.Robust
{
    /// <summary>
    /// DI extensions to enable baseline robustness:
    /// - Health/Readiness endpoints support
    /// - Dependency waiter (waits for external deps before reporting ready)
    /// - Standard HTTP policies (Timeout, Retry 1-3-9, Circuit Breaker)
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the minimal robustness building blocks.
        /// Call this once in Program.cs (before building the app).
        /// </summary>
        public static IServiceCollection AddHagiResilience(this IServiceCollection services)
        {
            // Add readiness health check. The waiter flips the flag when dependencies are OK.
            services.AddHealthChecks().AddCheck<ReadinessHealthCheck>("readiness", tags: new[] { "ready" });

            // Register the dependency waiter that runs during startup.
            services.AddHostedService<DependencyWaiter>();

            return services;
        }

        /// <summary>
        /// Adds a typed HttpClient and applies the shared robustness policy.
        /// Use this from a service to get consistent resilience on outgoing calls.
        ///
        /// This extension implements retry and circuit breaker for RUNTIME HTTP concerns.
        /// It protects individual HTTP calls made by a service during normal operation.
        ///
        /// NOTE: This is separate from DependencyWaiter which handles STARTUP dependency checks.
        /// Two different concerns require two different resilience pipelines:
        /// - AddResilientHttpClient: Protects individual HTTP calls with timeout, retry, and circuit breaker
        /// - DependencyWaiter: Ensures dependencies (RabbitMQ, Database) are available before service becomes ready
        /// </summary>
        public static IHttpClientBuilder AddResilientHttpClient<TClient, TImplementation>(this IServiceCollection services, string? baseAddress = null)
            where TClient : class
            where TImplementation : class, TClient
        {
            // Register typed client and attach modern Polly 8 resilience handler.
            var clientBuilder = services
                .AddHttpClient<TClient, TImplementation>(client =>
                {
                    if (!string.IsNullOrWhiteSpace(baseAddress))
                    {
                        client.BaseAddress = new Uri(baseAddress);
                    }
                });

            clientBuilder.AddResilienceHandler("HagiRobustPipeline", (builder, context) =>
            {
                var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Hagi.Robust.Http");

                // Strategy order in Polly v8: Timeout -> Retry -> Circuit Breaker
                // This ordering ensures timeout applies to the entire retry sequence, and circuit breaker
                // tracks failures across multiple requests to prevent cascading failures.

                // 1) Add timeout first - provides overall boundary for entire retry sequence
                builder.AddTimeout(TimeSpan.FromSeconds(5));

                // 2) Add retry with 1s, 3s, 9s backoff - as shown in class slides
                builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
                {
                    MaxRetryAttempts = 3,
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .HandleResult(r => r.StatusCode == HttpStatusCode.RequestTimeout || (int)r.StatusCode >= 500),
                    OnRetry = args =>
                    {
                        var reason = args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString() ?? "Unknown";
                        logger.LogWarning("HTTP retry attempt {Attempt} after {Delay}. Reason: {Reason}",
                            args.AttemptNumber + 1, args.RetryDelay, reason);
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
                });

                // 3) Add circuit breaker last - open after 5 failures for 30s
                builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
                {
                    FailureRatio = 1.0,
                    MinimumThroughput = 5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(30),
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .HandleResult(r => r.StatusCode == HttpStatusCode.RequestTimeout || (int)r.StatusCode >= 500),
                    OnOpened = args =>
                    {
                        logger.LogError("HTTP circuit opened for {Duration}.", args.BreakDuration);
                        return ValueTask.CompletedTask;
                    },
                    OnClosed = args =>
                    {
                        logger.LogInformation("HTTP circuit reset.");
                        return ValueTask.CompletedTask;
                    },
                    OnHalfOpened = args =>
                    {
                        logger.LogInformation("HTTP circuit half-open; next call is a trial.");
                        return ValueTask.CompletedTask;
                    }
                });
            });

            return clientBuilder;
        }

        /// <summary>
        /// Maps a conventional readiness endpoint filtering checks tagged as "ready".
        /// Compose/Kubernetes can call this endpoint for health gating.
        /// </summary>
        public static void MapReadinessEndpoint(this WebApplication app, string path = "/health/ready")
        {
            app.MapHealthChecks(path, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("ready")
            });
        }
    }
}
