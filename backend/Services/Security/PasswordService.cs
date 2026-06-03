using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace MindMatchAI.Services.Security;
// Hashes passwords and verifies password hashes during login.
public class PasswordService
{
    private readonly PasswordHashingSettings _settings;

    public PasswordService(IConfiguration configuration)
    {
        _settings = configuration
            .GetSection("Security:PasswordHashing")
            .Get<PasswordHashingSettings>() ?? new PasswordHashingSettings();
    }

    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password is required.");
        }

        byte[] salt = RandomNumberGenerator.GetBytes(_settings.SaltSize);

        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: salt,
            iterations: _settings.Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: _settings.KeySize
        );

        return $"{_settings.Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        string[] parts = storedHash.Split('.');

        if (parts.Length != _settings.StoredHashPartsCount)
        {
            return false;
        }

        if (!int.TryParse(parts[_settings.IterationsPartIndex], out int iterations))
        {
            return false;
        }

        byte[] salt = Convert.FromBase64String(parts[_settings.SaltPartIndex]);
        byte[] expectedHash = Convert.FromBase64String(parts[_settings.HashPartIndex]);

        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: salt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: expectedHash.Length
        );

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private class PasswordHashingSettings
    {
        public int SaltSize { get; set; } = 16;
        public int KeySize { get; set; } = 32;
        public int Iterations { get; set; } = 100000;

        public int StoredHashPartsCount { get; set; } = 3;
        public int IterationsPartIndex { get; set; } = 0;
        public int SaltPartIndex { get; set; } = 1;
        public int HashPartIndex { get; set; } = 2;
    }
}
