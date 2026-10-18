namespace ReleaseTicketing.Core.Exceptions;

/// <summary>Business errors. The API's exception handler turns each type into the right HTTP status.</summary>
public abstract class AppException(string message) : Exception(message);
public class NotFoundException(string message) : AppException(message);            // 404
public class ForbiddenException(string message = "You do not have permission to perform this action.") : AppException(message);  // 403
public class ConflictException(string message) : AppException(message);            // 409
public class BusinessRuleException(string message) : AppException(message);        // 422

public class RequestValidationException : AppException                              // 400
{
    public IDictionary<string, string[]> Errors { get; }
    public RequestValidationException(string field, string message) : base(message)
        => Errors = new Dictionary<string, string[]> { [field] = [message] };
    public RequestValidationException(IDictionary<string, string[]> errors) : base("One or more validation errors occurred.")
        => Errors = errors;
}
