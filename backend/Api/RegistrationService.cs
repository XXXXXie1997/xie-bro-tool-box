using System.Data.Common;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using XieBox.Contracts;

namespace XieBox.Api;

public sealed class AuthFlowException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed class RegistrationService(PlatformDb db, UserManager<PlatformUser> users, SessionService sessions,
    IDataProtectionProvider protection, VerificationMailSender mail, IConfiguration config)
{
    public const int CodeLifetimeMinutes = 5;
    private readonly IDataProtector _protector = protection.CreateProtector("XieBox.EmailCode.v1");
    private static AuthFlowException InvalidCode() => new(400, "INVALID_CODE", "验证码无效或已过期，请重新获取。");
    public static string NormalizeEmail(string? value)
    {
        var email = value?.Trim().ToLowerInvariant();
        if (email is not { Length: > 3 and <= 254 } || !MailAddress.TryCreate(email, out var address)
            || address.Address != email || !email.Contains('.') || email.Any(char.IsWhiteSpace))
            throw new AuthFlowException(400, "INVALID_EMAIL", "请输入有效邮箱地址。");
        return email;
    }
    public async Task<EmailChallengeResponse> BeginAsync(RegistrationRequest input, CancellationToken ct)
    {
        var email = NormalizeEmail(input.Email);
        if (input.Password is not { Length: >= 6 and <= 128 })
            throw new AuthFlowException(400, "INVALID_PASSWORD", "密码需为 6–128 位。");
        await using var gate = await AcquireLockAsync(ct);
        if (await users.FindByNameAsync(email) is not null || await users.FindByEmailAsync(email) is not null)
            throw new AuthFlowException(400, "REGISTRATION_FAILED", "该邮箱已注册，请登录或找回密码。");
        var user = new PlatformUser { UserName = email, Email = email };
        foreach (var validator in users.PasswordValidators)
            if (!(await validator.ValidateAsync(users, user, input.Password)).Succeeded)
                throw new AuthFlowException(400, "INVALID_PASSWORD", "密码不符合要求。");
        return await IssueAsync(new EmailChallenge { Purpose = "register", Email = email,
            PasswordHash = users.PasswordHasher.HashPassword(user, input.Password) }, true, ct);
    }
    public async Task<EmailChallengeResponse> ForgotAsync(EmailCodeRequest input, CancellationToken ct)
    {
        var email = NormalizeEmail(input.Email);
        await using var gate = await AcquireLockAsync(ct);
        var user = await users.FindByEmailAsync(email);
        var eligible = user is { Disabled: false, EmailConfirmed: true };
        return await IssueAsync(new EmailChallenge { Purpose = "reset", Email = email, UserId = eligible ? user!.Id : null }, eligible, ct);
    }
    private async Task<EmailChallengeResponse> IssueAsync(EmailChallenge challenge, bool send, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // Retain recent attempts so quotas survive restarts. Failed deliveries also count.
        await db.EmailChallenges.Where(x => x.CreatedAt < now.AddDays(-2)).ExecuteDeleteAsync(ct);
        var recent = db.EmailChallenges.Where(x => x.Email == challenge.Email);
        if (await recent.AnyAsync(x => x.CreatedAt > now.AddSeconds(-60), ct)
            || await recent.CountAsync(x => x.CreatedAt > now.AddDays(-1), ct) >= 5
            || await db.EmailChallenges.CountAsync(x => x.CreatedAt > now.AddDays(-1), ct) >= Math.Clamp(config.GetValue("Mail:DailyLimit", 100), 1, 1000))
            throw new AuthFlowException(429, "MAIL_RATE_LIMIT", "验证码发送过于频繁，请稍后再试。");
        await recent.Where(x => x.Purpose == challenge.Purpose && !x.Consumed).ExecuteUpdateAsync(s => s.SetProperty(x => x.Consumed, true), ct);
        var code = RandomNumberGenerator.GetInt32(1000000).ToString("D6");
        challenge.CreatedAt = now;
        challenge.ExpiresAt = now.AddMinutes(CodeLifetimeMinutes);
        challenge.ProtectedCode = _protector.Protect(challenge.Id + ":" + challenge.Purpose + ":" + code);
        db.EmailChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        if (send)
        {
            try { await mail.SendAsync(challenge.Email, code, challenge.Purpose, challenge.Id, ct); }
            catch { challenge.Consumed = true; await db.SaveChangesAsync(CancellationToken.None); throw; }
        }
        return new(challenge.Id, challenge.Purpose == "register" ? "验证码已发送，请查收邮箱。" : "如果该邮箱已注册，你会收到重置密码验证码。");
    }
    private async Task<EmailChallenge> CheckAsync(string? id, string? code, string purpose, CancellationToken ct)
    {
        if (id is not { Length: 32 }) throw InvalidCode();
        var challenge = await db.EmailChallenges.FindAsync([id], ct);
        if (challenge is null || challenge.Purpose != purpose || challenge.Consumed
            || challenge.ExpiresAt <= DateTime.UtcNow || challenge.CreatedAt.AddMinutes(CodeLifetimeMinutes) <= DateTime.UtcNow)
            throw InvalidCode();
        var valid = false;
        if (code is { Length: 6 } && code.All(char.IsAsciiDigit))
        {
            try
            {
                var expected = Encoding.UTF8.GetBytes(_protector.Unprotect(challenge.ProtectedCode));
                valid = CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(challenge.Id + ":" + purpose + ":" + code));
            }
            catch (CryptographicException) { }
        }
        if (!valid) throw new AuthFlowException(400, "INVALID_CODE", "验证码不正确，请检查后重试。");
        return challenge;
    }
    public async Task<AuthFlowResponse> VerifyAsync(VerifyEmailRequest input, CancellationToken ct)
    {
        await using var gate = await AcquireLockAsync(ct);
        var challenge = await CheckAsync(input.ChallengeId, input.Code, "register", ct);
        if (await users.FindByNameAsync(challenge.Email) is not null || await users.FindByEmailAsync(challenge.Email) is not null)
            throw new AuthFlowException(400, "REGISTRATION_FAILED", "该邮箱已注册，请登录。");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = new PlatformUser { UserName = challenge.Email, Email = challenge.Email, EmailConfirmed = true, PasswordHash = challenge.PasswordHash };
        var result = await users.CreateAsync(user);
        if (!result.Succeeded) throw new AuthFlowException(400, "REGISTRATION_FAILED", "账号无法创建，请重新注册。");
        challenge.Consumed = true;
        challenge.PasswordHash = null;
        var session = await sessions.CreateAsync(user, "desktop");
        await transaction.CommitAsync(ct);
        return new("signed_in", session, "邮箱验证成功，账号已创建。");
    }
    public async Task ResetAsync(ResetPasswordRequest input, CancellationToken ct)
    {
        if (input.NewPassword is not { Length: >= 6 and <= 128 })
            throw new AuthFlowException(400, "INVALID_PASSWORD", "新密码需为 6–128 位。");
        await using var gate = await AcquireLockAsync(ct);
        var challenge = await CheckAsync(input.ChallengeId, input.Code, "reset", ct);
        var user = challenge.UserId is null ? null : await users.FindByIdAsync(challenge.UserId);
        if (user is null || user.Disabled || !user.EmailConfirmed || NormalizeEmail(user.Email) != challenge.Email) throw InvalidCode();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, input.NewPassword);
        if (!result.Succeeded) throw new AuthFlowException(400, "INVALID_PASSWORD", "新密码不符合要求，请重试。");
        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await db.Sessions.Where(x => x.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true), ct);
        await db.EmailChallenges.Where(x => x.Email == challenge.Email && x.Purpose == "reset").ExecuteUpdateAsync(s => s.SetProperty(x => x.Consumed, true), ct);
        await transaction.CommitAsync(ct);
    }
    // A database-scoped lock protects quotas and one-time code use across API processes.
    private async Task<IAsyncDisposable> AcquireLockAsync(CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        var connection = db.Database.GetDbConnection();
        var name = "xiebox-email:" + SessionService.Hash(connection.Database)[..32];
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT GET_LOCK(@name, 3)";
            var parameter = command.CreateParameter(); parameter.ParameterName = "@name"; parameter.Value = name; command.Parameters.Add(parameter);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 1)
                throw new AuthFlowException(429, "AUTH_BUSY", "账号服务繁忙，请稍后重试。");
            return new DatabaseLock(db, connection, name);
        }
        catch { await db.Database.CloseConnectionAsync(); throw; }
    }
    private sealed class DatabaseLock(PlatformDb db, DbConnection connection, string name) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT RELEASE_LOCK(@name)";
                var parameter = command.CreateParameter(); parameter.ParameterName = "@name"; parameter.Value = name; command.Parameters.Add(parameter);
                await command.ExecuteScalarAsync();
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }
}
