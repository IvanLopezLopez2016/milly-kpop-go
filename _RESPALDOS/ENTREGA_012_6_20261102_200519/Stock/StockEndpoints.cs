using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;

namespace MillyKpopGo.Stock;

public static class StockEndpoints
{
    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/stock/agregar", Agregar).RequireAuthorization(new AuthorizeAttribute { Roles="ADMIN" }).DisableAntiforgery();
        app.MapPost("/admin/stock/asignar", Asignar).RequireAuthorization(new AuthorizeAttribute { Roles="ADMIN" }).DisableAntiforgery();
        return app;
    }

    private static async Task<HttpClient?> Client(HttpContext ctx, IHttpClientFactory factory, IConfiguration config)
    {
        var token = await ctx.GetTokenAsync("access_token");
        var url = config["Supabase:Url"]?.TrimEnd('/');
        var key = config["Supabase:PublishableKey"];
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key)) return null;
        var http = factory.CreateClient();
        http.BaseAddress = new Uri(url + "/rest/v1/");
        http.DefaultRequestHeaders.Add("apikey",key);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",token);
        return http;
    }

    private static async Task<bool> Validar(HttpContext ctx, IAntiforgery antiforgery)
    {
        try { await antiforgery.ValidateRequestAsync(ctx); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }
    private static async Task<IResult> Agregar(HttpContext ctx, IAntiforgery antiforgery, IHttpClientFactory factory, IConfiguration config)
    {
        if (!await Validar(ctx,antiforgery)) return Results.BadRequest("Token de formulario no valido.");
        var f=await ctx.Request.ReadFormAsync();
        var nombre=f["nombre"].ToString().Trim();
        if (nombre.Length is < 1 or > 250 || !int.TryParse(f["cantidad"],out var cantidad) || cantidad is < 1 or > 9999
           || !decimal.TryParse(f["precio"],NumberStyles.Number,CultureInfo.InvariantCulture,out var precio) || precio < 0)
            return Results.Redirect("/admin/stock?resultado=datos");
        using var http=await Client(ctx,factory,config);
        if (http is null) return Results.Redirect("/login");
        using var r=await http.PostAsJsonAsync("rpc/agregar_stock_manual_go",new { p_nombre=nombre,p_cantidad=cantidad,p_precio=precio,p_nota=f["nota"].ToString() });
        return Results.Redirect(r.IsSuccessStatusCode ? "/admin/stock?resultado=agregado" : "/admin/stock?resultado=error");
    }
    private static async Task<IResult> Asignar(HttpContext ctx, IAntiforgery antiforgery, IHttpClientFactory factory, IConfiguration config)
    {
        if (!await Validar(ctx,antiforgery)) return Results.BadRequest("Token de formulario no valido.");
        var f=await ctx.Request.ReadFormAsync();
        if (!Guid.TryParse(f["stock_id"],out var stockId) || !Guid.TryParse(f["comprador_id"],out var compradorId)
            || !int.TryParse(f["cantidad"],out var cantidad) || cantidad < 1)
            return Results.Redirect("/admin/stock?resultado=datos");
        using var http=await Client(ctx,factory,config);
        if (http is null) return Results.Redirect("/login");
        using var r=await http.PostAsJsonAsync("rpc/asignar_stock_go",new { p_stock_id=stockId,p_comprador_id=compradorId,p_cantidad=cantidad });
        if (!r.IsSuccessStatusCode) return Results.Redirect("/admin/stock?resultado=error");
        var json=await r.Content.ReadAsStringAsync();
        // PostgREST devuelve UUID JSON string; nunca usar una URL aportada por el cliente.
        try
        {
            var id=JsonSerializer.Deserialize<Guid>(json);
            return Results.Redirect($"/admin/pedidos/{id:D}");
        }
        catch (JsonException) { return Results.Redirect("/admin/stock?resultado=asignado"); }
    }
}
