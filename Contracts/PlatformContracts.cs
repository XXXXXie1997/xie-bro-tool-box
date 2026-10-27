namespace XieBox.Contracts;

public sealed record UserContext(string? UserId, string? Email, bool IsAuthenticated, string? Username = null, string? PublicId = null)
{
    public static UserContext Guest { get; } = new(null, null, false);
}
public sealed record CapabilityResult(bool Allowed, string Code, string Message);
public sealed record CapabilityDefinition(string Id, string Policy);
public sealed record ModuleDefinition(
    string Id, string Name, string Icon, string Description, string Version,
    string Status, string Runtime, bool Offline, string[] Features,
    CapabilityDefinition[] Capabilities, string? PackageUrl = null,
    string? Sha256 = null, string? Entry = null, int ProtocolVersion = 1);
public sealed record ModuleManifest(string Id, string Name, string Version, string Runtime, string Entry, int ProtocolVersion = 1);
public sealed record ModuleContent(string Title, string Description, string[] Tips);
public sealed record Credentials(string Username, string Password);
public sealed record RegistrationRequest(string Email, string Password);
public sealed record EmailCodeRequest(string Email);
public sealed record VerifyEmailRequest(string ChallengeId, string Code);
public sealed record ResetPasswordRequest(string ChallengeId, string Code, string NewPassword);
public sealed record EmailChallengeResponse(string ChallengeId, string Message, int RetryAfterSeconds = 60);
public sealed record RefreshRequest(string RefreshToken);
public sealed record SessionResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, UserContext User, DateTimeOffset? SessionExpiresAt = null);
public sealed record AuthFlowResponse(string Step, SessionResponse? Session = null, string? Message = null, EmailChallengeResponse? Challenge = null);
public sealed record ApiMessage(string Code, string Message);
public sealed record ModuleStateRequest(string Status);
public sealed record UserStateRequest(bool Disabled);

// Modules receive this context, never the platform's password, tokens or HttpClient.
// In-process implementations are trusted first-party code, not a security sandbox.
public interface IPlatformContext
{
    UserContext CurrentUser { get; }
    event EventHandler<UserContext>? UserContextChanged;
    Task RequestSignInAsync();
    Task<CapabilityResult> CheckCapabilityAsync(string capabilityId, CancellationToken cancellationToken = default);
}
