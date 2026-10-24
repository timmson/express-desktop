using System.Security.Cryptography;
using ExpressApp.Auth;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace ExpressApp.Auth.Services;

/// <summary>
/// Криптография клиента для QR-авторизации.
/// Реализуется в проекте как Ed25519ClientCrypto (зависит от MAUI Preferences),
/// здесь — рабочая реализация на BouncyCastle.
/// </summary>
public interface IClientCrypto
{
    string Udid { get; }
    string PublicKeyBase64 { get; }
    string PrivateKeyBase64 { get; }
    string PrivateKeySeedBase64 { get; }
    string RegistrationId { get; }
    string RegistrationToken { get; }
    string NewNonce();
    string ComputeDigest(ReadOnlySpan<byte> body);
    string BuildSignatureHeader(string keyId, string requestTarget, long created, string nonce, string digestHeader);
}

/// <summary>
/// Реальная Ed25519-криптография на BouncyCastle.
/// Тесты гонят настоящий крипто-код, а не заглушку.
/// </summary>
public sealed class TestClientCrypto : IClientCrypto
{
    public string Udid { get; }
    public string PublicKeyBase64 { get; }
    public string PrivateKeyBase64 { get; }
    public string PrivateKeySeedBase64 { get; }
    public string RegistrationId { get; }
    public string RegistrationToken { get; }

    public TestClientCrypto(AuthState? state = null)
    {
        state ??= new AuthState(
            Udid: string.Empty, PublicKeyBase64: string.Empty, PrivateKeySeedBase64: null,
            RegistrationId: null, RegistrationToken: null, AccessToken: null, TokenExpiresAt: null);

        Udid = string.IsNullOrEmpty(state.Udid) ? Guid.NewGuid().ToString("D") : state.Udid;
        RegistrationId = state.RegistrationId ?? Guid.NewGuid().ToString("D");
        RegistrationToken = state.RegistrationToken ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new KeyGenerationParameters(new SecureRandom(), 256));
        var keyPair = generator.GenerateKeyPair();
        var privateKey = (Ed25519PrivateKeyParameters)keyPair.Private;
        var publicKey = (Ed25519PublicKeyParameters)keyPair.Public;

        var seed = privateKey.GetEncoded();
        PrivateKeySeedBase64 = Convert.ToBase64String(seed);
        PrivateKeyBase64 = Convert.ToBase64String([.. seed, .. publicKey.GetEncoded()]);
        PublicKeyBase64 = Convert.ToBase64String(publicKey.GetEncoded());
    }

    public string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public string ComputeDigest(ReadOnlySpan<byte> body)
        => "SHA-256=" + Convert.ToBase64String(SHA256.HashData(body));

    public string BuildSignatureHeader(string keyId, string requestTarget, long created, string nonce, string digestHeader)
        => $"keyId=\"{keyId}\",algorithm=\"ed25519\",signature=\"sig\",created={created}";
}
