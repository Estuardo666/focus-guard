namespace FocusGuard.Domain;

public sealed record ValidationError(string Code, string Message);

public sealed record ValidationResult<T>(T? Value, IReadOnlyList<ValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static ValidationResult<T> Success(T value) =>
        new(value, Array.Empty<ValidationError>());

    public static ValidationResult<T> Failure(params ValidationError[] errors) =>
        new(default, errors);
}
