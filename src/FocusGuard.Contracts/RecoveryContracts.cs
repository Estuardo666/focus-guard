using System.Security.Cryptography;

namespace FocusGuard.Contracts;

public sealed record RecoveryCredential(
    int SchemaVersion,
    string Algorithm,
    int Iterations,
    string Salt,
    string Hash);

public sealed record GeneratedRecoveryCode(
    string DisplayCode,
    RecoveryCredential Credential);

public static class RecoveryCodeService
{
    public const int CurrentSchemaVersion = 1;
    public const string Algorithm = "PBKDF2-SHA256";
    public const int Iterations = 210_000;
    private const int EntropyBytes = 20;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static GeneratedRecoveryCode Generate()
    {
        Span<byte> entropy = stackalloc byte[EntropyBytes];
        RandomNumberGenerator.Fill(entropy);
        var normalized = Convert.ToHexString(entropy);
        var display = string.Join(
            '-',
            Enumerable.Range(0, normalized.Length / 5)
                .Select(index => normalized.Substring(index * 5, 5)));
        return new GeneratedRecoveryCode(display, CreateCredential(normalized));
    }

    public static bool Validate(string? suppliedCode, RecoveryCredential? credential)
    {
        if (!TryNormalize(suppliedCode, out var normalized) || !IsValid(credential))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(credential!.Salt);
            expected = Convert.FromBase64String(credential.Hash);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            normalized,
            salt,
            credential.Iterations,
            HashAlgorithmName.SHA256,
            HashBytes);
        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    public static bool IsValid(RecoveryCredential? credential)
    {
        if (credential is null
            || credential.SchemaVersion != CurrentSchemaVersion
            || credential.Algorithm != Algorithm
            || credential.Iterations != Iterations)
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(credential.Salt).Length == SaltBytes
                && Convert.FromBase64String(credential.Hash).Length == HashBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static RecoveryCredential CreateCredential(string normalizedCode)
    {
        Span<byte> salt = stackalloc byte[SaltBytes];
        RandomNumberGenerator.Fill(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            normalizedCode,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashBytes);
        try
        {
            return new RecoveryCredential(
                CurrentSchemaVersion,
                Algorithm,
                Iterations,
                Convert.ToBase64String(salt),
                Convert.ToBase64String(hash));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    private static bool TryNormalize(string? code, out string normalized)
    {
        normalized = string.Concat((code ?? string.Empty).Where(character => character != '-'))
            .Trim()
            .ToUpperInvariant();
        return normalized.Length == EntropyBytes * 2 && normalized.All(Uri.IsHexDigit);
    }
}
