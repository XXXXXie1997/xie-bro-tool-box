using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using XieBox.Api;
using XieBox.Contracts;

var builder = WebApplication.CreateBuilder(args);
if (Environment.GetEnvironmentVariable("XIEBOX_SECRETS_FILE") is { Length: > 0 } secretsFile)
    builder.Configuration.AddJsonFile(secretsFile, optional: false, reloadOnChange: false);
var bootstrapDevelopmentAdmin = args.Contains("--bootstrap-dev-admin");
if (bootstrapDevelopmentAdmin && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("The development account can only be initialized in Development.");
var dataDirectory = Path.GetFullPath(builder.Configuration["Platform:DataDirectory"] ?? "App_Data", builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataDirectory);
builder.Configuration["Platform:ResolvedDataDirectory"] = dataDirectory;
var databaseConnection = DatabaseConfiguration.GetConnectionString(builder.Configuration);
if (args.Contains("--check-test-database"))
{
    await DatabaseConfiguration.CheckEmptyTestDatabaseAsync(databaseConnection);
    Console.WriteLine("Isolated empty MySQL test database verified.");
    return;
}
builder.Services.AddDbContext<PlatformDb>(o => o.UseMySQL(databaseConnection));
builder.Services.AddDataProtection().PersistKeysToFileSystem(new(Path.Combine(dataDirectory, "keys"))).SetApplicationName("XieBox.Platform");
builder.Services.AddIdentityCore<PlatformUser>(o =>
{
    o.User.RequireUniqueEmail = false;
    o.User.AllowedUserNameCharacters = "";
    o.Password.RequiredLength = 6;
    o.Password.RequireLowercase = false;
    o.Password.RequireNonAlphanumeric = false;
    o.Password.RequireUppercase = false;
    o.Password.RequireDigit = false;
    o.Lockout.MaxFailedAccessAttempts = 5;
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<PlatformDb>().AddDefaultTokenProviders();
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromMinutes(30));
builder.Services.AddAuthentication("PlatformSession").AddScheme<AuthenticationSchemeOptions, PlatformAuthHandler>("PlatformSession", _ => { });
builder.Services.AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireAuthenticatedUser().RequireRole("Admin").RequireClaim("purpose", "admin")));
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<RegistrationService>();
builder.Services.AddScoped<VerificationMailSender>();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownProxies.Add(System.Net.IPAddress.Loopback);
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
    { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024);
