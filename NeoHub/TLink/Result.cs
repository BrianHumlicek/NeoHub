using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace DSC.TLink;

/// <summary>
/// A lightweight, allocation-free result type for TLink operations.
/// Either holds a success value or a <see cref="TLinkError"/>.
/// </summary>
[DebuggerDisplay("{IsSuccess ? \"Ok\" : Error.ToString(),nq}")]
public readonly struct Result<T>
{
    private readonly T _value;
    private readonly TLinkError? _error;

    private Result(T value)
    {
        _value = value;
        _error = null;
    }

    private Result(TLinkError error)
    {
        _value = default!;
        _error = error;
    }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _error is null;

    public bool IsFailure => _error is not null;

    /// <summary>
    /// The success value. Only valid when <see cref="IsSuccess"/> is true.
    /// </summary>
    public T Value => IsSuccess ? _value : throw new InvalidOperationException($"Cannot access Value on a failed result: {_error}");

    /// <summary>
    /// The error. Only valid when <see cref="IsFailure"/> is true.
    /// </summary>
    public TLinkError? Error => _error;

    /// <summary>
    /// Pattern-match on success or failure.
    /// </summary>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<TLinkError, TResult> onFailure) =>
        IsSuccess ? onSuccess(_value) : onFailure(_error!.Value);

    // Implicit conversions for ergonomic usage
    public static implicit operator Result<T>(T value) => new(value);
    public static implicit operator Result<T>(TLinkError error) => new(error);

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(TLinkError error) => new(error);
    public static Result<T> Fail(TLinkPacketException.Code code, string message, string? packetData = null) =>
        new(new TLinkError(code, message, packetData));
}

/// <summary>
/// A lightweight result type for void-returning TLink operations.
/// </summary>
[DebuggerDisplay("{IsSuccess ? \"Ok\" : Error.ToString(),nq}")]
public readonly struct Result
{
    private readonly TLinkError? _error;

    private Result(TLinkError? error) => _error = error;

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _error is null;

    public bool IsFailure => _error is not null;

    public TLinkError? Error => _error;

    public TResult Match<TResult>(Func<TResult> onSuccess, Func<TLinkError, TResult> onFailure) =>
        IsSuccess ? onSuccess() : onFailure(_error!.Value);

    public static Result Ok() => new(null);
    public static Result Fail(TLinkError error) => new(error);
    public static Result Fail(TLinkPacketException.Code code, string message, string? packetData = null) =>
        new(new TLinkError(code, message, packetData));

    public static implicit operator Result(TLinkError error) => Fail(error);
}