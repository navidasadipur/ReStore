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
    /// The update pipeline. Note what it does *not* do: it never loads the whole row.
    /// <see cref="IControls.LastEntityInfo"/> holds a partial entity (key plus the
    /// columns the control asked for), the command is mapped onto it, and only
    /// <see cref="BaseUpdateCommand.ChangeFields"/> are written. A full-form PUT
    /// therefore still becomes a one-column UPDATE when the caller says so.
    /// </summary>
    public abstract class BaseUpdateCommandHandler<Entity, Command> : IRequestHandler<Command, UpdateReturn>
        where Entity : BaseEntity
        where Command : BaseUpdateCommand, IRequest<UpdateReturn>
    {
        protected readonly IGenericCommandRepositoryBase<Entity> _repository;
        protected readonly IControls<Entity> _controls;
        protected readonly IMapperCustome _mapper;
        protected readonly IAppLogger _logger;

        protected BaseUpdateCommandHandler(
            IGenericCommandRepositoryBase<Entity> repository,
            IControls<Entity> controls,
            IMapperCustome mapper,
            IAppLogger logger)
        {
            _repository = repository;
            _controls = controls;
            _mapper = mapper;
            _logger = logger;
        }

        /// <summary>Anything SetResult should report back to the caller (e.g. regenerated keys).</summary>
        protected object? AdditionalInfo { get; set; }

        public async Task<UpdateReturn> Handle(Command command, CancellationToken cancellationToken)
        {
            try
            {
                var begin = BeginHandle(command);
                if (begin is { Result: false })
                    return Fail(begin);

                var commandCheck = await CheckCommand(command);
                if (commandCheck is { Result: false })
                    return Fail(commandCheck);

                var stub = await MapEntityFromRequest(command);

                // The command's key is not necessarily called "Id" on the entity.
                EntityReflection.SetKeyValue(stub, _repository.IdPropertyName, command.Id);

                var controlCheck = await _controls.CheckOnUpdate(stub, command.ChangeFields, command);
                if (controlCheck is { Result: false })
                    return Fail(controlCheck);

                var entity = _controls.LastEntityInfo;
                if (entity is null)
                    return Fail(new DomainResponse("The selected record could not be loaded."));

                _mapper.Map(command, entity);

                var updated = await Update(entity, command, cancellationToken);
                if (updated is { Result: false })
                {
                    _logger.LogError(updated.Error ?? "The record could not be updated.",
                        $"{GetType().Name}.Handle");
                    return Fail(updated);
                }

                return SetResult(new UpdateReturn(updated));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, $"{GetType().Name}.Handle");
                return new UpdateReturn(exception);
            }
        }

        protected virtual DomainResponse? BeginHandle(Command command) => null;

        protected virtual Task<DomainResponse> CheckCommand(Command command)
            => Task.FromResult(new DomainResponse());

        protected virtual Task<Entity> MapEntityFromRequest(Command command)
            => Task.FromResult(_mapper.Map<Command, Entity>(command));

        protected virtual Task<DomainResponse> Update(
            Entity entity,
            Command command,
            CancellationToken cancellationToken)
            => _repository.UpdateAsync(entity, command.ChangeFields, cancellationToken);

        protected virtual UpdateReturn SetResult(UpdateReturn result)
        {
            if (AdditionalInfo is not null)
                result.AdditionalInfo = AdditionalInfo;

            return result;
        }

        private static UpdateReturn Fail(BaseDomainResponse response)
            => new((response.Error ?? "Unknown error.", response.ErrorCode ?? string.Empty));
    }
}
