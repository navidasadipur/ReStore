using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ReStore.Application.Interfaces;
using ReStore.Domain.Common;
using ReStore.Domain.Interfaces.Common;
using ReStore.Domain.Interfaces.Repository;
using ReStore.Infrastructure.Persistence;

namespace ReStore.Infrastructure.Repositories
{
    /// <summary>
    /// Read-side generic repository.
    ///
    /// It is the only place that knows how a <see cref="RequestConfig{TEntity}"/>
    /// becomes SQL: criteria as a Where, OrderCols as an ORDER BY (by name, validated
    /// against the entity's properties), paging as Skip/Take, and Select as a
    /// projection that is mapped back onto the entity with
    /// <see cref="IMapperCustome.MapAnonymous{TDestination}"/>.
    ///
    /// Reads are always AsNoTracking: an update attaches its own stub, so nothing here
    /// should put entities in the change tracker.
    /// </summary>
    public class GenericQueryRepositoryBase<Entity> : IGenericQueryRepositoryBase<Entity>
        where Entity : BaseEntity
    {
        private const int DefaultPageSize = 25;
        private const int MaxPageSize = 200;

        private readonly StoreContext _context;
        private readonly IMapperCustome _mapper;

        public GenericQueryRepositoryBase(StoreContext context, IMapperCustome mapper)
        {
            _context = context;
            _mapper = mapper;
            IdPropertyName = EntityKeyResolver.Resolve<Entity>(context);
        }

        public string IdPropertyName { get; }

        public async Task<DomainResponseOne<Entity>> GetByIdAsync(
            object id,
            RequestConfig<Entity>? requestConfig = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var query = FilterById(id);

                var criteria = requestConfig?.Specification.Criteria;
                if (criteria is not null)
                    query = query.Where(criteria);

                var select = requestConfig?.Specification.Select;
                if (select is null)
                {
                    var entity = await query.SingleOrDefaultAsync(cancellationToken);
                    return entity is null
                        ? new DomainResponseOne<Entity>($"No {typeof(Entity).Name} with key {id} was found.")
                        : new DomainResponseOne<Entity>(entity);
                }

                // Project first, then map the anonymous shape back onto the entity: the
                // row keeps only the columns the caller asked for.
                var projected = await query.Select(select).SingleOrDefaultAsync(cancellationToken);
                var projectedEntity = _mapper.MapAnonymous<Entity>(projected);

                return projectedEntity is null
                    ? new DomainResponseOne<Entity>($"No {typeof(Entity).Name} with key {id} was found.")
                    : new DomainResponseOne<Entity>(projectedEntity);
            }
            catch (Exception exception)
            {
                return new DomainResponseOne<Entity>(exception);
            }
        }

