using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MindMatchAI.Models;
using MindMatchAI.Constants;
namespace MindMatchAI.Services.Security;
// Creates a JWT token for an authenticated user.
public class JwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string CreateToken(AppUser user)
    {
        string secret = _configuration["Security:JwtSecret"]
            ?? throw new InvalidOperationException("Missing Security:JwtSecret");

        string issuer = _configuration["Security:JwtIssuer"]
           ?? throw new InvalidOperationException("Missing Security:JwtIssuer");

        string audience = _configuration["Security:JwtAudience"]
            ?? throw new InvalidOperationException("Missing Security:JwtAudience");

        int expirationHours = _configuration
            .GetValue<int?>("Security:JwtExpirationHours")
               ?? throw new InvalidOperationException("Missing Security:JwtExpirationHours");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtClaimNames.UserId, user.Id.ToString()),
            new Claim(JwtClaimNames.Role, user.Role)
        };

        if (user.CompanyId.HasValue)
        {
            claims.Add(new Claim(JwtClaimNames.CompanyId, user.CompanyId.Value.ToString()));
        }

        if (user.CandidateId.HasValue)
        {
            claims.Add(new Claim(JwtClaimNames.CandidateId, user.CandidateId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(expirationHours),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
