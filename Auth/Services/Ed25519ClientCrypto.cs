using System.Security.Cryptography;
using System.Text;
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
    string NewNonce();
    string ComputeDigest(ReadOnlySpan<byte> body);
    string BuildSignatureHeader(string keyId, string requestTarget, long created, string nonce, string digestHeader);
}

public sealed class Ed25519ClientCrypto : IClientCrypto
{
    private readonly Ed25519PrivateKeyParameters _privateKey;
    private readonly Ed25519PublicKeyParameters _publicKey;

    public string Udid { get; }
    public string PublicKeyBase64 => Convert.ToBase64String(_publicKey.GetEncoded());

    public Ed25519ClientCrypto()
    {
        Udid = Guid.NewGuid().ToString("D");
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new KeyGenerationParameters(new SecureRandom(), 256));
        var keyPair = generator.GenerateKeyPair();
        _privateKey = (Ed25519PrivateKeyParameters)keyPair.Private;
        _publicKey = (Ed25519PublicKeyParameters)keyPair.Public;
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
