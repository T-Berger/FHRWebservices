using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Extensions;
using AspNet.Security.OpenIdConnect.Primitives;
using AspNet.Security.OpenIdConnect.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenIddict.Core;
using AspNetCore.Security.OpenIddict.Models;

namespace AspNetCore.Security.OpenIddict.Controllers
{
    public class AuthorizationController : Controller
    {
        private readonly IOptions<IdentityOptions> _identityOptions;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public AuthorizationController(IOptions<IdentityOptions> identityOptions, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager)
        {
            _identityOptions = identityOptions;
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [HttpPost("~/connect/token")]
        public async Task<IActionResult> Token(OpenIdConnectRequest request)
        {
            if (request.IsPasswordGrantType())
            {
                var result = await HandlePasswordGrantTypeRequest(request);
                return result;
            }
            else if (request.IsRefreshTokenGrantType())
            {
                var result = await HandleRefreshTokenTypeRequest(request);
                return result;
            }

            return BadRequest(new OpenIdConnectResponse
            {
                Error = OpenIdConnectConstants.Errors.UnsupportedGrantType,
                ErrorDescription = "The specified grant type is not supported."
            });
        }

        private async Task<IActionResult> HandlePasswordGrantTypeRequest(OpenIdConnectRequest request)
        {
            var user = await _userManager.FindByNameAsync(request.Username);

            var error = await ValidateUserAndPassword(user, request);
            if (error != null)
                return error;

            var ticket = await CreateTicketAsync(request, user, new AuthenticationProperties());
            // Sign in the user
            return SignIn(ticket.Principal, ticket.Properties, ticket.AuthenticationScheme);
        }

        private async Task<BadRequestObjectResult> ValidateUserAndPassword(ApplicationUser user, OpenIdConnectRequest request)
        {
            if (user == null)
            {
                // Return bad request if the user doesn't exist
                return BadRequest(new OpenIdConnectResponse
                {
                    Error = OpenIdConnectConstants.Errors.InvalidGrant,
                    ErrorDescription = "Invalid username or password"
                });
            }

            // Check that the user can sign in and is not locked out.
            // If two-factor authentication is supported, it would also be appropriate to check that 2FA is enabled for the user
            if (!await _signInManager.CanSignInAsync(user) ||
                (_userManager.SupportsUserLockout && await _userManager.IsLockedOutAsync(user)))
            {
                // Return bad request is the user can't sign in
                return BadRequest(new OpenIdConnectResponse
                {
                    Error = OpenIdConnectConstants.Errors.InvalidGrant,
                    ErrorDescription = "The specified user cannot sign in."
                });
            }

            if (!await _userManager.CheckPasswordAsync(user, request.Password))
            {
                // Return bad request if the password is invalid
                return BadRequest(new OpenIdConnectResponse
                {
                    Error = OpenIdConnectConstants.Errors.InvalidGrant,
                    ErrorDescription = "Invalid username or password"
                });
            }

            // The user is now validated, so reset lockout counts, if necessary
            if (_userManager.SupportsUserLockout)
            {
                await _userManager.ResetAccessFailedCountAsync(user);
            }

            return null;
        }

        private async Task<IActionResult> HandleRefreshTokenTypeRequest(OpenIdConnectRequest request)
        {
            // Das ClaimPrincipal aus dem Refresh-Token extrahieren
            var info = await HttpContext.Authentication.GetAuthenticateInfoAsync(OpenIdConnectServerDefaults.AuthenticationScheme);

            // Soll der Request-Token automatisch invalidiert werden, falls sich
            // das Passwort des Users zwischenzeitlich ge�ndert hat,
            // kann die folgende Methode verwendet werden:
            // var user = _signInManager.ValidateSecurityStampAsync(info.Principal);
            var user = await _userManager.GetUserAsync(info.Principal);
            var error = await ValidateUserForRefreshToken(user);
            if (error != null)
            {
                return error;
            }

            // F�r das neue Ticket werden Eigenschaften wie berechtigte Scopes �bernommen
            var ticket = await CreateTicketAsync(request, user, info.Properties);
            return SignIn(ticket.Principal, ticket.Properties, ticket.AuthenticationScheme);
        }

        private async Task<BadRequestObjectResult> ValidateUserForRefreshToken(ApplicationUser user)
        {
            if (user == null)
            {
                return BadRequest(new OpenIdConnectResponse
                {
                    Error = OpenIdConnectConstants.Errors.InvalidGrant,
                    ErrorDescription = "The refresh token is no longer valid."
                });
            }

            // Ensure the user is still allowed to sign in.
            if (!await _signInManager.CanSignInAsync(user))
            {
                return BadRequest(new OpenIdConnectResponse
                {
                    Error = OpenIdConnectConstants.Errors.InvalidGrant,
                    ErrorDescription = "The user is no longer allowed to sign in."
                });
            }

            return null;
        }

        private async Task<AuthenticationTicket> CreateTicketAsync(OpenIdConnectRequest request, ApplicationUser user, AuthenticationProperties properties = null)
        {
            // Aus dem User ein ClaimsPrincipal erzeugen dem die Claims hinzugef�gt werden
            var principal = await _signInManager.CreateUserPrincipalAsync(user);

            // Create a new authentication ticket holding the user identity.
            var ticket = new AuthenticationTicket(principal, properties, OpenIdConnectServerDefaults.AuthenticationScheme);

            if (!request.IsRefreshTokenGrantType())
            {
                // Hinweis: Der Scope "offline_access" wird ben�tigt das OpenIddict einen
                // Refresh-Token ausstellen darf.
                ticket.SetScopes(new[]
                {
                    OpenIdConnectConstants.Scopes.OpenId,
                    OpenIdConnectConstants.Scopes.Email,
                    OpenIdConnectConstants.Scopes.Profile,
                    OpenIdConnectConstants.Scopes.OfflineAccess,
                    OpenIddictConstants.Scopes.Roles

                }.Intersect(request.GetScopes()));
            }

            // TODO Explain
            ticket.SetResources("resource_server");

            // Claims werden nicht automatisch von OpenIddict an den access oder identity Token geh�ngt.
            // �ber die Destinations kann festgehalten werden an welchen Token die Claims serialisiert werden sollen.
            foreach (var claim in ticket.Principal.Claims)
            {
                // TODO: why
                // Never include the security stamp in the access and identity tokens, as it's a secret value.
                if (claim.Type == _identityOptions.Value.ClaimsIdentity.SecurityStampClaimType)
                {
                    continue;
                }

                // Only add the iterated claim to the id_token if the corresponding scope was granted to the client application.
                // The other claims will only be added to the access_token, which is encrypted when using the default format.
                if ((claim.Type == OpenIdConnectConstants.Claims.Name && ticket.HasScope(OpenIdConnectConstants.Scopes.Profile)) ||
                    (claim.Type == OpenIdConnectConstants.Claims.Email && ticket.HasScope(OpenIdConnectConstants.Scopes.Email)) ||
                    (claim.Type == OpenIdConnectConstants.Claims.Role && ticket.HasScope(OpenIddictConstants.Claims.Roles)))
                {
                    claim.SetDestinations(OpenIdConnectConstants.Destinations.IdentityToken, OpenIdConnectConstants.Destinations.AccessToken);
                }
                else
                {
                    claim.SetDestinations(OpenIdConnectConstants.Destinations.AccessToken);
                }
            }

            return ticket;
        }
    }
}