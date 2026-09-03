using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualBasic;

namespace BookingSystem.Api.Services;

public class JWTService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _config;

    public JWTService(UserManager<ApplicationUser> userManager, IConfiguration config)
    {
        _userManager = userManager;
        _config = config;
    }
    public async Task<string> TokenCreationAsync(ApplicationUser user)
    {
        var claims = await CreateClaimsAsync(user);

        var credentials = CreateSigningsCredentials();
        var issuer = _config["JWT:Issuer"] ?? throw new InvalidOperationException("JWT issuer info is missing");
        var audience = _config["JWT:Audience"] ?? throw new InvalidOperationException("JWT Audience info is missing");
        var expirationMinutes = _config.GetValue<int?>("JWT:ExpirationMinutes") ?? throw new InvalidOperationException("JWT expiration time is missing");
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,

            claims: claims,

            
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),

            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);

    }

    private async Task<List<Claim>> CreateClaimsAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
            new Claim(ClaimTypes.Email, user.Email ?? string.Empty)
        };
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return claims;
    }
    
    private SigningCredentials CreateSigningsCredentials()
    {
        var jwtKey = _config["JWT:KEY"] ?? throw new InvalidOperationException("JWT key is missing");


        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        return credentials;
    }
}