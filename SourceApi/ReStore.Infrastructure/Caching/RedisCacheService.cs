using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ReStore.Application.Common;
using ReStore.Application.Interfaces;
using StackExchange.Redis;

namespace ReStore.Infrastructure.Caching
{
    /// <summary>
    /// Redis behind <see cref="ICache"/>, written to the fail-soft contract:
    /// <list type="bullet">
    /// <item>it never connects in the constructor, so the app starts without Redis;</item>
    /// <item><c>AbortOnConnectFail = false</c> plus a short connect timeout, so an
    /// unreachable server fails fast instead of hanging a request;</item>
    /// <item>every operation is wrapped: an exception becomes a cache miss and one
    /// warning, never a 500.</item>
    /// </list>
    ///
    /// This is deliberately *not* the upstream pattern (a static IDatabase[] filled at
    /// startup), which throws when Redis is absent.
    /// </summary>
    public sealed class RedisCacheService : ICache, IDisposable
    {
        private readonly RedisSettings _settings;
        private readonly IAppLogger _logger;
        private readonly SemaphoreSlim _connectGate = new(1, 1);

        private IConnectionMultiplexer? _connection;
        private bool _unavailableLogged;

        public RedisCacheService(RedisSettings settings, IAppLogger logger)
        {
            _settings = settings;
            _logger = logger;
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            var database = await TryGetDatabaseAsync(cancellationToken);
            if (database is null)
                return default;

            try
            {
                var value = await database.StringGetAsync(Key(key));
                if (!value.HasValue || value.IsNull)
                    return default;

                return JsonSerializer.Deserialize<T>(value.ToString());
            }
            catch (Exception exception)
            {
                LogUnavailableOnce(exception, nameof(GetAsync));
                return default;
            }
        }

        public async Task<bool> SetAsync<T>(
            string key,
            T value,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default)
        {
            var database = await TryGetDatabaseAsync(cancellationToken);
            if (database is null)
                return false;

            try
            {
                var payload = JsonSerializer.Serialize(value);
                var expiry = timeToLive ?? _settings.DefaultTimeToLive;

                // StackExchange.Redis 2.10 has both a TimeSpan and an Expiration
                // overload; passing the non-nullable TimeSpan picks one unambiguously.
                return expiry.HasValue
                    ? await database.StringSetAsync(Key(key), payload, expiry.Value)
                    : await database.StringSetAsync(Key(key), payload);
            }
            catch (Exception exception)
            {
                LogUnavailableOnce(exception, nameof(SetAsync));
                return false;
            }
        }

        public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            var database = await TryGetDatabaseAsync(cancellationToken);
            if (database is null)
                return false;

            try
            {
                return await database.KeyDeleteAsync(Key(key));
            }
            catch (Exception exception)
            {
                LogUnavailableOnce(exception, nameof(RemoveAsync));
                return false;
            }
        }

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            var database = await TryGetDatabaseAsync(cancellationToken);
            return database is not null && _connection is { IsConnected: true };
        }

        /// <summary>
        /// Connects on first use only. Returns null (and logs once) when Redis is
        /// disabled or unreachable, so every caller degrades to a miss.
        /// </summary>
        private async Task<IDatabase?> TryGetDatabaseAsync(CancellationToken cancellationToken)
        {
            if (!_settings.Enabled)
                return null;

            if (_connection is { IsConnected: true })
                return _connection.GetDatabase();

            await _connectGate.WaitAsync(cancellationToken);
            try
            {
                if (_connection is { IsConnected: true })
                    return _connection.GetDatabase();

                var options = ConfigurationOptions.Parse(_settings.Configuration);
                options.AbortOnConnectFail = false;
                options.ConnectTimeout = _settings.ConnectTimeoutMilliseconds;
                options.SyncTimeout = _settings.SyncTimeoutMilliseconds;
                options.ConnectRetry = _settings.ConnectRetry;

                _connection = await ConnectionMultiplexer.ConnectAsync(options);
                _unavailableLogged = false;

                return _connection.GetDatabase();
            }
            catch (Exception exception)
            {
                LogUnavailableOnce(exception, "connect");
                _connection = null;
                return null;
            }
            finally
            {
                _connectGate.Release();
            }
        }

        private string Key(string key) => _settings.InstanceName + key;

        private void LogUnavailableOnce(Exception exception, string operation)
        {
            if (_unavailableLogged)
                return;

            _unavailableLogged = true;
            _logger.LogWarning(
                $"Redis is unavailable ({operation}); cache calls will miss until it returns. {exception.Message}",
                nameof(RedisCacheService));
        }

        public void Dispose()
        {
            _connection?.Dispose();
            _connectGate.Dispose();
        }
    }
}
