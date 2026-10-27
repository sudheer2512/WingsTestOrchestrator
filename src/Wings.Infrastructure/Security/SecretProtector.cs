using Microsoft.AspNetCore.DataProtection;
using Wings.Application.Abstractions;

namespace Wings.Infrastructure.Security;

/// <summary>
/// Encrypts environment secrets with ASP.NET Core Data Protection. Keys live under Storage:RootPath/keys; on Windows servers
/// protect them further with DPAPI or a certificate (see the README), and back the folder up with the database.
/// </summary>
public sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Wings.TestOrchestrator.EnvironmentSecrets.v1");
    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
