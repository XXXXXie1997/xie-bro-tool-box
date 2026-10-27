using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using XieBox.Contracts;

namespace XieBox.Api;

public sealed class SessionService(PlatformDb db)
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    public async Task<SessionResponse> CreateAsync(PlatformUser user, string purpose)
    {
        await PublicUserIds.EnsureAsync(db, user);
        var expiry = DateTime.UtcNow.AddDays(7);
        expiry = expiry.AddTicks(-(expiry.Ticks % TimeSpan.TicksPerSecond));
        var session = new PlatformSession { UserId = user.Id, SecurityStamp = user.SecurityStamp ?? "", Purpose = purpose,
            RefreshExpiresAt = expiry };
        db.Sessions.Add(session);
        return await IssueAsync(session, user);
    }
    private async Task<SessionResponse> IssueAsync(PlatformSession session, PlatformUser user)
    {
        var access = Token();
        var refresh = Token();
        session.AccessHash = Hash(access);
        session.RefreshHash = Hash(refresh);
        session.AccessExpiresAt = new[] { DateTime.UtcNow.AddMinutes(20), session.RefreshExpiresAt }.Min();
        await db.SaveChangesAsync();
        await PublicUserIds.EnsureAsync(db, user);
        return new(access, refresh, new(session.AccessExpiresAt, TimeSpan.Zero), new(user.Id, user.Email, true, user.UserName, user.PublicId), new(session.RefreshExpiresAt, TimeSpan.Zero));
    }
    public async Task<SessionResponse?> RefreshAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512) return null;
        var hash = Hash(token);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var consumed = await db.ConsumedRefreshes.FindAsync(hash);
        if (consumed is not null)
        {
            var previous = await db.Sessions.FindAsync(consumed.SessionId);
            if (previous is not null) previous.Revoked = true;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return null;
        }
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.RefreshHash == hash);
        if (session is null || session.Revoked || session.RefreshExpiresAt <= DateTime.UtcNow) return null;
        var user = await db.Users.FindAsync(session.UserId);
        if (user is null || user.Disabled || user.SecurityStamp != session.SecurityStamp) return null;
        db.ConsumedRefreshes.Add(new() { Hash = hash, SessionId = session.Id, ExpiresAt = session.RefreshExpiresAt });
        var result = await IssueAsync(session, user);
        await transaction.CommitAsync();
        return result;
    }
}

public sealed class PlatformAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, PlatformDb db, UserManager<PlatformUser> users)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var token = header[7..];
        if (token.Length is < 16 or > 512) return AuthenticateResult.Fail("Invalid session");
        var hash = SessionService.Hash(token);
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.AccessHash == hash);
        if (session is null || session.Revoked || session.AccessExpiresAt <= DateTime.UtcNow) return AuthenticateResult.Fail("Expired session");
        var user = await users.FindByIdAsync(session.UserId);
        if (user is null || user.Disabled || user.SecurityStamp != session.SecurityStamp) return AuthenticateResult.Fail("Revoked session");
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id), new(ClaimTypes.Name, user.UserName ?? ""), new(ClaimTypes.Email, user.Email ?? ""), new("session_id", session.Id), new("purpose", session.Purpose) };
        await PublicUserIds.EnsureAsync(db, user);
        claims.Add(new("public_uid", user.PublicId!));
        foreach (var role in await users.GetRolesAsync(user)) claims.Add(new(ClaimTypes.Role, role));
        return AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
    }
}
