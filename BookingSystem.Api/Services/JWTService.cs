using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualBasic;

namespace BookingSystem.Api.Services;
/// <summary>
/// Generates JWT tokens for authenticated users after a successful login.
/// The token contains infromation about the users identity, role (User or Admin), nad expiration time.
/// Uses UserManager to retrieve user-specific information and roles when generating the token.
/// Retrieves JWT configuration settings, including issuer, audiencem and expiration time, from IConfiguration.
/// </summary>
public class JWTService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _config;

    public JWTService(UserManager<ApplicationUser> userManager, IConfiguration config)
    {
        _userManager = userManager;
        _config = config;
    }
    /// <summary>
    /// Generates a JWT token containing the authenticated users
    /// identity, roles and expiration information.
    /// </summary>
    /// <param name="user">The user for whom the token is generated</param>
    /// <returns>A signed JWT token as a string.</returns>
    /// <exception cref="InvalidOperationException"></exception>
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
    /// <summary>
    /// Creates a collection of claims for the specified user.
    /// </summary>
    /// <param name="user">The user for whom the claims is generated</param>
    /// <returns>A list of claims associated with the user</returns>

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
    /// <summary>
    /// Creates signing credentials using a secret JWT key and the HMAC-SHA256 algorithm.
    /// These credentials are used to sign JWT tokens and protect them from unauthorized modifications.
    /// </summary>
    /// <returns>The signing credentials used to generate JWT signatures</returns>
    private SigningCredentials CreateSigningsCredentials()
    {
        var jwtKey = _config["JWT:KEY"] ?? throw new InvalidOperationException("JWT key is missing");


        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        return credentials;
    }
}