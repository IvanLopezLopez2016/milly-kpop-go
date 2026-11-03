using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace MillyKpopGo.Pedidos;

public static class PedidoEliminarEndpoints
{
    public static IEndpointRouteBuilder MapEliminarPedidoArticuloEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/pedidos/{pedidoId:guid}/articulos/{detalleId:guid}/eliminar", Eliminar)
           .RequireAuthorization(new AuthorizeAttribute { Roles = "ADMIN" })
           .DisableAntiforgery(); // Token verificado explícitamente dentro del handler.
        return app;
    }

    private static async Task<IResult> Eliminar(
        Guid pedidoId,
        Guid detalleId,
        HttpContext ctx,
        IAntiforgery antiforgery,
        IHttpClientFactory factory,
        IConfiguration config)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(ctx);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest("Solicitud inválida. Actualiza la página e inténtalo nuevamente.");
        }

        var token = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token))
            return Results.Redirect("/login");

        var url = config["Supabase:Url"]?.TrimEnd('/');
        var key = config["Supabase:PublishableKey"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            return Results.Problem("Falta configurar Supabase en el servidor.", statusCode: 500);

        using var http = factory.CreateClient();
        http.BaseAddress = new Uri(url + "/rest/v1/");
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.Add("apikey", key);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.PostAsJsonAsync(
            "rpc/eliminar_articulo_pedido_go",
            new { p_pedido_id = pedidoId, p_detalle_id = detalleId },
            ctx.RequestAborted);

        if (response.IsSuccessStatusCode)
            return Results.Redirect($"/admin/pedidos/{pedidoId:D}?eliminar=ok");

        var error = await response.Content.ReadAsStringAsync(ctx.RequestAborted);
        if (error.Contains("MOVIMIENTOS_ASOCIADOS", StringComparison.Ordinal))
            return Results.Redirect($"/admin/pedidos/{pedidoId:D}?eliminar=movimientos");
        if (error.Contains("ARTICULO_NO_ENCONTRADO", StringComparison.Ordinal))
            return Results.Redirect($"/admin/pedidos/{pedidoId:D}?eliminar=noencontrado");

        // No incluir respuestas crudas de Supabase en el navegador.
        return Results.Redirect($"/admin/pedidos/{pedidoId:D}?eliminar=error");
    }
}
