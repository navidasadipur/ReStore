using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ReStore.Application.Interfaces;

namespace ReStore.Infrastructure.Services
{
    /// <summary>Adapts <see cref="ILogger{T}"/> to the kernel's <see cref="IAppLogger"/>.</summary>
    public sealed class AppLogger : IAppLogger
    {
        private readonly ILogger<AppLogger> _logger;

        public AppLogger(ILogger<AppLogger> logger) => _logger = logger;

        public void LogError(Exception exception, string context)
            => _logger.LogError(exception, "{Context}", context);

        public void LogError(string message, string context)
            => _logger.LogError("{Context}: {Message}", context, message);

        public void LogWarning(string message, string context)
            => _logger.LogWarning("{Context}: {Message}", context, message);

        public void LogInformation(string message)
            => _logger.LogInformation("{Message}", message);
    }
}
