using ReStore.Domain.Interfaces.Common;

namespace ReStore.Application.Interfaces
{
    /// <summary>
    /// Resolves the repository named by a <see cref="ReStore.Domain.Common.CheckKeyExistsAttribute"/>
    /// and asks it whether a key exists.
    ///
    /// This is the one place in the design that needs to resolve a type at runtime: the
    /// attribute can only name a repository as a <c>Type</c>, so the handler cannot
    /// inject it as a generic. Keeping that lookup behind this interface means the
    /// handlers still take their dependencies by constructor.
    /// </summary>
    public interface IKeyExistenceChecker
    {
        Task<DomainResponse> CheckAsync(
            Type repositoryType,
            object value,
            CancellationToken cancellationToken = default);
    }
}
