namespace Hagi.Robust
{
    /// <summary>
    /// Contract for a minimal startup probe. Implement one per critical dependency
    /// (e.g., RabbitMQ, Database) in the consuming service.
    /// The implementation should throw on failure and return normally on success.
    /// </summary>
    public interface IStartupProbe
    {
        /// <summary>
        /// Probes the dependency and throws if the dependency is not ready.
        /// </summary>
        Task ProbeAsync(CancellationToken ct);
    }
}