using DisputePortal.Application;
using DisputePortal.Infrastructure;
using DisputePortal.Infrastructure.Persistence;
using DisputePortal.Api.Extensions;

// Composition root: the only place that knows about every layer.
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure()
    .AddPresentation(builder.Configuration);

var app = builder.Build();

await app.Services.InitialiseDatabaseAsync();

app.UsePresentation();
await app.RunAsync();

public partial class Program;
