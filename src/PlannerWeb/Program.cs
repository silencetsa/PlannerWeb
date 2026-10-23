using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Planer.Data;
using PlannerWeb.Services;

var builder = WebApplication.CreateBuilder(args);
var dataPath = Path.GetFullPath(builder.Configuration["Planner:DataDirectory"] ?? "data");
Directory.CreateDirectory(dataPath);
builder.Services.AddSingleton(new WebDatabase(Path.Combine(dataPath, "planner-web.db")));
builder.Services.AddSingleton<PlannerService>();
builder.Services.AddSingleton<AccountService>();
builder.Services.AddSingleton<IPasswordHasher<WebUser>, PasswordHasher<WebUser>>();
builder.Services.AddDataProtection().SetApplicationName("PlannerWeb")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")));
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.LoginPath = "/Account/Login"; o.AccessDeniedPath = "/Account/Login";
    o.Cookie.Name = "PlannerWeb.Auth"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromDays(14); o.SlidingExpiration = true;
    o.Events.OnValidatePrincipal = context =>
    {
        var id = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (id is null || context.HttpContext.RequestServices.GetRequiredService<WebDatabase>().UserById(id) is null)
            context.RejectPrincipal();
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("accounts", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
// Amvera exposes this deployment only through its HTTPS ingress; internal traffic is HTTP.
// This explicit deployment contract avoids trusting client-supplied forwarded headers.
if (!app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Planner:ExternalHttps"))
    app.Use((context, next) => { context.Request.Scheme = "https"; return next(context); });
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error"); app.UseHsts(); }
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; font-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (!context.Request.Path.StartsWithSegments("/css") && !context.Request.Path.StartsWithSegments("/js") && !context.Request.Path.StartsWithSegments("/images") && !context.Request.Path.StartsWithSegments("/fonts"))
        context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseStaticFiles(); app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/health", (WebDatabase database) => database.Healthy() ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503));
app.MapControllerRoute("default", "{controller=Planner}/{action=Index}/{id?}");
app.Run();
public partial class Program { }

