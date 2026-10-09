namespace Payroll.Core.Domain;

public class PayrollException : Exception { public PayrollException(string message, Exception? inner = null) : base(message, inner) { } }
/// <summary>A business rule was violated (raised by validation, calculators or stored procedures with error numbers >= 50000).</summary>
public class BusinessRuleException : PayrollException { public int Code { get; } public BusinessRuleException(string message, int code = 50002) : base(message) => Code = code; }
public sealed class NotFoundException : BusinessRuleException { public NotFoundException(string message = "The requested record was not found.") : base(message, 50001) { } }
public sealed class ForbiddenException : BusinessRuleException { public ForbiddenException(string message = "You are not allowed to perform this action.") : base(message, 50020) { } }
public sealed class ConcurrencyException : BusinessRuleException { public ConcurrencyException(string message) : base(message, 50010) { } }
public sealed class DataAccessException : PayrollException { public DataAccessException(string message, Exception inner) : base(message, inner) { } }

public sealed class ValidationFailedException : PayrollException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }
    public ValidationFailedException(IReadOnlyDictionary<string, string[]> errors) : base("One or more validation errors occurred.") => Errors = errors;
    public ValidationFailedException(string field, string message) : this(new Dictionary<string, string[]> { [field] = new[] { message } }) { }
}
