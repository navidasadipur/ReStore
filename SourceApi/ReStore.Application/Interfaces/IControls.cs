using System.Collections.Generic;
using System.Threading.Tasks;
using ReStore.Application.Common;
using ReStore.Domain.Common;
using ReStore.Domain.Interfaces.Common;

namespace ReStore.Application.Interfaces
{
    /// <summary>
    /// Per-entity rules, in one place, so handlers stay about flow and controls stay
    /// about validity. A control answers "may this happen?" and never writes.
    /// </summary>
    public interface IControls<Entity> where Entity : BaseEntity
    {
        /// <summary>
        /// The row that <see cref="IsKeyExistsAsync{Command}"/> last loaded. It is a
        /// *partial* entity — key plus whatever GetSelectForKey projects — which is
        /// exactly what an update needs: attach it, mark the changed columns, save.
        /// </summary>
        Entity? LastEntityInfo { get; }

        Task<DomainResponse> CheckOnAdd(Entity entity);

        Task<DomainResponse> CheckOnUpdate<Command>(
            Entity entity,
            IReadOnlyCollection<string>? changeFields,
            Command command) where Command : BaseUpdateCommand;

        Task<DomainResponse> CheckOnDelete<Command>(
            Entity entity,
            Command command) where Command : BaseDeleteCommand;

        /// <summary>Loads the row into <see cref="LastEntityInfo"/> and reports whether it exists.</summary>
        Task<(bool IsExists, string? Error)> IsKeyExistsAsync<Command>(
            Entity entity,
            Command command) where Command : class;
    }
}