var app = builder.Build();
app.UseForwardedHeaders();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PlatformDb>();
    await DatabaseConfiguration.ValidateDatabaseAsync(databaseConnection);
    // Single-instance development startup. Production deploys migrations as a separate reviewed step.
    if (app.Environment.IsDevelopment() || args.Contains("--migrate")) await db.Database.MigrateAsync();
    else if ((await db.Database.GetPendingMigrationsAsync()).Any())
        throw new InvalidOperationException("Apply database migrations with --migrate before starting Production.");
    if (!await db.Modules.AnyAsync())
    {
        var catalog = JsonSerializer.Deserialize<ModuleDefinition[]>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "catalog.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        db.Modules.AddRange(catalog.Select(m => new ModuleRecord { Id = m.Id, DefinitionJson = JsonSerializer.Serialize(m, JsonOptions.Web) }));
        await db.SaveChangesAsync();
    }
    if (args.Contains("--bootstrap-admin") || bootstrapDevelopmentAdmin)
    {
        var username = bootstrapDevelopmentAdmin ? "admin" : Environment.GetEnvironmentVariable("XIEBOX_ADMIN_USERNAME") ?? throw new InvalidOperationException("Set XIEBOX_ADMIN_USERNAME.");
        var password = Environment.GetEnvironmentVariable("XIEBOX_ADMIN_PASSWORD") ?? throw new InvalidOperationException("Set XIEBOX_ADMIN_PASSWORD.");
        if (!ValidCredentials(new(username, password))) throw new InvalidOperationException("Invalid administrator username or password (minimum 6 characters).");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PlatformUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var user = await users.FindByNameAsync(username.Trim());
        if (user is null)
        {
            user = new() { UserName = username.Trim() };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        }
        else if (bootstrapDevelopmentAdmin && !await users.CheckPasswordAsync(user, password))
            throw new InvalidOperationException("An admin username already exists with different credentials; it was not changed.");
        if (!await roles.RoleExistsAsync("Admin")) await roles.CreateAsync(new("Admin"));
        await users.AddToRoleAsync(user, "Admin");
        db.Audit.Add(new() { ActorId = "operator", Action = "bootstrap_admin", TargetId = user.Id });
        await db.SaveChangesAsync();
        Console.WriteLine("Administrator initialized. No credentials were printed.");
        return;
    }
}
if (args.Contains("--migrate")) return;
if (args.Contains("--test-mail"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<VerificationMailSender>().SendAsync(
        builder.Configuration["Mail:From"] ?? throw new InvalidOperationException("Set Mail:From."),
        System.Security.Cryptography.RandomNumberGenerator.GetInt32(1000000).ToString("D6"),
        "test", Guid.NewGuid().ToString("N"), CancellationToken.None);
    Console.WriteLine("Test email accepted by SMTP server.");
    return;
}
app.Use(async (context, next) =>
{
    if (!app.Environment.IsDevelopment() && !context.Request.IsHttps)
    {
        context.Response.StatusCode = 400;
        return;
    }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    try { await next(context); }
    catch (AuthFlowException error)
    {
        context.Response.StatusCode = error.StatusCode;
        await context.Response.WriteAsJsonAsync(new ApiMessage(error.Code, error.Message));
    }
    catch (Exception error)
    {
        app.Logger.LogError(error, "Request failed. Trace: {TraceId}", context.TraceIdentifier);
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new ApiMessage("SERVER_ERROR", "服务暂时不可用，请稍后重试。"));
        }
    }
});
var staticRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (Directory.Exists(staticRoot)) app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(staticRoot) });
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/", () => Results.Redirect("/admin/"));
app.MapGet("/admin/", () => Results.File(Path.Combine(staticRoot, "admin", "index.html"), "text/html"));
app.MapGet("/health", () => Results.Ok(new { status = "ok", version = "0.1.0" }));
var auth = app.MapGroup("/api/v1/auth").RequireRateLimiting("auth");
auth.MapPost("/continue", async (Credentials input, UserManager<PlatformUser> users, SessionService sessions, RegistrationService registration, CancellationToken ct) =>
{
    if (!ValidCredentials(input)) return Error("INVALID_INPUT", "请输入邮箱或已有账号，以及 6–128 位密码。");
    var identity = input.Username.Trim();
    var existing = await users.FindByNameAsync(identity) ?? await users.FindByEmailAsync(identity);
    if (existing is null)
    {
        var challenge = await registration.BeginAsync(new RegistrationRequest(identity, input.Password), ct);
        return Results.Ok(new AuthFlowResponse("verify_email", Challenge: challenge));
    }
    var session = await Authenticate(input, users, sessions, "desktop");
    return session is null ? Results.Unauthorized() : Results.Ok(new AuthFlowResponse("signed_in", Session: session));
});
auth.MapPost("/register", async (RegistrationRequest input, RegistrationService registration, CancellationToken ct) =>
    Results.Ok(await registration.BeginAsync(input, ct)));
auth.MapPost("/verify", async (VerifyEmailRequest input, RegistrationService registration, CancellationToken ct) =>
    Results.Ok(await registration.VerifyAsync(input, ct)));
auth.MapPost("/forgot-password", async (EmailCodeRequest input, RegistrationService registration, CancellationToken ct) =>
    Results.Ok(await registration.ForgotAsync(input, ct)));
