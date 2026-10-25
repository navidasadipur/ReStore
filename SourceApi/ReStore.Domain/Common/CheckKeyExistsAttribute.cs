using System;

namespace ReStore.Domain.Common
{
    /// <summary>
    /// Marks a command property that carries a foreign key supplied by the caller.
    /// Before the entity is mapped, the add handler asks the named repository whether
    /// that key exists and reports the property name if it does not — so a bad foreign
    /// key is a readable message instead of a database constraint violation.
    ///
    /// The repository is named as a <see cref="Type"/>, not a string. AccountsApi
    /// resolves a repository by name through its service locator; that is exactly the
    /// by-name lookup ReStore dropped in favour of constructor injection (decision D6),
    /// and a compile-time type is checked by the compiler as a bonus.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class CheckKeyExistsAttribute : Attribute
    {
        public CheckKeyExistsAttribute(Type repositoryType)
        {
            RepositoryType = repositoryType
                ?? throw new ArgumentNullException(nameof(repositoryType));
        }

        /// <summary>Must be a repository registered in DI that implements INonGenericQueryRepositoryBase.</summary>
        public Type RepositoryType { get; }
    }
}
