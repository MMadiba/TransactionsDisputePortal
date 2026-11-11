using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace DisputePortal.Api.Extensions;

public static class PresentationPipeline
{
    public static WebApplication UsePresentation(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseExceptionHandler();
        app.UseStatusCodePages(); // problem details for empty 4xx/5xx responses

        // The React app and the API share one origin and everything is self-hosted, so the policy can stay
        // strict: no inline scripts, no third-party hosts. Bootstrap's form icons are data: URIs, hence img-src.
        var contentSecurityPolicy = string.Join("; ",
            "default-src 'self'",
            "script-src 'self'",
            "style-src 'self'",
            "img-src 'self' data:",
            "font-src 'self'",
            "connect-src 'self'",
            "form-action 'self'",
            "frame-ancestors 'none'",
            "base-uri 'self'",
            "object-src 'none'");

        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = contentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            await next();
        });

        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        // The React app (client/) is built into wwwroot.
        app.MapStaticAssets();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

        if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled"))
        {
            app.MapOpenApi();
        }

        app.MapControllers();

        // An unknown API route is a JSON 404, never the React app's HTML.
        app.Map("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found"));

        // Any other path that isn't a file gets the React app's index.html, so deep links and reloads work and
        // the app's own router takes it from there.
        app.MapFallbackToFile("index.html");

        return app;
    }
}
