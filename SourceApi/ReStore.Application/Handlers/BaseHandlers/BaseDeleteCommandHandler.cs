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
    /// The delete pipeline: load the row through <see cref="IControls{T}"/> (which is
    /// also where "this may not be deleted" rules live), then delete it.
    /// </summary>
    public abstract class BaseDeleteCommandHandler<Entity, Command> : IRequestHandler<Command, DeleteReturn>
        where Entity : BaseEntity
        where Command : BaseDeleteCommand, IRequest<DeleteReturn>
    {
        protected readonly IGenericCommandRepositoryBase<Entity> _repository;
        protected readonly IControls<Entity> _controls;
        protected readonly IAppLogger _logger;

        protected BaseDeleteCommandHandler(
            IGenericCommandRepositoryBase<Entity> repository,
            IControls<Entity> controls,
            IAppLogger logger)
        {
            _repository = repository;
            _controls = controls;
            _logger = logger;
        }

        public async Task<DeleteReturn> Handle(Command command, CancellationToken cancellationToken)
        {
            try
            {
                var begin = BeginHandle(command);
                if (begin is { Result: false })
                    return Fail(begin);

                var stub = await GetStub(command);
                EntityReflection.SetKeyValue(stub, _repository.IdPropertyName, command.Id);

                var controlCheck = await _controls.CheckOnDelete(stub, command);
                if (controlCheck is { Result: false })
                    return Fail(controlCheck);

                var entity = _controls.LastEntityInfo;
                if (entity is null)
                    return Fail(new DomainResponse("The selected record could not be loaded."));

                var deleted = await Delete(entity, command, cancellationToken);
                if (deleted is { Result: false })
                {
                    _logger.LogError(deleted.Error ?? "The record could not be deleted.",
                        $"{GetType().Name}.Handle");
                    return Fail(deleted);
                }

                var result = SetResult(new DeleteReturn(deleted));
                result.DeleteResults.Add(new DeleteResult(command.Id, true));
                return result;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, $"{GetType().Name}.Handle");
                return new DeleteReturn(exception);
            }
        }

        protected virtual DomainResponse? BeginHandle(Command command) => null;

        /// <summary>
        /// A carrier for the key only: delete needs nothing but the id, and the row is
        /// loaded by Controls. Override if a pre-delete rule needs values from the command.
        /// </summary>
        protected virtual Task<Entity> GetStub(Command command)
            => Task.FromResult(Activator.CreateInstance<Entity>());

        protected virtual Task<DomainResponse> Delete(
            Entity entity,
            Command command,
            CancellationToken cancellationToken)
            => _repository.DeleteAsync(entity, cancellationToken);

        protected virtual DeleteReturn SetResult(DeleteReturn result) => result;

        private static DeleteReturn Fail(BaseDomainResponse response)
            => new((response.Error ?? "Unknown error.", response.ErrorCode ?? string.Empty));
    }
}
