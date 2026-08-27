using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace StoicTrade.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IConfiguration config, ILogger<AuthController> logger)
        {
            _config = config;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpPost("google-login")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
        {
            try
            {
                var clientId = _config["GoogleClientId"] ?? throw new InvalidOperationException("GoogleClientId is not configured.");
                var settings = new GoogleJsonWebSignature.ValidationSettings()
                {
                    Audience = new List<string>() { clientId }
                };

                // Validate the token cryptographically
                var payload = await GoogleJsonWebSignature.ValidateAsync(request.Token, settings);

                // Verify against allowed admin emails
                var allowedEmailsConfig = _config["AllowedAdminEmails"] ?? "";
                var allowedEmails = allowedEmailsConfig.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                       .Select(e => e.Trim().ToLower())
                                                       .ToList();

                if (allowedEmails.Any() && !allowedEmails.Contains(payload.Email.ToLower()))
                {
                    _logger.LogWarning($"Unauthorized Google Login attempt from: {payload.Email}");
                    return Unauthorized(new { Message = "Unauthorized user account." });
                }

                // Generate system JWT token
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(_config["JWT_SECRET"] ?? "super_secret_jwt_key_that_must_be_long_enough_for_hmac_sha256");
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(new[] 
                    { 
                        new Claim(ClaimTypes.Name, payload.Name),
                        new Claim(ClaimTypes.Email, payload.Email)
                    }),
                    Expires = DateTime.UtcNow.AddDays(1),
                    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
                };
                var token = tokenHandler.CreateToken(tokenDescriptor);
                var tokenString = tokenHandler.WriteToken(token);

                return Ok(new { Token = tokenString });
            }
            catch (InvalidJwtException ex)
            {
                _logger.LogError(ex, "Invalid Google JWT token.");
                return Unauthorized(new { Message = "Invalid Google token." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Google login.");
                return StatusCode(500, new { Message = "Internal server error." });
            }
        }

        [AllowAnonymous]
        [HttpPost("pin-login")]
        public IActionResult PinLogin([FromBody] PinLoginRequest request)
        {
            try
            {
                var expectedHash = _config["MASTER_PIN_HASH"] ?? "73575068bb4b3b7f4ccc6f6eada01a7e0bf61afea3d0ce77d64cb7d7284e11a8";
                if (string.IsNullOrEmpty(request.PinHash) || request.PinHash.ToLowerInvariant() != expectedHash.ToLowerInvariant())
                {
                    _logger.LogWarning("Unauthorized PIN Login attempt.");
                    return Unauthorized(new { Message = "Invalid Master PIN." });
                }

                // Generate system JWT token with 30-day lifespan for mobile sessions
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(_config["JWT_SECRET"] ?? "super_secret_jwt_key_that_must_be_long_enough_for_hmac_sha256");
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(new[] 
                    { 
                        new Claim(ClaimTypes.Name, "StoicTrader Admin"),
                        new Claim(ClaimTypes.Role, "Admin")
                    }),
                    Expires = DateTime.UtcNow.AddDays(30),
                    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
                };
                var token = tokenHandler.CreateToken(tokenDescriptor);
                var tokenString = tokenHandler.WriteToken(token);

                return Ok(new { Token = tokenString });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during PIN login.");
                return StatusCode(500, new { Message = "Internal server error." });
            }
        }
    }

    public class GoogleLoginRequest
    {
        public string Token { get; set; } = string.Empty;
    }

    public class PinLoginRequest
    {
        public string PinHash { get; set; } = string.Empty;
    }
}
