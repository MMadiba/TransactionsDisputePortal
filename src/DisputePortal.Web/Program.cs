using DisputePortal.Application;
using DisputePortal.Infrastructure;
using DisputePortal.Infrastructure.Persistence;
using DisputePortal.Web.Extensions;
using Microsoft.AspNetCore.Identity;

// Composition root: the only place that knows about every layer.
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(identity => identity.AddDefaultUI())
    .AddPresentation(builder.Configuration);

var app = builder.Build();

await app.Services.InitialiseDatabaseAsync();

app.UsePresentation();
await app.RunAsync();

public partial class Program;
