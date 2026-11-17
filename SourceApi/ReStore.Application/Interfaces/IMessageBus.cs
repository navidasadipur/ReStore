using System.Threading;
using System.Threading.Tasks;

namespace ReStore.Application.Interfaces
{
    /// <summary>
    /// Message bus seam. Like <see cref="ICache"/>, publishing must never throw: a
    /// broker outage must not fail the request that produced the event. The return
    /// value says whether the message was actually handed over, and the failure has
    /// already been logged by then.
    ///
    /// Registered as NullMessageBus today; RabbitMqMessageBus replaces it by
    /// uncommenting one line in ServiceRegistration (decision D9).
    /// </summary>
    public interface IMessageBus
    {
        /// <summary>Publishes a message. False means "not delivered, already logged"; never throws.</summary>
        Task<bool> PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class;

        Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    }
}
