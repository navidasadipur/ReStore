using System;

namespace ReStore.Application.Interfaces
{
    /// <summary>
    /// Logging seam for the kernel. Base handlers need to record a failed write, but
    /// they must not depend on Microsoft.Extensions.Logging (that would drag a
    /// package into Application for one method). Infrastructure adapts ILogger to it.
    /// </summary>
    public interface IAppLogger
    {
        void LogError(Exception exception, string context);

        void LogError(string message, string context);

        void LogWarning(string message, string context);

        void LogInformation(string message);
    }
}