auth.MapPost("/reset-password", async (ResetPasswordRequest input, RegistrationService registration, CancellationToken ct) =>
{
    await registration.ResetAsync(input, ct);
    return Results.Ok(new ApiMessage("PASSWORD_RESET", "密码已重置，请使用新密码登录。"));
});
auth.MapPost("/login", (Credentials input, UserManager<PlatformUser> users, SessionService sessions) => Login(input, users, sessions, "desktop"));
auth.MapPost("/admin-login", (Credentials input, UserManager<PlatformUser> users, SessionService sessions) => Login(input, users, sessions, "admin"));
auth.MapPost("/refresh", async (RefreshRequest input, SessionService sessions) =>
{
    var response = await sessions.RefreshAsync(input.RefreshToken);
    return response is null ? Results.Unauthorized() : Results.Ok(response);
});
auth.MapPost("/logout", async (ClaimsPrincipal principal, PlatformDb db) =>
{
    var session = await db.Sessions.FindAsync(principal.FindFirstValue("session_id"));
    if (session is not null) { session.Revoked = true; await db.SaveChangesAsync(); }
    return Results.NoContent();
}).RequireAuthorization();
app.MapGet("/api/v1/me", (ClaimsPrincipal principal) => Results.Ok(new UserContext(principal.FindFirstValue(ClaimTypes.NameIdentifier), principal.FindFirstValue(ClaimTypes.Email), true, principal.FindFirstValue(ClaimTypes.Name), principal.FindFirstValue("public_uid")))).RequireAuthorization();
app.MapGet("/api/v1/modules", async (PlatformDb db) => Results.Ok(await Catalog(db)));
app.MapGet("/api/v1/modules/{moduleId}/capabilities/{capabilityId}", async (string moduleId, string capabilityId, ClaimsPrincipal principal, PlatformDb db) =>
{
    var module = (await Catalog(db)).SingleOrDefault(x => x.Id == moduleId);
    var capability = module?.Capabilities.SingleOrDefault(x => x.Id == capabilityId);
    if (capability is null) return Results.NotFound(new ApiMessage("UNKNOWN_CAPABILITY", "未找到该能力。"));
    var allowed = capability.Policy == "free" || capability.Policy == "login" && principal.Identity?.IsAuthenticated == true;
    return Results.Ok(new CapabilityResult(allowed, allowed ? "ALLOWED" : "ENTITLEMENT_REQUIRED", allowed ? "可以使用" : "需要对应权限"));
});
var admin = app.MapGroup("/api/v1/admin").RequireAuthorization("Admin");
admin.MapGet("/users", async (PlatformDb db) => Results.Ok(await db.Users.OrderByDescending(x => x.CreatedAt).Take(200)
    .Select(x => new { x.Id, Username = x.UserName, x.Disabled, x.CreatedAt }).ToArrayAsync()));
