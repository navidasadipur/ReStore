using System;
using System.Collections.Generic;

namespace ReStore.Domain.Interfaces.Common
{
    /// <summary>
    /// The result of a domain operation. Handlers never throw for an expected
    /// failure — they return <c>Result = false</c> and an English message that the
    /// caller puts straight into the response envelope.
    ///
    /// <see cref="AddTo"/> exists so several checks can report at once: the
    /// messages are concatenated instead of the first one winning.
    /// </summary>
    public abstract class BaseDomainResponse
    {
        protected BaseDomainResponse()
        {
            Result = true;
        }

        protected BaseDomainResponse(string error)
        {
            Result = false;
            Error = error;
        }

        protected BaseDomainResponse(string error, string errorCode) : this(error)
        {
            ErrorCode = errorCode;
        }

        protected BaseDomainResponse(Exception exception)
        {
            Result = false;
            Exception = exception;
            Error = exception.Message;
        }

        protected BaseDomainResponse(int lastKey)
        {
            Result = true;
            LastKey = lastKey;
        }

        public bool Result { get; set; }

        public Exception? Exception { get; set; }

        public string? Error { get; set; }

        public string? ErrorCode { get; set; }

        public int LastKey { get; set; }

        public object? AdditionalInfo1 { get; set; }

        public object? AdditionalInfo2 { get; set; }

        /// <summary>Joins another outcome into this one. Failures accumulate, success is a conjunction.</summary>
        public virtual DomainResponse AddTo(DomainResponse target)
        {
            target ??= new DomainResponse();

            if (Error is not null)
                target.Error = target.Error is null ? Error : target.Error + Environment.NewLine + Error;

            if (ErrorCode is not null)
                target.ErrorCode = target.ErrorCode is null ? ErrorCode : target.ErrorCode + Environment.NewLine + ErrorCode;

            if (Exception is not null)
                target.Exception = Exception;

            target.Result = target.Result && Result;

            return target;
        }
    }

    /// <summary>Outcome of a write (add/update/delete) or of a hand-written command.</summary>
    public class DomainResponse : BaseDomainResponse
    {
        public DomainResponse()
        {
        }

        public DomainResponse(string error) : base(error)
        {
        }

        public DomainResponse(string error, string errorCode) : base(error, errorCode)
        {
        }

        public DomainResponse((string Error, string ErrorCode) error) : base(error.Error, error.ErrorCode)
        {
        }

        public DomainResponse(Exception exception) : base(exception)
        {
        }

        public DomainResponse(int lastKey) : base(lastKey)
        {
        }

        public DomainResponse(object? data, int lastKey = 0) : base(lastKey)
        {
            Data = data;
        }

        public DomainResponse(bool isPendingOperated)
        {
            IsPendingOperated = isPendingOperated;
        }

        public DomainResponse(string error, Exception exception, object? data)
        {
            Result = false;
            Error = error;
            Exception = exception;
            Data = data;
        }

        public object? Data { get; set; }

        public bool IsPendingOperated { get; set; }

        public override DomainResponse AddTo(DomainResponse target)
        {
            var result = base.AddTo(target);
            if (Data is not null)
                result.Data = Data;
            return result;
        }
    }

    /// <summary>Outcome carrying exactly one entity; "not found" is a failed response, not an exception.</summary>
    public class DomainResponseOne<Entity> : BaseDomainResponse
    {
        public DomainResponseOne()
        {
        }

        public DomainResponseOne(string error) : base(error)
        {
        }

        public DomainResponseOne(Exception exception) : base(exception)
        {
        }

        public DomainResponseOne(Entity data) : base(0)
        {
            Data = data;
        }

        public Entity? Data { get; set; }
    }

    /// <summary>Outcome carrying a list, plus how many rows the unpaged query matched.</summary>
    public class DomainResponseList<Entity> : BaseDomainResponse
    {
        public DomainResponseList()
        {
            Data = new List<Entity>();
        }

        public DomainResponseList(string error) : base(error)
        {
            Data = new List<Entity>();
        }

        public DomainResponseList(Exception exception) : base(exception)
        {
            Data = new List<Entity>();
        }

        public DomainResponseList(List<Entity> data) : base(0)
        {
            Data = data;
            ReturnCount = data?.Count ?? 0;
        }

        public DomainResponseList(List<Entity> data, int totalCount) : this(data)
        {
            TotalCount = totalCount;
        }

        public List<Entity> Data { get; set; }

        /// <summary>Rows matching the criteria, ignoring paging.</summary>
        public int TotalCount { get; set; }

        /// <summary>Rows actually materialised on this page.</summary>
        public int ReturnCount { get; set; }
    }
}
