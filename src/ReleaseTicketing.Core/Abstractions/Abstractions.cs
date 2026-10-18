namespace ReleaseTicketing.Core.Abstractions;

/// <summary>Who is making this call. The API fills it from the Entra token; Azure Functions use SystemUser.</summary>
public interface ICurrentUser
{
    int? UserId { get; }
    string DisplayName { get; }
    string? Email { get; }
    int? ClusterId { get; }
    string? IpAddress { get; }
    bool IsSystem { get; }
    IReadOnlySet<string> Roles { get; }
    bool IsInRole(params string[] roles) => roles.Any(Roles.Contains);
}

public sealed class SystemUser : ICurrentUser
{
    public int? UserId => null;
    public string DisplayName => "SYSTEM";
    public string? Email => null;
    public int? ClusterId => null;
    public string? IpAddress => null;
    public bool IsSystem => true;
    public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
}

public static class CurrentUserExtensions
{
    public static void Require(this ICurrentUser user, params string[] roles)
    {
        if (!user.IsInRole(roles)) throw new Exceptions.ForbiddenException();
    }
}

public interface ITicketNumberGenerator { Task<string> NextAsync(int year, CancellationToken ct); }

public interface IEmailSender { Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct); }

public interface IBlobStore
{
    Task UploadAsync(string path, Stream content, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);
}
