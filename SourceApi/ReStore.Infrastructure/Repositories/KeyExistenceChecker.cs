using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ReStore.Application.Interfaces;
using ReStore.Domain.Interfaces.Common;
using ReStore.Domain.Interfaces.Repository;

namespace ReStore.Infrastructure.Repositories
{
    /// <summary>
    /// Resolves the repository named by <see cref="ReStore.Domain.Common.CheckKeyExistsAttribute"/>
    /// from DI and delegates to its non-generic existence check.
    ///
    /// A failed lookup is a configuration error, not a user error, so it is reported as
    /// a failed DomainResponse with the type name — the handler turns that into an
    /// envelope, and the message says exactly which repository is missing.
    /// </summary>
    public sealed class KeyExistenceChecker : IKeyExistenceChecker
    {
        private readonly IServiceProvider _serviceProvider;

        public KeyExistenceChecker(IServiceProvider serviceProvider)
            => _serviceProvider = serviceProvider;

        public async Task<DomainResponse> CheckAsync(
            Type repositoryType,
            object value,
            CancellationToken cancellationToken = default)
        {
            if (repositoryType is null)
                return new DomainResponse("A CheckKeyExists attribute was declared without a repository type.");

            var repository = _serviceProvider.GetService(repositoryType) as INonGenericQueryRepositoryBase;

            if (repository is null)
            {
                return new DomainResponse(
                    $"{repositoryType.Name} is not registered, or does not implement "
                    + $"{nameof(INonGenericQueryRepositoryBase)}.");
            }

            return await repository.IsKeyExistsAsync(value!, cancellationToken);
        }
    }
}
