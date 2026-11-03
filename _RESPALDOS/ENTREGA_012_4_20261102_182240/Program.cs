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

app.MapAuthEndpoints();
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
