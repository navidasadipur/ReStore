using System.Collections.Generic;

namespace ReStore.Application.Common
{
    /// <summary>
    /// Paged read request. Every list query derives from this, so paging, ordering,
    /// filtering and the free-text search phrase are handled once, in BaseQueryHandler.
    /// </summary>
    public abstract class BaseQuery
    {
        /// <summary>Optional filter on the primary key.</summary>
        public virtual int? Id { get; set; }

        public PagingInfo? PagingInfo { get; set; }

        public List<OrderInfo>? OrderInfo { get; set; }

        /// <summary>
        /// Column filters, ANDed together. A non-zero <see cref="FilterInfo.InnerFilterId"/>
        /// groups filters that are ORed inside the group — see FilterExpressionBuilder.
        /// </summary>
        public List<FilterInfo>? Filters { get; set; }

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

    /// <summary>
    /// The comparison a filter performs. Values match AccountsApi's <c>eFilterType</c>
    /// so a client written against either API sends the same numbers.
    /// </summary>
    public enum eFilterType
    {
        ftEqual = 0,
        ftNotEqual = 1,
        ftGreaterThan = 2,
        ftLessThan = 3,
        ftGreaterThanOrEqual = 4,
        ftLessThanOrEqual = 5,
        ftContains = 6,
        ftIsNull = 7,
        ftNotNull = 8,
        /// <summary>Whole-word equality; treated as ftEqual here.</summary>
        ftWordtoWord = 9,
        ftStartWith = 10,
        ftEndWith = 11,
        /// <summary>Inclusive range: exactly two values.</summary>
        ftBetween = 12,
        /// <summary>Set membership: one or more values.</summary>
        ftIn = 13
    }

    /// <summary>
    /// One column predicate. <see cref="Value"/> is <c>object?</c> because it arrives
    /// from JSON: the builder converts it to the column's type or reports why it cannot.
    /// </summary>
    public class FilterInfo
    {
        /// <summary>Entity property name. Matched case-insensitively; unknown names fail the query.</summary>
        public string? ColName { get; set; }

        public eFilterType? FilterType { get; set; } = eFilterType.ftContains;

        public object? Value { get; set; }

        /// <summary>
        /// Null or 0 means "AND with everything else". Filters sharing the same non-zero
        /// id are ORed together inside one group.
        /// </summary>
        public int? InnerFilterId { get; set; }
    }
}
