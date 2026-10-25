using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using ReStore.Application.Common;
using ReStore.Application.Interfaces;
using ReStore.Domain.Common;
using ReStore.Domain.Interfaces.Common;
using ReStore.Domain.Interfaces.Repository;

namespace ReStore.Application.Handlers.BaseHandlers
{
    /// <summary>
    /// Soft delete: flips <see cref="IHasInactive.Inactive"/> instead of removing the
    /// row. It exists now, unused, because the pipeline is part of the architecture we
    /// are adopting; it starts working the moment an entity implements IHasInactive
    /// (Product is the obvious first candidate, but that is a product decision).
    /// </summary>
    public abstract class BaseInActiveHandler<Entity, Command> : IRequestHandler<Command, UpdateReturn>
        where Entity : BaseEntity, IHasInactive
        where Command : InactiveCommand, IRequest<UpdateReturn>
    {
        protected const string InactiveColumn = nameof(IHasInactive.Inactive);

        protected readonly IGenericCommandRepositoryBase<Entity> _repository;
        protected readonly IControls<Entity> _controls;
        protected readonly IAppLogger _logger;

        protected BaseInActiveHandler(
            IGenericCommandRepositoryBase<Entity> repository,
            IControls<Entity> controls,
            IAppLogger logger)
        {
            _repository = repository;
            _controls = controls;
            _logger = logger;
        }

        public async Task<UpdateReturn> Handle(Command command, CancellationToken cancellationToken)
        {
            try
            {
                var stub = Activator.CreateInstance<Entity>();
                EntityReflection.SetKeyValue(stub, _repository.IdPropertyName, command.Id);

                // The control runs with the single column we intend to change, so its
                // validation only sees what is actually being touched.
                command.ChangeFields = new List<string> { InactiveColumn };

                var controlCheck = await _controls.CheckOnUpdate(stub, command.ChangeFields, command);
                if (controlCheck is { Result: false })
                    return Fail(controlCheck);

                var entity = _controls.LastEntityInfo;
                if (entity is null)
                    return Fail(new DomainResponse("The selected record could not be loaded."));

                entity.Inactive = command.Inactive;

                var updated = await _repository.UpdateAsync(entity, command.ChangeFields, cancellationToken);
                if (updated is { Result: false })
                {
                    _logger.LogError(updated.Error ?? "The record could not be deactivated.",
                        $"{GetType().Name}.Handle");
                    return Fail(updated);
                }

                return new UpdateReturn(updated);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, $"{GetType().Name}.Handle");
                return new UpdateReturn(exception);
            }
        }

        private static UpdateReturn Fail(BaseDomainResponse response)
            => new((response.Error ?? "Unknown error.", response.ErrorCode ?? string.Empty));
    }
}
