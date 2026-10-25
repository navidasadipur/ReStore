using System.Collections.Generic;

namespace ReStore.Application.Common
{
    /// <summary>
    /// Paged read request. Every list query derives from this, so paging, ordering
    /// and the free-text search phrase are handled once, in BaseQueryHandler.
    /// </summary>
    public abstract class BaseQuery
    {
        /// <summary>Optional filter on the primary key.</summary>
        public virtual int? Id { get; set; }

        public PagingInfo? PagingInfo { get; set; }

        public List<OrderInfo>? OrderInfo { get; set; }

        /// <summary>Free text matched by the query's FullSearchExpr.</summary>
        public string? FullSearchPhrase { get; set; }
    }

    /// <summary>
    /// Read one row by key. The handler checks that Id was supplied and answers with
    /// a failed envelope, so a missing key is a normal user-input error rather than
    /// an exception or an empty list.
    /// </summary>
    public abstract class BaseDetailQuery : BaseQuery
    {
    }

    /// <summary>
    /// Read used between services: paging disabled and the caller supplies its own
    /// criteria, so a client-supplied PagingInfo cannot shrink an internal lookup.
    /// </summary>
    public abstract class BaseInternalQuery
    {
    }

    public class PagingInfo
    {
        public int PageNumber { get; set; } = 1;

        public int PageRecordCount { get; set; } = 25;

        /// <summary>Count only; do not return rows.</summary>
        public bool OnlyTotalCount { get; set; }

        /// <summary>Also return the total row count (what PaginatedResponseListDto needs).</summary>
        public bool WithTotalCount { get; set; }

        public int? StartIndex { get; set; }
    }

    /// <summary>An ORDER BY column chosen by the caller.</summary>
    public class OrderInfo
    {
        public string? ColumnName { get; set; }

        /// <summary>True for ascending. Nullable so a missing value is not silently read as "desc".</summary>
        public bool? Ascending { get; set; }
    }
}
