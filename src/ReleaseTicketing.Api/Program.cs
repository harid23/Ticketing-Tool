using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using ReleaseTicketing.Api.Authorization;
using ReleaseTicketing.Api.Controllers;
using ReleaseTicketing.Api.Middleware;
using ReleaseTicketing.Core.Abstractions;
using ReleaseTicketing.Core.Data;
using ReleaseTicketing.Core.Services;

var builder = WebApplication.CreateBuilder(args);

// Production secrets come from Key Vault (managed identity). Locally they come from user-secrets / env vars.
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());

builder.Services.AddApplicationInsightsTelemetry();          // reads APPLICATIONINSIGHTS_CONNECTION_STRING
builder.Services.AddReleaseTicketingCore(builder.Configuration);   // DbContext + all services (shared with Azure Functions)

// "Who is calling?" - filled per request by UserContextMiddleware from the validated Entra token.
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserService>());
builder.Services.AddScoped<LocalDemoScenarioService>();

builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd");   // validates Entra ID access tokens
builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    // Entra app roles are emitted in the access token's "roles" claim.
    options.TokenValidationParameters.RoleClaimType = "roles";
});
builder.Services.AddAuthorization(options =>
{
    // APIs are authenticated by default; health checks opt out explicitly below.
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddControllers().AddJsonOptions(o =>
{
    // Enums travel as text: Wip -> "WIP", YesInterested -> "YES_INTERESTED"
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");
builder.Services.AddOpenApi();

var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy("Frontend", p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("X-Correlation-ID")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // SQLite is only for explicitly selected isolated local testing. SQL Server uses the committed migrations.
    if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        db.Database.EnsureCreated();
    else
        db.Database.Migrate();
    await LegacyRoleMigration.ApplyAsync(db);
}

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseMiddleware<UserContextMiddleware>();
app.UseAuthorization();

if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

public partial class Program;   // lets integration tests start the app
