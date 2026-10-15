namespace EPaperDashboard.Utilities;

/// <summary>
/// Outcome of an operation that has no value: either success or a failure with a message.
/// A default instance is a failure, so an uninitialised result never passes as success.
/// </summary>
public readonly struct Result
{
    private readonly string? _error;

    private Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        _error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public string Error => IsFailure
        ? _error ?? Result<object>.UninitialisedError
        : throw new InvalidOperationException("A successful result has no error.");

    public static Result Success() => new(true, null);

    public static Result Failure(string error) => new(false, error);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(string error) => Result<T>.Failure(error);

    /// <summary>Runs <paramref name="operation"/> and turns an exception into a failure described by <paramref name="describe"/>.</summary>
    public static async Task<Result<T>> Try<T>(Func<Task<T>> operation, Func<Exception, string> describe)
    {
        try
        {
            return Success(await operation());
        }
        catch (Exception exception)
        {
            return Failure<T>(describe(exception));
        }
    }

    public static implicit operator Result(string error) => Failure(error);
}

/// <summary>
/// Outcome of an operation: either a value or a failure with a message.
/// A default instance is a failure, so an uninitialised result never passes as success.
/// </summary>
public readonly struct Result<T>
{
    internal const string UninitialisedError = "The result was not initialised.";

    private readonly T? _value;
    private readonly string? _error;

    private Result(bool isSuccess, T? value, string? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"A failed result has no value: {Error}");

    public string Error => IsFailure
        ? _error ?? UninitialisedError
        : throw new InvalidOperationException("A successful result has no error.");

    public static Result<T> Success(T value) => new(true, value, null);

    public static Result<T> Failure(string error) => new(false, default, error);

    public Result<TNext> Map<TNext>(Func<T, TNext> map) =>
        IsSuccess ? Result<TNext>.Success(map(_value!)) : Result<TNext>.Failure(Error);

    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> next) =>
        IsSuccess ? next(_value!) : Result<TNext>.Failure(Error);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<string, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(Error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(string error) => Failure(error);
}

public static class ResultTaskExtensions
{
    public static async Task<Result<TNext>> Map<T, TNext>(this Task<Result<T>> result, Func<T, TNext> map) =>
        (await result).Map(map);
}
