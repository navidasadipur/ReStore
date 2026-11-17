using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using ReStore.Application.Common;
using ReStore.Application.Interfaces;
using ReStore.Domain.Common;
using ReStore.Domain.Interfaces.Common;
using ReStore.Domain.Interfaces.Repository;

namespace ReStore.Application.Controls
{
    /// <summary>
    /// Default implementation of <see cref="IControls{Entity}"/>. It does two things:
    /// validates the properties marked with <see cref="ControlAttribute"/>, and loads
    /// the current row (partially) so an update has something to attach to.
    ///
    /// Business rules go in the protected virtual hooks; a feature overrides only the
    /// ones it needs.
    /// </summary>
    public class BaseControls<Entity> : IControls<Entity> where Entity : BaseEntity
    {
        private readonly IGenericQueryRepositoryBase<Entity> _repository;
        private Entity? _keyEntityData;

        public BaseControls(IGenericQueryRepositoryBase<Entity> repository)
            => _repository = repository;

        public Entity? LastEntityInfo => _keyEntityData;

        public async Task<DomainResponse> CheckOnAdd(Entity entity)
        {
            var attributeCheck = ControlAttributeValidator.Validate(entity);
            var customCheck = await CheckOnAddInternal(entity);

            return new DomainResponse().AddTo(customCheck).AddTo(attributeCheck);
        }

        public async Task<DomainResponse> CheckOnUpdate<Command>(
            Entity entity,
            IReadOnlyCollection<string>? changeFields,
            Command command) where Command : BaseUpdateCommand
        {
            var existence = await IsKeyExistsAsync(entity, command);
            if (!existence.IsExists)
                return new DomainResponse(existence.Error ?? "The selected record could not be loaded.");

            if (changeFields is null || changeFields.Count == 0)
                return new DomainResponse("No columns were selected for update.");

            var attributeCheck = ControlAttributeValidator.Validate(entity, changeFields);
            var customCheck = await CheckOnUpdateInternal(entity, changeFields, command);

            return new DomainResponse().AddTo(customCheck).AddTo(attributeCheck);
        }

        public async Task<DomainResponse> CheckOnDelete<Command>(
            Entity entity,
            Command command) where Command : BaseDeleteCommand
        {
            var existence = await IsKeyExistsAsync(entity, command);
            if (!existence.IsExists)
                return new DomainResponse(existence.Error ?? "The selected record could not be loaded.");

            return await CheckOnDeleteInternal(entity);
        }

        public async Task<(bool IsExists, string? Error)> IsKeyExistsAsync<Command>(
            Entity entity,
            Command command) where Command : class
        {
            var id = EntityReflection.GetKeyValueAsInt(entity, _repository.IdPropertyName);

            var config = GetSelectForKey() is { } select
                ? new RequestConfig<Entity>(select)
                : null;

            var response = await _repository.GetByIdAsync(id, config);

            if (!response.Result)
                return (false, response.Error ?? "The record was not found.");

            _keyEntityData = response.Data;

            return response.Data is null
                ? (false, "The record was not found.")
                : (true, null);
        }

        /// <summary>
        /// Columns loaded for the row an update will modify. Null means "key only",
        /// which is enough with a partial update but useless when a business rule
        /// needs another column — override it in that case.
        /// </summary>
        protected virtual Expression<Func<Entity, object>>? GetSelectForKey() => null;

        protected virtual Task<DomainResponse> CheckOnAddInternal(Entity entity)
            => Task.FromResult(new DomainResponse());

        protected virtual Task<DomainResponse> CheckOnUpdateInternal<Command>(
            Entity entity,
            IReadOnlyCollection<string> changeFields,
            Command command) where Command : BaseUpdateCommand
            => Task.FromResult(new DomainResponse());

        protected virtual Task<DomainResponse> CheckOnDeleteInternal(Entity entity)
            => Task.FromResult(new DomainResponse());
    }
}
