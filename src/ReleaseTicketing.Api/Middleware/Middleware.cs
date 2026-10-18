using System.Security.Claims;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReleaseTicketing.Api.Authorization;
using ReleaseTicketing.Core.Domain;
using ReleaseTicketing.Core.Exceptions;

namespace ReleaseTicketing.Api.Middleware;

/// <summary>Gives every request a correlation id, returns it in a header and attaches it to every log line.</summary>
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> log)
{
    public async Task Invoke(HttpContext ctx)
    {
        var id = ctx.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? ctx.TraceIdentifier;
        ctx.Response.Headers["X-Correlation-ID"] = id;
        using (log.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
            await next(ctx);
    }
}

/// <summary>After the token is validated, loads/creates the user row and fills CurrentUserService.</summary>
public class UserContextMiddleware(RequestDelegate next, IWebHostEnvironment env, IConfiguration config)
{
    public async Task Invoke(HttpContext ctx, CurrentUserService current)
    {
        ClaimsPrincipal principal = ctx.User;
        if (!ctx.User.Identity?.IsAuthenticated == true && env.IsDevelopment())
        {
            var requestedRole = ctx.Request.Headers["X-Local-Dev-Role"].FirstOrDefault();
            var configuredRole = requestedRole ?? config["LocalDevelopment:Role"];
            var localRole = RoleNames.Normalize(configuredRole ?? "") ?? RoleNames.Pm;
            var localClusterId = ctx.Request.Headers["X-Local-Dev-Cluster-Id"].FirstOrDefault()
                ?? (string.IsNullOrWhiteSpace(requestedRole) ? config["LocalDevelopment:ClusterId"] : null);
            var clusterSuffix = int.TryParse(localClusterId, out var clusterId) ? clusterId.ToString() : "none";
            var localId = $"local-dev-{localRole.ToLowerInvariant()}-{clusterSuffix}";
            var claims = new List<Claim>
            {
                new("http://schemas.microsoft.com/identity/claims/objectidentifier", localId),
                new("name", $"Local {localRole}"),
                new("preferred_username", $"{localId}@localhost"),
                new("roles", localRole)
            };
            if (!string.IsNullOrWhiteSpace(localClusterId))
                claims.Add(new Claim("local_cluster_id", localClusterId));

            principal = new ClaimsPrincipal(new ClaimsIdentity(
                claims, "LocalDevelopment"));
            ctx.User = principal;
        }

        if (principal.Identity is not null)
            await current.InitializeAsync(principal, ctx.Connection.RemoteIpAddress?.ToString(), ctx.RequestAborted);

        await next(ctx);
    }
}

/// <summary>Turns exceptions into consistent ProblemDetails responses. Stack traces and SQL errors never reach the client.</summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            RequestValidationException => (400, "Validation failed"),
            NotFoundException => (404, "Not found"),
            ForbiddenException => (403, "Forbidden"),
            ConflictException => (409, "Conflict"),
            BusinessRuleException => (422, "Business rule violation"),
            DbUpdateConcurrencyException => (409, "Conflict"),
            OperationCanceledException => (499, "Request cancelled"),
            _ => (500, "Unexpected error")
        };
        if (status >= 500) log.LogError(ex, "Unhandled exception for {Path}", ctx.Request.Path);
        else log.LogInformation("Request rejected ({Status}): {Message}", status, ex.Message);

        var problem = new ProblemDetails
        {
            Status = status, Title = title,
            Detail = status >= 500 ? "An unexpected error occurred. Please quote the trace id when contacting support." : ex.Message,
            Type = $"https://httpstatuses.com/{status}"
        };
        problem.Extensions["traceId"] = ctx.TraceIdentifier;
        if (ex is RequestValidationException v) problem.Extensions["errors"] = v.Errors;
        ctx.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = problem, Exception = ex });
        return true;
    }
}
