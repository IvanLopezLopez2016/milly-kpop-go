using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MillyKpopGo.Auth;

namespace MillyKpopGo.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Los GET /login, /logout y /denegado se sirven desde Pages/Auth/*.razor.
        // Se conservan las peticiones POST HTTP para modificar la cookie de sesión.
        app.MapPost("/auth/login", async (HttpContext ctx, IAntiforgery antiforgery,
            SupabaseLoginService auth, ILoggerFactory loggerFactory) =>
        {
            try { await antiforgery.ValidateRequestAsync(ctx); }
            catch (AntiforgeryValidationException) { return Results.BadRequest("Solicitud inválida."); }

            var form = await ctx.Request.ReadFormAsync();
            var email = form["email"].ToString().Trim();
            var password = form["password"].ToString();
            if (email.Length is < 3 or > 320 || password.Length is < 1 or > 1024)
                return Results.Redirect("/login?error=1");

            LoginResult result;
            try
            {
                result = await auth.LoginAsync(email, password, ctx.RequestAborted);
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("AuthEndpoints").LogError(ex, "Error interno al iniciar sesión");
                return Results.Redirect("/login?error=1");
            }

            if (!result.Success)
            {
                loggerFactory.CreateLogger("AuthEndpoints").LogWarning("Login rechazado: {Motivo}", result.Message);
                return Results.Redirect("/login?error=1");
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, result.UserId!),
                new Claim(ClaimTypes.Name, result.Email!),
                new Claim(ClaimTypes.Role, result.Role!)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var ticket = new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30),
                AllowRefresh = false
            };
            ticket.StoreTokens(new[] { new AuthenticationToken { Name = "access_token", Value = result.AccessToken! } });
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), ticket);
            return Results.Redirect(result.Role == "ADMIN" ? "/admin" : "/mi-cuenta");
        }).DisableAntiforgery(); // La validación del token se realiza explícitamente arriba.

        app.MapPost("/auth/logout", async (HttpContext ctx, IAntiforgery antiforgery) =>
        {
            try { await antiforgery.ValidateRequestAsync(ctx); }
            catch (AntiforgeryValidationException) { return Results.BadRequest("Solicitud inválida."); }
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).DisableAntiforgery();

        return app;
    }
}
