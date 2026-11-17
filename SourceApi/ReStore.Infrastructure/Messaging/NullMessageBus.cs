using System.Threading;
using System.Threading.Tasks;
using ReStore.Application.Interfaces;

namespace ReStore.Infrastructure.Messaging
{
    /// <summary>
    /// The bus that is registered today (decision D9): publishing reports "not
    /// delivered" and does nothing else. No connection, no exception, no log noise —
    /// a handler that publishes on a happy path must not write a line per request
    /// while the broker is not installed.
    ///
    /// Swapping in <see cref="RabbitMqMessageBus"/> is one uncommented line in
    /// ServiceRegistration.
    /// </summary>
    public sealed class NullMessageBus : IMessageBus
    {
        public Task<bool> PublishAsync<T>(T message, CancellationToken cancellationToken = default)
            where T : class
            => Task.FromResult(false);

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
