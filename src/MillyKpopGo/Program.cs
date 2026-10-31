using MillyKpopGo.Acceso;
using System.Threading.RateLimiting;
using MudBlazor.Services;
using MillyKpopGo.Logistica;
using MillyKpopGo.Pagos;
using MillyKpopGo.Cajas;
using MillyKpopGo.Pedidos;
using MillyKpopGo.Articulos;
using MillyKpopGo.Compradores;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MillyKpopGo.Auth;
using MillyKpopGo.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/denegado";
        options.Cookie.Name = "MillyGo.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // HTTPS obligatorio en producciÃ³n
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = false;
    });
builder.Services.AddHttpClient();
builder.Services.AddScoped<MillyKpopGo.Administradores.AdministradoresService>();
builder.Services.AddScoped<CodigosAccesoService>();
builder.Services.AddRateLimiter(options => {
    options.AddPolicy("registro", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions {
            PermitLimit=5, Window=TimeSpan.FromMinutes(15), QueueLimit=0, AutoReplenishment=true
        }));
    options.RejectionStatusCode=429;
});
builder.Services.AddMudServices();
builder.Services.AddScoped<MillyKpopGo.Dashboard.DashboardUiService>();
builder.Services.AddScoped<MillyKpopGo.Portal.PortalClienteService>();
builder.Services.AddScoped<MillyKpopGo.Pagos.PagosUiService>();
builder.Services.AddScoped<MillyKpopGo.Pedidos.PedidosCajasUiService>();
builder.Services.AddScoped<MillyKpopGo.Catalogos.CatalogosUiService>();
builder.Services.AddScoped<MillyKpopGo.Logistica.LogisticaUiService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SupabaseLoginService>();
builder.Services.AddScoped<MillyKpopGo.Auth.ActivacionCuentaService>();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

static string Page(string title, string body) => $$"""
<!doctype html><html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{{WebUtility.HtmlEncode(title)}} Â· Milly K-Pop GO</title>
<style>body{font-family:system-ui;margin:0;min-height:100vh;display:grid;place-items:center;background:#fdf2f8;color:#30212b}main{background:white;width:min(92vw,390px);padding:28px;border-radius:18px;box-shadow:0 12px 48px #83184317}h1{margin-top:0}label{display:block;margin:16px 0 5px}input{box-sizing:border-box;width:100%;padding:11px;border:1px solid #d1d5db;border-radius:9px}button{background:#be185d;border:0;color:white;border-radius:9px;padding:12px 20px;margin-top:19px;width:100%;cursor:pointer}a{color:#be185d}p{line-height:1.5}</style></head><body><main>{{body}}</main></body></html>
""";

app.MapGet("/login", (HttpContext ctx, IAntiforgery antiforgery) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true) return Results.Redirect(ctx.User.IsInRole("ADMIN") ? "/admin" : "/mi-cuenta");
    var token = antiforgery.GetAndStoreTokens(ctx).RequestToken!;
    var warning = ctx.Request.Query.ContainsKey("error") ? "<p role='alert'>No fue posible iniciar sesiÃ³n. Verifica tus credenciales y permisos.</p>" : "";
    var html = Page("Acceso", $"<h1>ðŸŒ¸ Milly K-Pop GO</h1><p>Acceso exclusivo por invitaciÃ³n.</p>{warning}<form method='post' action='/login'><input type='hidden' name='__RequestVerificationToken' value='{WebUtility.HtmlEncode(token)}'><label>Correo electrÃ³nico</label><input name='email' type='email' required autocomplete='username'><label>ContraseÃ±a</label><input name='password' type='password' required autocomplete='current-password'><button type='submit'>Iniciar sesiÃ³n</button></form><p><a href='/crear-cuenta'>Crear mi cuenta</a></p>");
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/login", async (HttpContext ctx, IAntiforgery antiforgery, SupabaseLoginService auth) =>
{
    try { await antiforgery.ValidateRequestAsync(ctx); }
    catch (AntiforgeryValidationException) { return Results.BadRequest("Solicitud invÃ¡lida."); }
    var form = await ctx.Request.ReadFormAsync();
    var email = form["email"].ToString().Trim();
    var password = form["password"].ToString();
    if (email.Length is < 3 or > 320 || password.Length is < 1 or > 1024) return Results.Redirect("/login?error=1");
    LoginResult result;

    try
    {
        result = await auth.LoginAsync(
            email,
            password,
            ctx.RequestAborted
        );
    }
    catch (Exception ex)
    {
        app.Logger.LogError(
            ex,
            "Error interno al iniciar sesiÃ³n"
        );

        return Results.Redirect("/login?error=1");
    }

    if (!result.Success)
    {
        app.Logger.LogWarning(
            "Login rechazado: {Motivo}",
            result.Message
        );

        return Results.Redirect("/login?error=1");
    }
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, result.UserId!),
        new Claim(ClaimTypes.Name, result.Email!),
        new Claim(ClaimTypes.Role, result.Role!)
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var ticket = new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30), AllowRefresh = false };
    ticket.StoreTokens(new[] { new AuthenticationToken { Name = "access_token", Value = result.AccessToken! } });
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), ticket);
    return Results.Redirect(result.Role == "ADMIN" ? "/admin" : "/mi-cuenta");
}).DisableAntiforgery(); // Se valida explÃ­citamente arriba.

app.MapGet("/logout", (HttpContext ctx, IAntiforgery antiforgery) =>
{
    var token = antiforgery.GetAndStoreTokens(ctx).RequestToken!;
    return Results.Content(Page("Salir", $"<h1>Cerrar sesiÃ³n</h1><form action='/logout' method='post'><input type='hidden' name='__RequestVerificationToken' value='{WebUtility.HtmlEncode(token)}'><button>Confirmar salida</button></form><p><a href='/'>Volver</a></p>"), "text/html; charset=utf-8");
});
app.MapPost("/logout", async (HttpContext ctx, IAntiforgery antiforgery) =>
{
    try { await antiforgery.ValidateRequestAsync(ctx); }
    catch (AntiforgeryValidationException) { return Results.BadRequest("Solicitud invÃ¡lida."); }
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

app.MapGet("/denegado", () => Results.Content(Page("Acceso denegado", "<h1>Acceso denegado</h1><p>Tu cuenta no tiene permisos para ver esta secciÃ³n.</p><a href='/'>Volver al inicio</a>"), "text/html; charset=utf-8"));
app.MapCodigosAccesoEndpoints();
app.MapCompradoresEndpoints();
app.MapArticulosEndpoints();
app.MapPedidosEndpoints();
app.MapCajasEndpoints();
app.MapPagosEndpoints();
app.MapLogisticaEndpoints();
// 010.2: guarda únicamente la procedencia visual del Dashboard (no es autenticación).
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    if (HttpMethods.IsGet(ctx.Request.Method) && path.Equals("/admin", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.Cookies.Delete("MillyGo.NavOrigin", new CookieOptions { Path = "/admin" });
    }
    else if (HttpMethods.IsGet(ctx.Request.Method) && path.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase)
             && ctx.Request.Query.TryGetValue("origen", out var origen) && origen == "dashboard")
    {
        ctx.Response.Cookies.Append("MillyGo.NavOrigin", "dashboard", new CookieOptions
        {
            HttpOnly = true, Secure = ctx.Request.IsHttps, SameSite = SameSiteMode.Lax,
            Path = "/admin", IsEssential = true
        });
    }
    await next();
});
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
