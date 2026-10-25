using MediatR;
using ReStore.Application.Common;
using ReStore.Application.Interfaces;
using ReStore.Domain.Common;
using ReStore.Domain.Interfaces.Common;
using ReStore.Domain.Interfaces.Repository;
using System.Reflection;

namespace ReStore.Application.Handlers.BaseHandlers
{
    /// <summary>
    /// The create pipeline, in one fixed order so every add command behaves the same:
    /// <code>
    /// BeginHandle → CheckCommand → map command to entity → SetDefaultEntity
    /// → CheckEntity (Controls.CheckOnAdd) → Add → key → SetResult
    /// </code>
    /// A feature overrides only the hooks it needs. Failures are returned, never
    /// thrown: the response envelope carries them (decision D1).
    /// </summary>
    public abstract class BaseAddCommandHandler<Entity, Command> : IRequestHandler<Command, InsertReturn>
        where Entity : BaseEntity
        where Command : BaseAddCommand, IRequest<InsertReturn>
    {
        protected readonly IGenericCommandRepositoryBase<Entity> _repository;
        protected readonly IControls<Entity> _controls;
        protected readonly IMapperCustome _mapper;
        protected readonly IAppLogger _logger;
        private readonly IKeyExistenceChecker _keyExistenceChecker;

        protected BaseAddCommandHandler(
            IGenericCommandRepositoryBase<Entity> repository,
            IControls<Entity> controls,
            IMapperCustome mapper,
            IAppLogger logger,
            IKeyExistenceChecker keyExistenceChecker)
        {
            _repository = repository;
            _controls = controls;
            _mapper = mapper;
            _logger = logger;
            _keyExistenceChecker = keyExistenceChecker;
        }

        public async Task<InsertReturn> Handle(Command command, CancellationToken cancellationToken)
        {
            try
            {
                var begin = BeginHandle(command);
                if (begin is { Result: false })
                    return Fail(begin);

                var commandCheck = await CheckCommand(command);
                if (commandCheck is { Result: false })
                    return Fail(commandCheck);

                var foreignKeyCheck = await CheckKeyExistsFields(command, cancellationToken);
                if (foreignKeyCheck is { Result: false })
                    return Fail(foreignKeyCheck);

                var entity = await MapEntityFromRequest(command);
                await SetDefaultEntity(entity);

                var entityCheck = await CheckEntity(entity);
                if (entityCheck is { Result: false })
                    return Fail(entityCheck);

                var added = await Add(entity, command, cancellationToken);
                if (added is { Result: false })
                {
                    _logger.LogError(added.Error ?? "The record could not be created.",
                        $"{GetType().Name}.Handle");
                    return Fail(added);
                }

                var key = EntityReflection.GetKeyValueAsInt(entity, _repository.IdPropertyName);
                return SetResult(new InsertReturn(key, added));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, $"{GetType().Name}.Handle");
                return new InsertReturn(exception);
            }
        }

        /// <summary>Runs before anything else. Return a failed response to stop the add.</summary>
        protected virtual DomainResponse? BeginHandle(Command command) => null;

        /// <summary>
        /// Honours <see cref="CheckKeyExistsAttribute"/> on the command: every foreign key
        /// the caller supplied is verified before the entity is built, and the failure
        /// names the offending property.
        /// </summary>
        private async Task<DomainResponse?> CheckKeyExistsFields(
            Command command,
            CancellationToken cancellationToken)
        {
            foreach (var property in typeof(Command).GetProperties())
            {
                var attribute = property.GetCustomAttribute<CheckKeyExistsAttribute>();
                if (attribute is null)
                    continue;

                var value = property.GetValue(command);
                if (value is null)
                    continue;

                var response = await _keyExistenceChecker.CheckAsync(
                    attribute.RepositoryType, value, cancellationToken);

                if (response.Result is false)
                    return new DomainResponse($"{response.Error} ({{{property.Name}}})");
            }

            return null;
        }

        /// <summary>Validates the command itself (as opposed to the entity it will become).</summary>
        protected virtual Task<DomainResponse> CheckCommand(Command command)
            => Task.FromResult(new DomainResponse());

        protected virtual Task<Entity> MapEntityFromRequest(Command command)
            => Task.FromResult(_mapper.Map<Command, Entity>(command));

        /// <summary>Sets values the caller must not control, e.g. an owner or a default flag.</summary>
        protected virtual Task SetDefaultEntity(Entity entity) => Task.CompletedTask;

        /// <summary>Extra validation for the entity, on top of Controls.CheckOnAdd.</summary>
        protected virtual DomainResponse? CheckEntityOnAdd(Entity entity) => null;

        protected virtual async Task<DomainResponse> CheckEntity(Entity entity)
        {
            var controlsCheck = _controls is null
                ? new DomainResponse()
                : await _controls.CheckOnAdd(entity);

            return new DomainResponse()
                .AddTo(CheckEntityOnAdd(entity) ?? new DomainResponse())
                .AddTo(controlsCheck);
        }

        protected virtual Task<DomainResponse> Add(Entity entity, Command command, CancellationToken cancellationToken)
            => _repository.AddAsync(entity, cancellationToken);

        protected virtual InsertReturn SetResult(InsertReturn result) => result;

        private static InsertReturn Fail(BaseDomainResponse response)
            => new((response.Error ?? "Unknown error.", response.ErrorCode ?? string.Empty));
    }
}
