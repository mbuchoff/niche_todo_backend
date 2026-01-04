// ABOUTME: Supplies RSA signing and validation keys for JWT issuance.
// ABOUTME: Loads PEM data when provided, otherwise generates an ephemeral key pair.

using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TodoBackend.Api.Auth.Options;

namespace TodoBackend.Api.Security;

public interface IJwtKeyProvider
{
    RsaSecurityKey SigningKey { get; }
    RsaSecurityKey ValidationKey { get; }
    string KeyId { get; }
}

public sealed class JwtKeyProvider : IJwtKeyProvider, IDisposable
{
    private readonly JwtOptions _options;
    private readonly ILogger<JwtKeyProvider> _logger;
    private readonly Lazy<KeyPair> _keys;

    public JwtKeyProvider(IOptions<JwtOptions> options, ILogger<JwtKeyProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _keys = new Lazy<KeyPair>(BuildKeys, isThreadSafe: true);
    }

    public RsaSecurityKey SigningKey => _keys.Value.SigningKey;
    public RsaSecurityKey ValidationKey => _keys.Value.ValidationKey;
    public string KeyId => _keys.Value.KeyId;

    public void Dispose()
    {
        if (_keys.IsValueCreated)
        {
            _keys.Value.SigningKey.Rsa?.Dispose();
            if (!ReferenceEquals(_keys.Value.SigningKey, _keys.Value.ValidationKey))
            {
                _keys.Value.ValidationKey.Rsa?.Dispose();
            }
        }
    }

    private KeyPair BuildKeys()
    {
        if (!string.IsNullOrWhiteSpace(_options.PrivateKeyPem))
        {
            var signing = CreateKeyFromPem(_options.PrivateKeyPem!);
            var validation = !string.IsNullOrWhiteSpace(_options.PublicKeyPem)
                ? CreateKeyFromPem(_options.PublicKeyPem!)
                : signing;
            var keyId = Guid.NewGuid().ToString("N");
            signing.KeyId = keyId;
            validation.KeyId = keyId;
            _logger.LogInformation("Loaded RSA signing key from configuration");
            return new KeyPair(signing, validation, keyId);
        }

        _logger.LogWarning("No JWT private key configured. Generating ephemeral development key.");
        var rsa = RSA.Create(2048);
        var generated = new RsaSecurityKey(rsa)
        {
            KeyId = Guid.NewGuid().ToString("N")
        };
        return new KeyPair(generated, generated, generated.KeyId);
    }

    private static RsaSecurityKey CreateKeyFromPem(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new RsaSecurityKey(rsa);
    }

    private sealed record KeyPair(RsaSecurityKey SigningKey, RsaSecurityKey ValidationKey, string KeyId);
}
