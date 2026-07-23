using System.Globalization;
using System.Security.Cryptography;

namespace Application.Common.Security.Mfa;

/// <summary>
/// Implementación de <see cref="ITotpService"/> conforme a RFC 6238 (TOTP)
/// sobre RFC 4226 (HOTP) con HMAC-SHA1, paso de 30 s y 6 dígitos — el perfil
/// que consumen las apps autenticadoras estándar (Google/Microsoft Authenticator).
/// Sin estado mutable: registrar como Singleton.
/// </summary>
public sealed class TotpService : ITotpService
{
    private const int SecretSizeBytes = 20;   // 160 bits (RFC 4226 §4 recomendado)
    private const int StepSeconds = 30;
    private const int Digits = 6;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <inheritdoc/>
    public string GenerateSecret()
    {
        Span<byte> buffer = stackalloc byte[SecretSizeBytes];
        RandomNumberGenerator.Fill(buffer);
        return Base32Encode(buffer);
    }

    /// <inheritdoc/>
    public bool ValidateCode(string base32Secret, string otp, DateTimeOffset utcNow, int windowSteps = 1)
    {
        if (string.IsNullOrWhiteSpace(base32Secret) || string.IsNullOrWhiteSpace(otp))
            return false;

        otp = otp.Trim();
        if (otp.Length != Digits || !otp.All(char.IsAsciiDigit))
            return false;

        byte[] key;
        try
        {
            key = Base32Decode(base32Secret);
        }
        catch (FormatException)
        {
            return false;
        }

        var currentStep = utcNow.ToUnixTimeSeconds() / StepSeconds;
        var match = false;

        // Se evalúan TODOS los pasos de la ventana (sin salida temprana) y se compara
        // en tiempo constante, para no filtrar información por temporización.
        for (var offset = -windowSteps; offset <= windowSteps; offset++)
        {
            var candidate = ComputeCode(key, currentStep + offset);
            var equal = CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(candidate),
                System.Text.Encoding.ASCII.GetBytes(otp));
            match |= equal;
        }

        CryptographicOperations.ZeroMemory(key);
        return match;
    }

    /// <inheritdoc/>
    public string BuildProvisioningUri(string issuer, string username, string base32Secret)
    {
        var label = Uri.EscapeDataString($"{issuer}:{username}");
        var issuerParam = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={base32Secret}&issuer={issuerParam}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }

    /// <summary>HOTP (RFC 4226 §5.3): HMAC-SHA1 + truncamiento dinámico a 6 dígitos.</summary>
    private static string ComputeCode(byte[] key, long timeStep)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, timeStep);

        Span<byte> hash = stackalloc byte[20];
        using (var hmac = new HMACSHA1(key))
        {
            hmac.TryComputeHash(counter, hash, out _);
        }

        var dynamicOffset = hash[19] & 0x0F;
        var binaryCode = ((hash[dynamicOffset] & 0x7F) << 24)
                       | (hash[dynamicOffset + 1] << 16)
                       | (hash[dynamicOffset + 2] << 8)
                       | hash[dynamicOffset + 3];

        var code = binaryCode % 1_000_000;
        return code.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static string Base32Encode(ReadOnlySpan<byte> data)
    {
        var result = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
        int bitBuffer = 0, bitCount = 0;

        foreach (var b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;
            while (bitCount >= 5)
            {
                bitCount -= 5;
                result.Append(Base32Alphabet[(bitBuffer >> bitCount) & 0x1F]);
            }
        }

        if (bitCount > 0)
            result.Append(Base32Alphabet[(bitBuffer << (5 - bitCount)) & 0x1F]);

        return result.ToString();
    }

    private static byte[] Base32Decode(string encoded)
    {
        encoded = encoded.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(encoded.Length * 5 / 8);
        int bitBuffer = 0, bitCount = 0;

        foreach (var c in encoded)
        {
            var index = Base32Alphabet.IndexOf(c);
            if (index < 0)
                throw new FormatException($"Carácter Base32 inválido: '{c}'.");

            bitBuffer = (bitBuffer << 5) | index;
            bitCount += 5;
            if (bitCount >= 8)
            {
                bitCount -= 8;
                output.Add((byte)((bitBuffer >> bitCount) & 0xFF));
            }
        }

        return output.ToArray();
    }
}
