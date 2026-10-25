using System;
using System.Collections.Generic;
using System.IO;
using ReStore.Domain.Interfaces.Common;

namespace ReStore.Application.Common
{
    #region Commands

    /// <summary>Marker for "create one row" commands. Nothing on it: the handler only needs the type.</summary>
    public abstract class BaseAddCommand
    {
    }

    /// <summary>
    /// Marker for commands only an administrator may run. Upstream uses it to skip
    /// the account-expiry and document checks; ReStore has neither, so it is
    /// carried for fidelity and for future policy checks.
    /// </summary>
    public interface IAdminCommand
    {
    }

    /// <summary>
    /// Update commands carry the key and — importantly — <see cref="ChangeFields"/>:
    /// the list of columns the caller actually wants written. The repository marks
    /// only those properties as modified, so a full-form PUT still becomes a
    /// single-column UPDATE when the caller says so.
    /// </summary>
    public abstract class BaseUpdateCommand
    {
        public int Id { get; set; }

        public List<string>? ChangeFields { get; set; }
    }

    /// <summary>
    /// Add command that carries a file. Streams stay in the Application layer: the
    /// controller converts IFormFile, so nothing here references ASP.NET Core.
    /// </summary>
    public abstract class AddFileCommand : BaseAddCommand
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public Stream Stream { get; set; } = Stream.Null;

        [System.Text.Json.Serialization.JsonIgnore]
        public string ContentType { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;
    }

    public abstract class BaseDeleteCommand
    {
        public int Id { get; set; }
    }

    /// <summary>Deactivate instead of delete. Requires an entity implementing IHasInactive.</summary>
    public abstract class InactiveCommand : BaseUpdateCommand
    {
        public bool Inactive { get; set; }
    }

    #endregion

    #region Returns

    public enum ReturnResult
    {
        Failed = 0,
        Success = 1,
        Warning = 2
    }

    /// <summary>Something the caller should show but which does not block the operation.</summary>
    public class WarningInfo
    {
        public WarningInfo()
        {
            Name = string.Empty;
            Message = string.Empty;
        }

        public WarningInfo(string name, string message)
        {
            Name = name;
            Message = message;
        }

        public string Name { get; set; }

        public string Message { get; set; }
    }

    /// <summary>
    /// The response envelope. Controllers return this as-is (decision D1: HTTP 200,
    /// result in the body), so a failure never becomes an exception or a status code.
    /// </summary>
    public abstract class BaseReturn
    {
        protected BaseReturn()
        {
            Result = ReturnResult.Success;
        }

        protected BaseReturn(string error)
        {
            Error = error;
            Result = ReturnResult.Failed;
        }

        protected BaseReturn((string Error, string ErrorCode) error) : this(error.Error)
        {
            ErrorCode = error.ErrorCode;
        }

        protected BaseReturn(BaseDomainResponse domainResponse)
        {
            Result = domainResponse.Result ? ReturnResult.Success : ReturnResult.Failed;
            Error = domainResponse.Error;
            ErrorCode = domainResponse.ErrorCode;
        }

        protected BaseReturn(Exception exception)
        {
            Error = exception.Message;
            Result = ReturnResult.Failed;
        }

        public ReturnResult Result { get; set; }

        public string? Error { get; set; }

        public string? ErrorCode { get; set; }

        public List<WarningInfo> Warnings { get; set; } = new();

        public object? AdditionalInfo { get; set; }

        public object? AdditionalInfo2 { get; set; }

        public bool Succeeded => Result != ReturnResult.Failed;

        public string GetError() => (Error ?? string.Empty) + Environment.NewLine;
    }

    public class InsertReturn : BaseReturn
    {
        public InsertReturn()
        {
        }

        public InsertReturn(int key)
        {
            Key = key;
        }

        public InsertReturn(int key, BaseDomainResponse domainResponse) : base(domainResponse)
        {
            Key = key;
        }

        public InsertReturn((string Error, string ErrorCode) error) : base(error)
        {
        }

        public InsertReturn(Exception exception) : base(exception)
        {
        }

        /// <summary>The key of the row that was created.</summary>
        public int Key { get; set; }
    }

    public class UpdateReturn : BaseReturn
    {
        public UpdateReturn()
        {
        }

        public UpdateReturn(string error) : base(error)
        {
        }

        public UpdateReturn((string Error, string ErrorCode) error) : base(error)
        {
        }

        public UpdateReturn(BaseDomainResponse domainResponse) : base(domainResponse)
        {
        }

        public UpdateReturn(Exception exception) : base(exception)
        {
        }
    }

    public class DeleteReturn : BaseReturn
    {
        public DeleteReturn()
        {
        }

        public DeleteReturn(string error) : base(error)
        {
        }

        public DeleteReturn((string Error, string ErrorCode) error) : base(error)
        {
        }

        public DeleteReturn(BaseDomainResponse domainResponse) : base(domainResponse)
        {
        }

        public DeleteReturn(Exception exception) : base(exception)
        {
        }

        public List<DeleteResult> DeleteResults { get; set; } = new();
    }

    public class DeleteResult
    {
        public DeleteResult(int key, bool isDeleted, string? error = null, string? errorCode = null)
        {
            Key = key;
            IsDeleted = isDeleted;
            Error = error;
            ErrorCode = errorCode;
        }

        public int Key { get; set; }

        public bool IsDeleted { get; set; }

        public string? Error { get; set; }

        public string? ErrorCode { get; set; }
    }

    #endregion
}
