using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ReleaseTicketing.Core.Abstractions;
using ReleaseTicketing.Core.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.ConfigureFunctionsApplicationInsights();

// Same DbContext and services as the Web API...
builder.Services.AddReleaseTicketingCore(builder.Configuration);
// ...but the "current user" for background work is SYSTEM, so audit rows say who really acted.
builder.Services.AddSingleton<ICurrentUser, SystemUser>();

builder.Build().Run();
