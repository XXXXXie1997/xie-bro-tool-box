using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace XieBox.Api;

public sealed class VerificationMailSender(IConfiguration config, IHostEnvironment environment, ILogger<VerificationMailSender> logger)
{
    public async Task SendAsync(string email, string code, string purpose, string challengeId, CancellationToken cancellationToken)
    {
        var action = purpose == "register" ? "注册账号" : "找回密码";
        var subject = $"【蟹小箱】{code}";
        var text = $"{action}验证码：{code}\n{RegistrationService.CodeLifetimeMinutes} 分钟内有效。\n\n非本人操作请忽略，请勿泄露验证码。";
        if (purpose == "test")
        {
            subject = $"【蟹小箱】{code}（邮件配置测试）";
            text = $"【蟹小箱】测试验证码：{code}\n\n邮件配置测试成功。此验证码仅用于展示，不能用于注册或找回密码，无需进行任何操作。\n\n蟹小箱\n本邮件由系统自动发送，请勿直接回复。";
        }
        // Explicit test mode never sends email and cannot be used in Production.
        if (config["Mail:Mode"] == "DevelopmentFile")
        {
            if (!environment.IsDevelopment()) throw new InvalidOperationException("Development mail is forbidden in Production.");
            var directory = Path.Combine(config["Platform:ResolvedDataDirectory"]!, "dev-mail");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, challengeId + ".json"),
                JsonSerializer.Serialize(new { email, code, purpose, subject, text }), cancellationToken);
            return;
        }
        var username = config["Mail:Username"];
        var password = config["Mail:Password"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new AuthFlowException(503, "MAIL_UNAVAILABLE", "邮件服务暂未就绪，请稍后重试。");
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("蟹小箱", config["Mail:From"] ?? username));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = text };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var client = new SmtpClient { Timeout = 15000 };
        try
        {
            await client.ConnectAsync(config["Mail:Host"] ?? "smtp.qq.com", config.GetValue("Mail:Port", 465), SecureSocketOptions.SslOnConnect, timeout.Token);
            await client.AuthenticateAsync(username, password, timeout.Token);
            await client.SendAsync(message, timeout.Token);
            try { await client.DisconnectAsync(true, timeout.Token); } catch (Exception) { /* Already accepted by SMTP. */ }
        }
        catch (Exception error)
        {
            logger.LogWarning("Verification email failed ({ErrorType}).", error.GetType().Name);
            throw new AuthFlowException(503, "MAIL_UNAVAILABLE", "验证码暂时发送失败，请稍后重试。");
        }
    }
}
