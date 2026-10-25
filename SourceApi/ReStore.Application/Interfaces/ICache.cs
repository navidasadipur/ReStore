using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReStore.Application.Interfaces
{
    /// <summary>
    /// Cache seam. The contract is deliberately "cannot throw": a cache is an
    /// optimisation, so an outage degrades to a miss and never reaches the caller as
    /// an error. <c>GetAsync</c> returns null when the value is absent *or* the cache
    /// is unavailable; <c>IsAvailableAsync</c> is the only way to tell the difference.
    ///
    /// Registered as NullCache today; RedisCacheService replaces it by uncommenting
    /// one line in ServiceRegistration (decision D9).
    /// </summary>
    public interface ICache
    {
        Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

        /// <summary>Stores a value. Returns false when the write did not happen; never throws.</summary>
        Task<bool> SetAsync<T>(
            string key,
            T value,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default);

        /// <summary>Removes a key. Returns false when the cache is unavailable; never throws.</summary>
        Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

        Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    }
}
