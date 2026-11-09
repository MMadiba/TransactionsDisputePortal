namespace DisputePortal.Domain.Common;

/// <summary>Base type for expected, user-facing failures. Each carries a stable machine-readable code.</summary>
public abstract class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>A business rule stopped the operation (e.g. the transaction is outside the dispute window).</summary>
public sealed class BusinessRuleViolationException(string code, string message) : DomainException(code, message);

/// <summary>The operation clashes with the current state (already disputed, already closed).</summary>
public sealed class DomainConflictException(string code, string message) : DomainException(code, message);