admin.MapPost("/users/{id}/state", async (string id, UserStateRequest input, ClaimsPrincipal principal, PlatformDb db, UserManager<PlatformUser> users) =>
{
    if (id == principal.FindFirstValue(ClaimTypes.NameIdentifier)) return Error("SELF_CHANGE", "不能停用当前管理账号。");
    var user = await users.FindByIdAsync(id);
    if (user is null) return Results.NotFound();
    user.Disabled = input.Disabled;
    await users.UpdateSecurityStampAsync(user);
    await db.Sessions.Where(x => x.UserId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true));
    db.Audit.Add(new() { ActorId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!, Action = input.Disabled ? "disable_user" : "enable_user", TargetId = id });
    await db.SaveChangesAsync();
    return Results.NoContent();
});
admin.MapPost("/users/{id}/revoke-sessions", async (string id, ClaimsPrincipal principal, PlatformDb db) =>
{
    await db.Sessions.Where(x => x.UserId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true));
    db.Audit.Add(new() { ActorId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!, Action = "revoke_sessions", TargetId = id });
    await db.SaveChangesAsync();
    return Results.NoContent();
});
admin.MapGet("/modules", async (PlatformDb db) => Results.Ok(await Catalog(db)));
admin.MapPost("/modules", async (ModuleDefinition input, ClaimsPrincipal principal, PlatformDb db) =>
{
    if (string.IsNullOrWhiteSpace(input.Id) || !Regex.IsMatch(input.Id, "^[a-z][a-z0-9-]{0,63}$") ||
        string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 40 || input.Description is null || input.Description.Length > 1000 ||
        !Version.TryParse(input.Version, out _) || input.ProtocolVersion != 1 || input.Runtime != "content" ||
        input.Status != "available" || input.Sha256 is null || !Regex.IsMatch(input.Sha256, "^[a-fA-F0-9]{64}$") ||
        input.Features is null || input.Features.Length > 20 || input.Capabilities is null || input.Capabilities.Any(x => x.Policy is not ("free" or "login")) ||
        !Uri.TryCreate(input.PackageUrl, UriKind.Absolute, out var url) ||
        !(url.Scheme == "https" || app.Environment.IsDevelopment() && url.Scheme == "http" && url.IsLoopback) || !string.IsNullOrEmpty(url.UserInfo))
        return Error("INVALID_RELEASE", "请检查模块 ID、版本、内容模块类型、下载地址和 SHA-256。首版仅支持声明式内容模块。");
    var record = await db.Modules.FindAsync(input.Id);
    if (record is null) { record = new() { Id = input.Id }; db.Modules.Add(record); }
    record.DefinitionJson = JsonSerializer.Serialize(input, JsonOptions.Web);
    record.UpdatedAt = DateTime.UtcNow;
    db.Audit.Add(new() { ActorId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!, Action = "publish_module", TargetId = input.Id });
    await db.SaveChangesAsync();
    return Results.Ok(new ApiMessage("PUBLISHED", "模块发布信息已保存，客户端刷新目录后可安装。"));
});
admin.MapPost("/modules/{id}/state", async (string id, ModuleStateRequest input, ClaimsPrincipal principal, PlatformDb db) =>
{
    if (input.Status is not ("development" or "planned" or "available" or "disabled")) return Error("INVALID_STATUS", "未知状态。");
    var record = await db.Modules.FindAsync(id);
    if (record is null) return Results.NotFound();
    var module = JsonSerializer.Deserialize<ModuleDefinition>(record.DefinitionJson, JsonOptions.Web)!;
    if (input.Status == "available" && (module.PackageUrl is null || module.Sha256 is null)) return Error("PACKAGE_REQUIRED", "发布前需要可信模块包及校验信息。");
    record.DefinitionJson = JsonSerializer.Serialize(module with { Status = input.Status }, JsonOptions.Web);
    record.UpdatedAt = DateTime.UtcNow;
    db.Audit.Add(new() { ActorId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!, Action = "module_" + input.Status, TargetId = id });
    await db.SaveChangesAsync();
    return Results.NoContent();
});
admin.MapGet("/audit", async (PlatformDb db) => Results.Ok(await db.Audit.OrderByDescending(x => x.Id).Take(100).ToArrayAsync()));
await app.RunAsync();

static IResult Error(string code, string message) => Results.BadRequest(new ApiMessage(code, message));
static bool ValidCredentials(Credentials input) => input.Username is { Length: > 0 and <= 254 }
    && (Regex.IsMatch(input.Username.Trim(), @"\A[\p{L}\p{N}_.-]{1,64}\z")
        || System.Net.Mail.MailAddress.TryCreate(input.Username.Trim(), out var email) && email.Address == input.Username.Trim())
    && input.Password is { Length: >= 6 and <= 128 };
static async Task<IResult> Login(Credentials input, UserManager<PlatformUser> users, SessionService sessions, string purpose)
{
    var session = await Authenticate(input, users, sessions, purpose);
    return session is null ? Results.Unauthorized() : Results.Ok(session);
}
static async Task<SessionResponse?> Authenticate(Credentials input, UserManager<PlatformUser> users, SessionService sessions, string purpose)
{
    if (!ValidCredentials(input)) return null;
    var user = await users.FindByNameAsync(input.Username.Trim()) ?? await users.FindByEmailAsync(input.Username.Trim());
    if (user is null || user.Disabled || await users.IsLockedOutAsync(user)) return null;
    if (user.Email is not null && !user.EmailConfirmed) return null;
    if (!await users.CheckPasswordAsync(user, input.Password)) { await users.AccessFailedAsync(user); return null; }
    if (purpose == "admin" && !await users.IsInRoleAsync(user, "Admin")) return null;
    await users.ResetAccessFailedCountAsync(user);
    return await sessions.CreateAsync(user, purpose);
}
static async Task<ModuleDefinition[]> Catalog(PlatformDb db) => (await db.Modules.AsNoTracking().ToArrayAsync())
    .Select(x => JsonSerializer.Deserialize<ModuleDefinition>(x.DefinitionJson, JsonOptions.Web)!).ToArray();
internal static class JsonOptions { public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web); }