        public async Task<DomainResponseList<Entity>> GetByIdsAsync(
            IEnumerable<object> ids,
            RequestConfig<Entity>? requestConfig = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var idList = ids?.ToList() ?? new List<object>();
                if (idList.Count == 0)
                    return new DomainResponseList<Entity>(new List<Entity>());

                var query = FilterByIds(idList);

                var criteria = requestConfig?.Specification.Criteria;
                if (criteria is not null)
                    query = query.Where(criteria);

                var data = await Materialise(query, requestConfig, cancellationToken);
                return new DomainResponseList<Entity>(data, data.Count);
            }
            catch (Exception exception)
            {
                return new DomainResponseList<Entity>(exception);
            }
        }

        public async Task<DomainResponseList<Entity>> ListAsync(
            RequestConfig<Entity>? requestConfig = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                IQueryable<Entity> query = _context.Set<Entity>().AsNoTracking();

                var criteria = requestConfig?.Specification.Criteria;
                if (criteria is not null)
                    query = query.Where(criteria);

                var paging = requestConfig?.PagingRequest ?? new RequestPaging();

                var totalCount = await query.CountAsync(cancellationToken);

                if (paging.OnlyTotalCount)
                    return new DomainResponseList<Entity>(new List<Entity>(), totalCount);

                query = ApplyOrdering(query, paging, out var orderingError);
                if (orderingError is not null)
                    return new DomainResponseList<Entity>(orderingError);

                if (!paging.DisablePaging)
                {
                    var page = paging.PageNum < 1 ? 1 : paging.PageNum;
                    var pageSize = paging.PageCount <= 0
                        ? DefaultPageSize
                        : Math.Min(paging.PageCount, MaxPageSize);

                    var skip = paging.StartIndex ?? (page - 1) * pageSize;
                    query = query.Skip(skip).Take(pageSize);
                }

                var data = await Materialise(query, requestConfig, cancellationToken);
                return new DomainResponseList<Entity>(data, totalCount);
            }
            catch (Exception exception)
            {
                return new DomainResponseList<Entity>(exception);
            }
        }

        public async Task<DomainResponse> IsKeyExistsAsync(
            object id,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var exists = await FilterById(id).AnyAsync(cancellationToken);

                return exists
                    ? new DomainResponse()
                    : new DomainResponse($"No {typeof(Entity).Name} with key {id} was found.");
            }
            catch (Exception exception)
            {
                return new DomainResponse(exception);
            }
        }

        private async Task<List<Entity>> Materialise(
            IQueryable<Entity> query,
            RequestConfig<Entity>? requestConfig,
            CancellationToken cancellationToken)
        {
            var select = requestConfig?.Specification.Select;

            if (select is null)
                return await query.ToListAsync(cancellationToken);

            var projected = await query.Select(select).ToListAsync(cancellationToken);
            return _mapper.MapAnonymous<List<Entity>>(projected) ?? new List<Entity>();
        }

        /// <summary>e => e.Key == id, built by name so it does not assume "Id".</summary>
        private IQueryable<Entity> FilterById(object id)
        {
            var parameter = Expression.Parameter(typeof(Entity), "entity");
            var keyProperty = Expression.Property(parameter, IdPropertyName);

            var comparison = Expression.Equal(
                keyProperty,
                Expression.Constant(Convert.ChangeType(id, keyProperty.Type), keyProperty.Type));

            return _context.Set<Entity>()
                .AsNoTracking()
                .Where(Expression.Lambda<Func<Entity, bool>>(comparison, parameter));
        }

        /// <summary>e => ids.Contains(e.Key), with the list materialised at the key's type.</summary>
        private IQueryable<Entity> FilterByIds(IReadOnlyList<object> ids)
        {
            var parameter = Expression.Parameter(typeof(Entity), "entity");
            var keyProperty = Expression.Property(parameter, IdPropertyName);
            var keyType = keyProperty.Type;

            var listType = typeof(List<>).MakeGenericType(keyType);
            var typedList = (IList)Activator.CreateInstance(listType)!;
            foreach (var id in ids)
                typedList.Add(Convert.ChangeType(id, keyType));

            var contains = Expression.Call(
                typeof(Enumerable),
                nameof(Enumerable.Contains),
                new[] { keyType },
                Expression.Constant(typedList, listType),
                keyProperty);

            return _context.Set<Entity>()
                .AsNoTracking()
                .Where(Expression.Lambda<Func<Entity, bool>>(contains, parameter));
        }

        /// <summary>
        /// Applies OrderCols with Dynamic LINQ.
        ///
        /// The sort column arrives from the client, so two things happen before it is
        /// used: a blank column is skipped, and a column that is not a public property
        /// of the entity **fails the query with a message naming it** rather than being
        /// silently dropped. EF parameterizes values, so this is not a SQL injection
        /// surface; the real risks are parse errors, sorting by a column the API never
        /// meant to expose, and unbounded sorts on unindexed columns.
        /// </summary>
        private static IQueryable<Entity> ApplyOrdering(
            IQueryable<Entity> query,
            RequestPaging paging,
            out string? error)
        {
            error = null;

            if (paging.OrderCols is not { Count: > 0 })
                return query;

            var clauses = new List<string>();

            foreach (var order in paging.OrderCols.Where(order => !string.IsNullOrWhiteSpace(order.FieldName)))
            {
                if (FindProperty(order.FieldName!) is null)
                {
                    error = $"Unknown sort column '{order.FieldName}' for {typeof(Entity).Name}.";
                    return query;
                }

                clauses.Add($"{order.FieldName} {(order.IsAscending ? "asc" : "desc")}");
            }

            return clauses.Count == 0 ? query : query.OrderBy(string.Join(", ", clauses));
        }

        private static PropertyInfo? FindProperty(string name)
            => typeof(Entity).GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
    }
}
