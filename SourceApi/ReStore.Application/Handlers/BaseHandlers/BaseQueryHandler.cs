using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using ReStore.Application.Common;
using ReStore.Application.DTOs;
using ReStore.Application.Filters;
using ReStore.Application.Interfaces;
using ReStore.Domain.Common;
using ReStore.Domain.Interfaces.Common;
using ReStore.Domain.Interfaces.Repository;

namespace ReStore.Application.Handlers.BaseHandlers
{
    /// <summary>
    /// The list-query pipeline: build one criteria expression, hand the repository a
    /// RequestConfig, map the partially-projected entities to DTOs, return the list
    /// envelope with counts.
    ///
    /// Two details worth knowing:
    /// <list type="bullet">
    /// <item><see cref="SelectExpr"/> decides which columns are read. The repository
    /// projects with it and maps the anonymous shape back onto the entity, so a list
    /// query never selects columns it will not use.</item>
    /// <item>Ordering and paging are *not* applied here: they travel inside
    /// RequestConfig, because applying them needs the provider (and its SQL dialect).</item>
    /// </list>
    /// </summary>
    public abstract class BaseQueryHandler<Entity, Query, DtoReturn, Dto> : IRequestHandler<Query, DtoReturn>
        where Entity : BaseEntity
        where Query : BaseQuery, IRequest<DtoReturn>
        where DtoReturn : IBaseDtoSetter<Dto>, new()
        where Dto : BaseDto
    {
        protected const int DefaultPageSize = 25;
        protected const int MaxPageSize = 200;

        protected readonly IGenericQueryRepositoryBase<Entity> _repository;
        protected readonly IMapperCustome _mapper;
        protected readonly IAppLogger _logger;

        protected BaseQueryHandler(
            IGenericQueryRepositoryBase<Entity> repository,
            IMapperCustome mapper,
            IAppLogger logger)
        {
            _repository = repository;
            _mapper = mapper;
            _logger = logger;
        }

        /// <summary>Columns to read. Keep it in step with the DTO's property names.</summary>
        protected abstract Expression<Func<Entity, object>> SelectExpr { get; }

        /// <summary>The stored procedure for the free-text phrase: which columns it searches.</summary>
        protected abstract Expression<Func<Entity, bool>> FullSearchExpr(Query query);

        /// <summary>Entity-specific filtering, e.g. "only this brand".</summary>
        protected virtual Task<Expression<Func<Entity, bool>>?> GetAdditionalCriteria(Query query)
            => Task.FromResult<Expression<Func<Entity, bool>>?>(null);

        /// <summary>Rejects a malformed query before it reaches the database.</summary>
        protected virtual DomainResponse? ValidateQuery(Query query) => null;

        protected virtual PagingInfo BuildPaging(Query query) => query.PagingInfo ?? new PagingInfo();

        protected virtual Task<DomainResponseList<Entity>> GetList(
            RequestConfig<Entity> requestConfig,
            Query query,
            CancellationToken cancellationToken)
            => _repository.ListAsync(requestConfig, cancellationToken);

        protected virtual Task<List<Dto>> MapDto(List<Entity> data, Query query)
            => Task.FromResult(_mapper.MapList<Entity, Dto>(data));

        protected virtual DtoReturn GetResult(List<Dto> data, int totalCount, int returnCount)
        {
            var result = new DtoReturn();
            result.SetResultFrom(data, totalCount, returnCount);
            return result;
        }

        public async Task<DtoReturn> Handle(Query query, CancellationToken cancellationToken)
        {
            try
            {
                var invalid = ValidateQuery(query);
                if (invalid is { Result: false })
                    return Error(invalid.Error);

                var (criteria, criteriaError) = await BuildCriteria(query);
                if (criteriaError is not null)
                    return Error(criteriaError);

                var requestConfig = new RequestConfig<Entity>(SelectExpr);
                if (criteria is not null)
                    requestConfig.Specification.Criteria = criteria;

                ApplyPaging(requestConfig, query, BuildPaging(query));

                var list = await GetList(requestConfig, query, cancellationToken);
                if (list is { Result: false })
                    return Error(list.Error);

                var data = await MapDto(list.Data, query);
                return GetResult(data, list.TotalCount, list.ReturnCount);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, $"{GetType().Name}.Handle");
                return Error(exception.Message);
            }
        }

        private async Task<(Expression<Func<Entity, bool>>? Criteria, string? Error)> BuildCriteria(Query query)
        {
            Expression<Func<Entity, bool>>? criteria = null;

            if (query.Id.HasValue)
                criteria = BuildKeyCriteria(query.Id.Value);

            criteria = criteria.And(await GetAdditionalCriteria(query));

            if (!string.IsNullOrWhiteSpace(query.FullSearchPhrase))
                criteria = criteria.And(FullSearchExpr(query));

            var (filters, filterError) = FilterExpressionBuilder.Build<Entity>(query.Filters);
            if (filterError is not null)
                return (null, filterError);

            if (filters is not null)
                criteria = criteria.And(filters);

            return (criteria, null);
        }

        /// <summary>
        /// x => x.Key == id, built by name so it works whatever the key is called.
        /// This is the one place that needs the key name at runtime.
        /// </summary>
        private Expression<Func<Entity, bool>> BuildKeyCriteria(int id)
        {
            var parameter = Expression.Parameter(typeof(Entity), "entity");
            var keyProperty = Expression.Property(parameter, _repository.IdPropertyName);
            var comparison = Expression.Equal(
                keyProperty,
                Expression.Constant(id, keyProperty.Type));

            return Expression.Lambda<Func<Entity, bool>>(comparison, parameter);
        }

        private void ApplyPaging(RequestConfig<Entity> requestConfig, Query query, PagingInfo paging)
        {
            var page = paging.PageNumber < 1 ? 1 : paging.PageNumber;
            var pageSize = paging.PageRecordCount <= 0
                ? DefaultPageSize
                : Math.Min(paging.PageRecordCount, MaxPageSize);

            var target = requestConfig.PagingRequest;
            target.PageNum = page;
            target.PageCount = pageSize;
            target.WithTotalCount = true;
            target.OnlyTotalCount = paging.OnlyTotalCount;
            target.StartIndex = paging.StartIndex;
            target.DefaultOrderIsAcc = true;

            if (query.OrderInfo is { Count: > 0 })
            {
                target.OrderCols = query.OrderInfo
                    .Where(order => !string.IsNullOrWhiteSpace(order.ColumnName))
                    .Select(order => new Domain.Interfaces.Common.OrderInfo(
                        order.ColumnName!,
                        order.Ascending ?? true))
                    .ToList();
            }
        }

        private DtoReturn Error(string? message)
        {
            var result = new DtoReturn();
            result.SetErrorResult(message ?? "Unknown error.");
            return result;
        }
    }
}
