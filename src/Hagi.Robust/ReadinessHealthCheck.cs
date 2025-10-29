using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hagi.Robust
{
    /// <summary>
    /// Health check that reports Unhealthy until dependency probes have succeeded once.
    /// DependencyWaiter flips the Ready flag when all mandatory dependencies are reachable.
    /// </summary>
    internal sealed class ReadinessHealthCheck : IHealthCheck
    {
        /// <summary>
        /// Returns Healthy only after DependencyWaiter.Ready is set to true.
        /// </summary>
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (!DependencyWaiter.Ready)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("Dependencies not ready."));
            }

            return Task.FromResult(HealthCheckResult.Healthy("Dependencies ready."));
        }
    }
}