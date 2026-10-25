using System.Collections.Generic;

namespace ReStore.Application.DTOs
{
    /// <summary>Marker base for response DTOs, so the envelopes can be generic over them.</summary>
    public class BaseDto
    {
    }

    /// <summary>
    /// Minimal shape for dropdowns/autocomplete. Property names keep the underscore
    /// prefix from AccountsApi so both codebases teach the same convention.
    /// </summary>
    public class BaseSelectOptionDto : BaseDto
    {
        public decimal? _SelectId { get; set; }

        public string? _SelectValue { get; set; }

        public string? _SelectDisplay { get; set; }

        public object? _AdditionalData { get; set; }
    }

    /// <summary>
    /// What every query envelope must be able to do: be filled from a DTO list or
    /// be turned into an error. BaseQueryHandler works against this, not against a
    /// concrete envelope type.
    /// </summary>
    public interface IBaseDtoSetter<Dto> where Dto : BaseDto
    {
        bool Result { get; set; }

        string? Error { get; set; }

        void SetResultFrom(List<Dto> data, int totalCount, int returnCount);

        void SetErrorResult(string error);
    }

    public abstract class BaseDtoReturn<Dto> : IBaseDtoSetter<Dto> where Dto : BaseDto
    {
        public bool Result { get; set; }

        public string? Error { get; set; }

        /// <summary>Rows matching the criteria, ignoring paging.</summary>
        public int TotalCount { get; set; }

        /// <summary>Rows returned on this page.</summary>
        public int ReturnCount { get; set; }

        public abstract void SetResultFrom(List<Dto> data, int totalCount, int returnCount);

        public void SetErrorResult(string error)
        {
            Result = false;
            Error = error;
        }
    }

    /// <summary>List envelope. This is what a list query returns and what the controller writes to the response body.</summary>
    public class PaginatedResponseListDto<Dto> : BaseDtoReturn<Dto> where Dto : BaseDto
    {
        public IReadOnlyList<Dto> Data { get; set; } = new List<Dto>();

        public override void SetResultFrom(List<Dto> data, int totalCount, int returnCount)
        {
            Result = true;
            Data = data;
            TotalCount = totalCount;
            ReturnCount = returnCount;
        }
    }

    /// <summary>Single-row envelope: the first row of the result, or null.</summary>
    public class DtoReturnOne<Dto> : BaseDtoReturn<Dto> where Dto : BaseDto
    {
        public Dto? Data { get; set; }

        public override void SetResultFrom(List<Dto> data, int totalCount, int returnCount)
        {
            Result = true;
            Data = data.Count > 0 ? data[0] : null;
            TotalCount = totalCount;
            ReturnCount = returnCount;
        }
    }
}
