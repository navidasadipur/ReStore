using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ReStore.Application.Interfaces;
using ReStore.Domain.Entities;

namespace ReStore.Infrastructure.Services;

public sealed class JwtTokenGenerator(IConfiguration configuration) : ITokenGenerator
{
    public string GenerateToken(User user, IReadOnlyCollection<string> roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.UserName ?? throw new InvalidOperationException("The account has no username."))
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var tokenKey = configuration["JWTSettings:TokenKey"]
            ?? throw new InvalidOperationException("JWTSettings:TokenKey is not configured.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenKey));
        var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha512));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
