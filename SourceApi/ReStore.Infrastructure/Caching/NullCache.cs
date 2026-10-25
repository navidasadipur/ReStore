using System;
using System.Threading;
using System.Threading.Tasks;
using ReStore.Application.Interfaces;

namespace ReStore.Infrastructure.Caching
{
    /// <summary>
    /// The cache that is registered today (decision D9). Every call is a miss and
    /// every write reports "not stored"; nothing connects anywhere and nothing throws.
    ///
    /// Swapping in <see cref="RedisCacheService"/> is one uncommented line in
    /// ServiceRegistration, and the calling handlers do not change.
    /// </summary>
    public sealed class NullCache : ICache
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<T?>(default);

        public Task<bool> SetAsync<T>(
            string key,
            T value,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
