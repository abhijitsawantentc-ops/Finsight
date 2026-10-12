using Identity.Service.Models;
using Identity.Service.Services;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.Dtos;
using Shared.Contracts.Events;

namespace Identity.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        IPublishEndpoint publishEndpoint,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] UserRegistrationRequest request)
    {
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
        {
            return BadRequest(new { message = "Email already registered." });
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        const string defaultRole = "PortfolioManager";
        var roles = new List<string> { defaultRole };

        // Publish event to MassTransit for downstream services
        await _publishEndpoint.Publish(new UserCreatedEvent(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            defaultRole,
            user.CreatedAtUtc
        ));

        _logger.LogInformation("New user registered successfully: {UserId} ({Email})", user.Id, user.Email);

        var token = _tokenService.GenerateJwtToken(user, roles);

        return Ok(new AuthTokenResponse(
            token,
            RefreshToken: Guid.NewGuid().ToString("N"),
            ExpiresInSeconds: 28800,
            UserId: user.Id,
            Email: user.Email,
            Role: defaultRole
        ));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] UserLoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Count == 0)
        {
            roles.Add("PortfolioManager");
        }

        var token = _tokenService.GenerateJwtToken(user, roles);

        _logger.LogInformation("User logged in: {UserId}", user.Id);

        return Ok(new AuthTokenResponse(
            token,
            RefreshToken: Guid.NewGuid().ToString("N"),
            ExpiresInSeconds: 28800,
            UserId: user.Id,
            Email: user.Email ?? string.Empty,
            Role: roles.FirstOrDefault() ?? "PortfolioManager"
        ));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedGuid))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(parsedGuid.ToString());
        if (user == null)
        {
            return NotFound();
        }

        var roles = await _userManager.GetRolesAsync(user);

        return Ok(new
        {
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            Roles = roles,
            user.CreatedAtUtc
        });
    }
}
