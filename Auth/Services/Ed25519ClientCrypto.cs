using System.Security.Cryptography;
using System.Text;
using ExpressApp.Auth;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace ExpressApp.Auth.Services;

/// <summary>
/// Криптография клиента для QR-авторизации: пара Ed25519, одноразовые nonce,
/// digest тела и подпись по схеме HTTP Signatures (draft-cavage).
/// </summary>
public interface IClientCrypto
{
    string Udid { get; }
    string PublicKeyBase64 { get; }

    /// <summary>Приватный ключ в формате NaCl (seed||pub, 64 байта, base64).</summary>
    string PrivateKeyBase64 { get; }

    /// <summary>Seed приватного ключа (32 байта, base64) — значение поля registration_key в QR.</summary>
    string PrivateKeySeedBase64 { get; }

    string RegistrationId { get; }
    string RegistrationToken { get; }

    string NewNonce();
    string ComputeDigest(ReadOnlySpan<byte> body);
    string BuildSignatureHeader(string keyId, string requestTarget, long created, string nonce, string digestHeader);
}

public sealed class Ed25519ClientCrypto : IClientCrypto
{
    private const int PublicKeyBytes = 32;
    private const int PrivateKeyBytes = 64;

    private readonly IAuthStateStore _store;
    private readonly Ed25519PrivateKeyParameters _privateKey;
    private readonly Ed25519PublicKeyParameters _publicKey;

    public string Udid { get; }
    public string PublicKeyBase64 => Convert.ToBase64String(_publicKey.GetEncoded());
    public string PrivateKeyBase64 { get; }
    public string PrivateKeySeedBase64 { get; }
    public string RegistrationId { get; }
    public string RegistrationToken { get; }

    public Ed25519ClientCrypto(IAuthStateStore store)
    {
        _store = store;
        var state = store.Load();

        Udid = string.IsNullOrEmpty(state.Udid) ? Guid.NewGuid().ToString("D") : state.Udid;
        RegistrationId = state.RegistrationId ?? Guid.NewGuid().ToString("D");
        RegistrationToken = state.RegistrationToken ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        if (HasStoredKeys(state))
        {
            (_privateKey, _publicKey) = ImportKeys(state.PrivateKeySeedBase64!);
        }
        else
        {
            var generator = new Ed25519KeyPairGenerator();
            generator.Init(new KeyGenerationParameters(new SecureRandom(), 256));
            var keyPair = generator.GenerateKeyPair();
            _privateKey = (Ed25519PrivateKeyParameters)keyPair.Private;
            _publicKey = (Ed25519PublicKeyParameters)keyPair.Public;
        }

        // NaCl-формат приватного ключа: seed (32) || public (32) = 64 байта.
        // Именно этот blob уходит в payload QR-кода как registration_key.
        var seed = _privateKey.GetEncoded();
        PrivateKeySeedBase64 = Convert.ToBase64String(seed);
        PrivateKeyBase64 = Convert.ToBase64String([.. seed, .. _publicKey.GetEncoded()]);

        _store.Save(state with
        {
            Udid = Udid,
            PublicKeyBase64 = PublicKeyBase64,
            PrivateKeySeedBase64 = PrivateKeyBase64,
            RegistrationId = RegistrationId,
            RegistrationToken = RegistrationToken
        });
    }

    private static bool HasStoredKeys(AuthState state)
        => !string.IsNullOrEmpty(state.PrivateKeySeedBase64) && !string.IsNullOrEmpty(state.PublicKeyBase64);

    private static (Ed25519PrivateKeyParameters Private, Ed25519PublicKeyParameters Public) ImportKeys(string privateKeyBase64)
    {
        var raw = Convert.FromBase64String(privateKeyBase64);

        // Поддерживаем и старый формат (только seed, 32 байта), и новый (seed||pub, 64).
        var seed = raw.Length switch
        {
            PrivateKeyBytes => raw[..PublicKeyBytes],
            PublicKeyBytes => raw,
            _ => throw new FormatException($"Некорректный размер приватного ключа: {raw.Length} байт")
        };

        var privateKey = new Ed25519PrivateKeyParameters(seed, 0);
        return (privateKey, privateKey.GeneratePublicKey());
    }

    public string NewNonce()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    public string ComputeDigest(ReadOnlySpan<byte> body)
    {
        var hash = SHA256.HashData(body);
        return "SHA-256=" + Convert.ToBase64String(hash);
    }

    public string BuildSignatureHeader(string keyId, string requestTarget, long created, string nonce, string digestHeader)
    {
        var signingString = string.Join(
            "\n",
            $"(request-target): {requestTarget}",
            $"(created): {created}",
            $"express-request-nonce: {nonce}",
            $"digest: {digestHeader}");

        var signer = new Ed25519Signer();
        signer.Init(true, _privateKey);
        var payload = Encoding.UTF8.GetBytes(signingString);
        signer.BlockUpdate(payload, 0, payload.Length);
        var signature = Convert.ToBase64String(signer.GenerateSignature());

        return $"keyId=\"{keyId}\",algorithm=\"ed25519\"," +
               $"headers=\"(request-target) (created) express-request-nonce digest\"," +
               $"signature=\"{signature}\",created={created}";
    }
}
