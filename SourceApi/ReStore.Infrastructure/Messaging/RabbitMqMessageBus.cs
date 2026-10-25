using System;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using ReStore.Application.Common;
using ReStore.Application.Interfaces;

namespace ReStore.Infrastructure.Messaging
{
    /// <summary>
    /// RabbitMQ behind <see cref="IMessageBus"/>, written to the fail-soft contract.
    ///
    /// The important decision: this does **not** use MassTransit's hosted service
    /// (<c>AddMassTransit</c> + <c>AddBus</c>), because that starts the bus during
    /// <c>IHost.StartAsync</c> and fails host startup when the broker is unreachable —
    /// exactly the exception we must not have. Instead the bus is built and started on
    /// the first publish, inside a bounded timeout, and discarded on failure so the
    /// next publish retries. Startup therefore has zero broker dependency.
    /// </summary>
    public sealed class RabbitMqMessageBus : IMessageBus, IDisposable
    {
        private readonly RabbitMqSettings _settings;
        private readonly IAppLogger _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private IBusControl? _bus;
        private bool _unavailableLogged;

        public RabbitMqMessageBus(RabbitMqSettings settings, IAppLogger logger)
        {
            _settings = settings;
            _logger = logger;
        }

        public async Task<bool> PublishAsync<T>(T message, CancellationToken cancellationToken = default)
            where T : class
        {
            if (!_settings.Enabled || message is null)
                return false;

            var bus = await TryGetBusAsync(cancellationToken);
            if (bus is null)
                return false;

            try
            {
                await bus.Publish(message, cancellationToken);
                return true;
            }
            catch (Exception exception)
            {
                LogUnavailableOnce(exception, nameof(PublishAsync));
                return false;
            }
        }

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            if (!_settings.Enabled)
                return false;

            var bus = await TryGetBusAsync(cancellationToken);
            return bus is not null;
        }

        private async Task<IBusControl?> TryGetBusAsync(CancellationToken cancellationToken)
        {
            if (_bus is not null)
                return _bus;

            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_bus is not null)
                    return _bus;

                var bus = Bus.Factory.CreateUsingRabbitMq(configurator =>
                {
                    configurator.Host(
                        _settings.Host,
                        _settings.Port,
                        _settings.VirtualHost,
                        host =>
                        {
                            host.Username(_settings.Username);
                            host.Password(_settings.Password);
                        });
                });

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_settings.StartTimeoutMilliseconds);

                await bus.StartAsync(timeout.Token);

                _bus = bus;
                _unavailableLogged = false;

                return _bus;
            }
            catch (Exception exception)
            {
                LogUnavailableOnce(exception, "start");
                _bus = null;
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }

        private void LogUnavailableOnce(Exception exception, string operation)
        {
            if (_unavailableLogged)
                return;

            _unavailableLogged = true;
            _logger.LogWarning(
                $"RabbitMQ is unavailable ({operation}); messages will not be published until it returns. {exception.Message}",
                nameof(RabbitMqMessageBus));
        }

        public void Dispose()
        {
            try
            {
                _bus?.StopAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
            }
            catch
            {
                // Shutdown must never throw: the broker may already be gone.
            }

            _gate.Dispose();
        }
    }
}
