namespace Ake.Core;

/// <summary>
/// Beklenen başarısızlıkları exception fırlatmadan taşır. Politika: docs/adr/0002-hata-yonetimi.md
/// </summary>
public class Result
{
    private protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(null);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public static Result<T> Success<T>(T value) => new(value, null);

    public static Result<T> Failure<T>(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error);
    }
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, Error? error)
        : base(error) => _value = value;

    /// <summary>Başarısız sonuçta okunursa <see cref="InvalidOperationException"/> fırlatır.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Başarısız sonucun değeri okunamaz: {Error!.Code}");
}
