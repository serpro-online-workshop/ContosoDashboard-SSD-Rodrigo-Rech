using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Add authentication state provider for Blazor
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthenticationStateProvider>();

// Configure Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure Mock Authentication (Cookie-based for training purposes)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// Add authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Employee", policy => policy.RequireRole("Employee", "TeamLead", "ProjectManager", "Administrator"));
    options.AddPolicy("TeamLead", policy => policy.RequireRole("TeamLead", "ProjectManager", "Administrator"));
    options.AddPolicy("ProjectManager", policy => policy.RequireRole("ProjectManager", "Administrator"));
    options.AddPolicy("Administrator", policy => policy.RequireRole("Administrator"));
});

// Register application services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddScoped<IFileSafetyScanner, DeterministicLocalFileSafetyScanner>();

// Add HttpContextAccessor for accessing user claims
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.EnsureCreated(); // For development - use migrations in production
        DocumentSchemaInitializer.EnsureCreated(context);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred creating the database.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    // Use HSTS even in development for training purposes
    app.UseHsts();
}

// Add security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    
    // Content Security Policy for Blazor Server
    context.Response.Headers["Content-Security-Policy"] = 
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
        "font-src 'self' https://cdn.jsdelivr.net; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self' wss: ws:;";
    
    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Enable authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/documents/{documentId:int}/download", async (
    int documentId,
    HttpContext httpContext,
    IDocumentService documentService,
    CancellationToken cancellationToken) =>
{
    if (!TryGetAuthenticatedUserId(httpContext.User, out var userId))
    {
        return Results.NotFound();
    }

    var access = await documentService.OpenFileAsync(userId, documentId, preview: false, cancellationToken);
    if (access == null)
    {
        return Results.NotFound();
    }

    httpContext.Response.Headers.CacheControl = "no-store, no-cache";
    httpContext.Response.Headers.Pragma = "no-cache";
    httpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{access.FileName.Replace("\"", "") }\"";
    return Results.File(access.Content, access.ContentType);
}).RequireAuthorization();

app.MapGet("/documents/{documentId:int}/preview", async (
    int documentId,
    HttpContext httpContext,
    IDocumentService documentService,
    CancellationToken cancellationToken) =>
{
    if (!TryGetAuthenticatedUserId(httpContext.User, out var userId))
    {
        return Results.NotFound();
    }

    var summary = await documentService.GetVisibleAsync(userId, documentId, cancellationToken);
    if (summary == null)
    {
        return Results.NotFound();
    }

    if (!DocumentFileTypes.IsPreviewable(summary.FileType))
    {
        return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
    }

    var access = await documentService.OpenFileAsync(userId, documentId, preview: true, cancellationToken);
    if (access == null)
    {
        return Results.NotFound();
    }

    httpContext.Response.Headers.CacheControl = "no-store, no-cache";
    httpContext.Response.Headers.Pragma = "no-cache";
    httpContext.Response.Headers.ContentDisposition = $"inline; filename=\"{access.FileName.Replace("\"", "") }\"";
    return Results.File(access.Content, access.ContentType);
}).RequireAuthorization();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

static bool TryGetAuthenticatedUserId(ClaimsPrincipal principal, out int userId)
{
    userId = 0;
    return principal.Identity?.IsAuthenticated == true
        && int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId)
        && userId > 0;
}

app.Run();
