using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace DisputePortal.Web.Extensions;

public static class PresentationPipeline
{
    public static WebApplication UsePresentation(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseMigrationsEndPoint();
        }
        else
        {
            app.UseHsts();
        }

        app.UseExceptionHandler("/Home/Error");
        app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"),
            api => api.UseStatusCodePages()); // problem details for empty 4xx/5xx API responses
        app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"),
            site => site.UseStatusCodePagesWithReExecute("/Home/Status/{0}"));

        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
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

        app.MapStaticAssets();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

        if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled"))
        {
            app.MapOpenApi();
        }

        app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
        app.MapRazorPages().WithStaticAssets();

        return app;
    }
}
