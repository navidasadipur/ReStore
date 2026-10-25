using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using ReStore.Application.Common;
using ReStore.Application.DTOs;
using ReStore.Application.Interfaces;
using ReStore.Domain.Common;
using ReStore.Domain.Interfaces.Common;
using ReStore.Domain.Interfaces.Repository;

namespace ReStore.Application.Handlers.BaseHandlers
{
    /// <summary>
    /// A list query that must return exactly one row.
    ///
    /// It seals the free-text search (a detail lookup is by key, so searching would
    /// only widen the result) and requires the id, so "missing id" becomes a failed
    /// envelope instead of an accidental full-table read.
    /// </summary>
    public abstract class BaseDetailQueryHandler<Entity, Query, DtoReturn, Dto>
        : BaseQueryHandler<Entity, Query, DtoReturn, Dto>
        where Entity : BaseEntity
        where Query : BaseDetailQuery, IRequest<DtoReturn>
        where DtoReturn : IBaseDtoSetter<Dto>, new()
        where Dto : BaseDto
    {
        protected BaseDetailQueryHandler(
            IGenericQueryRepositoryBase<Entity> repository,
            IMapperCustome mapper,
            IAppLogger logger)
            : base(repository, mapper, logger)
        {
        }

        /// <summary>Sealed: a detail query never searches, it matches one key.</summary>
        protected sealed override Expression<Func<Entity, bool>> FullSearchExpr(Query query)
            => _ => true;

        protected override DomainResponse? ValidateQuery(Query query)
            => query.Id is null or <= 0
                ? new DomainResponse("A valid id is required.")
                : null;

        /// <summary>One row is all a detail query can use, whatever the caller asked for.</summary>
        protected override PagingInfo BuildPaging(Query query)
            => new() { PageNumber = 1, PageRecordCount = 1 };
    }
}
