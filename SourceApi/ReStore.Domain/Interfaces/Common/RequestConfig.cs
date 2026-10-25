using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using ReStore.Domain.Common;

namespace ReStore.Domain.Interfaces.Common
{
    /// <summary>
    /// How a handler asks the repository for data: which columns to select, which
    /// rows to match, and how to page/order them.
    ///
    /// The handler builds this; the repository interprets it. Neither knows the
    /// other's implementation, which is what keeps reads testable.
    /// </summary>
    public class RequestPaging
    {
        public int PageNum { get; set; }

        public int PageCount { get; set; }

        /// <summary>Ask for the total row count as well (needed by paged lists).</summary>
        public bool WithTotalCount { get; set; }

        /// <summary>Only count; do not materialise rows.</summary>
        public bool OnlyTotalCount { get; set; }

        public List<OrderInfo>? OrderCols { get; set; }

        public bool DefaultOrderIsAcc { get; set; }

        public int? StartIndex { get; set; }

        public bool DisablePaging { get; set; }
    }

    /// <summary>An ORDER BY column, by name, so the client can choose the sort.</summary>
    public class OrderInfo
    {
        public OrderInfo()
        {
        }

        public OrderInfo(string fieldName, bool isAscending)
        {
            FieldName = fieldName;
            IsAscending = isAscending;
        }

        public string? FieldName { get; set; }

        public bool IsAscending { get; set; }
    }

    /// <summary>Projection + filter + optional max() selector for one query.</summary>
    public class RequestBody<Entity> where Entity : BaseEntity
    {
        public Expression<Func<Entity, bool>>? Criteria { get; set; }

        public Expression<Func<Entity, object>>? Select { get; set; }

        public Func<Entity, int?>? Max { get; set; }
    }

    /// <summary>Non-generic view of the paging part, for callers that do not build a query expression.</summary>
    public class RequestConfigNonGeneric
    {
        public RequestConfigNonGeneric()
        {
            PagingRequest = new RequestPaging();
        }

        public RequestPaging PagingRequest { get; set; }

        public object? Tran { get; set; }
    }

    public class RequestConfig<Entity> : RequestConfigNonGeneric where Entity : BaseEntity
    {
        private RequestConfig()
        {
            Specification = new RequestBody<Entity>();
            PagingRequest = new RequestPaging();
        }

        public RequestConfig(RequestConfigNonGeneric requestConfigNonGeneric)
        {
            Specification = new RequestBody<Entity>();
            PagingRequest = requestConfigNonGeneric.PagingRequest;
        }

        public RequestConfig(Expression<Func<Entity, object>> select) : this()
        {
            Specification.Select = select;
        }

        public RequestConfig(Expression<Func<Entity, bool>> where) : this()
        {
            Specification.Criteria = where;
        }

        public RequestConfig(Expression<Func<Entity, bool>> where, Func<Entity, int?> max) : this(where)
        {
            Specification.Max = max;
        }

        public RequestConfig(Expression<Func<Entity, object>> select, Expression<Func<Entity, bool>> where) : this(select)
        {
            Specification.Criteria = where;
        }

        public RequestConfig(
            Expression<Func<Entity, object>> select,
            Expression<Func<Entity, bool>> where,
            Func<Entity, int?> max) : this(select, where)
        {
            Specification.Max = max;
        }

        public RequestBody<Entity> Specification { get; set; }
    }
}
