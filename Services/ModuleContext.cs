using System.Net.Http;
using XieBox.Contracts;

namespace XieBox.Services;
// Created by the host for one known module; modules cannot supply another module ID.
public sealed class ModuleContext(ModuleDefinition definition, AccountService accounts, Func<Task> signIn) : IPlatformContext, IDisposable
{
    private EventHandler<UserContext>? _listeners;
    public UserContext CurrentUser => accounts.Current;
    public event EventHandler<UserContext>? UserContextChanged
    {
        add { accounts.Changed += value; _listeners += value; }
        remove { accounts.Changed -= value; _listeners -= value; }
    }
    public Task RequestSignInAsync() => DesktopFeatures.AccountsEnabled ? signIn() : Task.CompletedTask;
    public Task<CapabilityResult> CheckCapabilityAsync(string capabilityId, CancellationToken cancellationToken = default)
    {
        var capability = definition.Capabilities.SingleOrDefault(c => c.Id == capabilityId);
        if (capability is null) return Task.FromResult(new CapabilityResult(false, "UNKNOWN_CAPABILITY", "该模块未声明此能力。"));
        if (capability.Policy == "free") return Task.FromResult(new CapabilityResult(true, "ALLOWED", "免费能力可离线使用。"));
        if (!DesktopFeatures.AccountsEnabled) return Task.FromResult(new CapabilityResult(false, "NOT_AVAILABLE", "此功能暂未开放。"));
        if (!CurrentUser.IsAuthenticated) return Task.FromResult(new CapabilityResult(false, "LOGIN_REQUIRED", "请通过平台登录。"));
        return accounts.SendAuthorizedAsync<CapabilityResult>($"modules/{Uri.EscapeDataString(definition.Id)}/capabilities/{Uri.EscapeDataString(capabilityId)}", HttpMethod.Get, cancellationToken: cancellationToken);
    }
    public void Dispose() { accounts.Changed -= _listeners; _listeners = null; }
}
