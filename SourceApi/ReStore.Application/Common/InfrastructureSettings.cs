namespace ReStore.Application.Common
{
    /// <summary>
    /// Redis connection settings. Bound by hand from IConfiguration in
    /// ServiceRegistration (no Microsoft.Extensions.Configuration.Binder dependency).
    ///
    /// <see cref="Enabled"/> is the second gate: even with the real adapter registered,
    /// setting false keeps it inert while the server has no Redis.
    /// </summary>
    public class RedisSettings
    {
        public bool Enabled { get; set; }

        /// <summary>StackExchange.Redis configuration string, e.g. "localhost:6379".</summary>
        public string Configuration { get; set; } = "localhost:6379";

        /// <summary>Prefix applied to every key, so two apps can share one Redis.</summary>
        public string InstanceName { get; set; } = "restore:";

        public int ConnectTimeoutMilliseconds { get; set; } = 1000;

        public int SyncTimeoutMilliseconds { get; set; } = 1000;

        public int ConnectRetry { get; set; } = 3;

        /// <summary>Default TTL for entries stored without an explicit one. Null = no expiry.</summary>
        public System.TimeSpan? DefaultTimeToLive { get; set; }
    }

    /// <summary>
    /// RabbitMQ settings. Bound by hand from IConfiguration, same rule as Redis:
    /// <see cref="Enabled"/> must be true before any connection is attempted.
    /// </summary>
    public class RabbitMqSettings
    {
        public bool Enabled { get; set; }

        public string Host { get; set; } = "localhost";

        public ushort Port { get; set; } = 5672;

        public string VirtualHost { get; set; } = "/";

        public string Username { get; set; } = "guest";

        public string Password { get; set; } = "guest";

        /// <summary>Queue the message bus publishes to.</summary>
        public string QueueName { get; set; } = "restore.messages";

        /// <summary>How long a publish waits for the broker before reporting failure.</summary>
        public int StartTimeoutMilliseconds { get; set; } = 3000;
    }
}
