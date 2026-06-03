using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using MindMatchAI.Constants;
namespace MindMatchAI.Services.Security;
// Handles encryption and searchable hashing for personal data
public class PersonalDataProtector
{
    private readonly IDataProtector _protector;
    private readonly string _hashSecret;

    public PersonalDataProtector(
        IDataProtectionProvider provider,
        IConfiguration configuration)
    {
        string protectionPurpose =
           configuration["Security:PersonalDataProtectionPurpose"]
           ?? throw new InvalidOperationException(
              "Missing Security:PersonalDataProtectionPurpose");

        _protector = provider.CreateProtector(protectionPurpose);

        _hashSecret = configuration["Security:HashSecret"]
            ?? throw new InvalidOperationException("Missing Security:HashSecret");
    }

    public string? Protect(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return plainText;
        }

        return _protector.Protect(plainText);
    }

    public string? Unprotect(string? protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText))
        {
            return protectedText;
        }

        return _protector.Unprotect(protectedText);
    }

    public string? CreateSearchHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_hashSecret));

        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexString(hashBytes);
    }
}
