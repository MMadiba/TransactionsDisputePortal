using DisputePortal.Domain.Common;

namespace DisputePortal.Application.Common;

/// <summary>
/// The thing asked for does not exist, or does not belong to the caller. Deliberately the same answer
/// for both, so the API never confirms that someone else's record exists.
/// </summary>
public sealed class NotFoundException(string code, string message) : DomainException(code, message);

/// <summary>Input failed validation. Errors are keyed by field name, like ASP.NET Core's ValidationProblemDetails.</summary>
public sealed class RequestValidationException(IDictionary<string, string[]> errors)
    : DomainException("request.invalid", "One or more fields are invalid.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Raised by the unit of work when an optimistic concurrency check fails.</summary>
public sealed class ConcurrencyConflictException(Exception inner)
    : Exception("The record was changed by someone else.", inner);

/// <summary>Raised by the unit of work when a unique constraint in the store is violated.</summary>
public sealed class UniqueConstraintConflictException(Exception inner)
    : Exception("A uniqueness rule was violated.", inner);
