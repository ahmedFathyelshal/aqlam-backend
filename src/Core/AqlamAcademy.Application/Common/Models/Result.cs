namespace AqlamAcademy.Application.Common.Models;

/// <summary>
/// Classifies a failed result so the API layer can map it to the right HTTP status.
/// </summary>
public enum ResultErrorType
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    Forbidden = 3,
    Conflict = 4,
    Unauthorized = 5
}

public class Result
{
    internal Result(bool succeeded, IEnumerable<string> errors, ResultErrorType errorType = ResultErrorType.None)
    {
        Succeeded = succeeded;
        Errors = errors.ToArray();
        ErrorType = succeeded ? ResultErrorType.None : errorType;
    }

    public bool Succeeded { get; }
    public string[] Errors { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public ResultErrorType ErrorType { get; }

    public static Result Success() => new(true, Array.Empty<string>());
    public static Result Failure(IEnumerable<string> errors) => new(false, errors, ResultErrorType.Validation);
}

public class Result<T> : Result
{
    internal Result(T? data, bool succeeded, IEnumerable<string> errors, ResultErrorType errorType = ResultErrorType.None)
        : base(succeeded, errors, errorType)
    {
        Data = data;
    }

    public T? Data { get; }

    public static Result<T> Success(T data) => new(data, true, Array.Empty<string>());
    public new static Result<T> Failure(IEnumerable<string> errors) => new(default, false, errors, ResultErrorType.Validation);
    public static Result<T> Failure(ResultErrorType errorType, params string[] errors) => new(default, false, errors, errorType);
    public static Result<T> NotFound(string error = "Resource not found.") => new(default, false, new[] { error }, ResultErrorType.NotFound);
    public static Result<T> Unauthorized(string error) => new(default, false, new[] { error }, ResultErrorType.Unauthorized);
    public static Result<T> Conflict(string error) => new(default, false, new[] { error }, ResultErrorType.Conflict);
}
