using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace MillyKpopGo.Pedidos;

// Sustituye el endpoint 012.4: la funcion anterior NO debe seguir utilizandose.
public static class PedidoEliminarEndpoints
{
    public static IEndpointRouteBuilder MapEliminarPedidoArticuloEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/pedidos/{pedidoId:guid}/articulos/{detalleId:guid}/eliminar", Cancelar)
           .RequireAuthorization(new AuthorizeAttribute { Roles="ADMIN" }).DisableAntiforgery();
        return app;
    }
    private static async Task<IResult> Cancelar(Guid pedidoId, Guid detalleId, HttpContext ctx,
        IAntiforgery antiforgery,IHttpClientFactory factory,IConfiguration config)
    {
        try { await antiforgery.ValidateRequestAsync(ctx); }
        catch (AntiforgeryValidationException) { return Results.BadRequest("Solicitud no valida."); }
        var token=await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token)) return Results.Redirect("/login");
        var url=config["Supabase:Url"]?.TrimEnd('/'); var key=config["Supabase:PublishableKey"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key)) return Results.Problem("Supabase no configurado.");
        var f=await ctx.Request.ReadFormAsync();
        var aceptar=f["aceptar_movimientos"].ToString()=="true";
        using var http=factory.CreateClient();
        http.BaseAddress=new Uri(url+"/rest/v1/");
        http.DefaultRequestHeaders.Add("apikey",key);
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var r=await http.PostAsJsonAsync("rpc/cancelar_detalle_go",
          new { p_pedido_id=pedidoId,p_detalle_id=detalleId,p_aceptar_movimientos=aceptar },ctx.RequestAborted);
        if (!r.IsSuccessStatusCode)
            return Results.Redirect($"/admin/pedidos/{pedidoId:D}?eliminar=error");
        var status=JsonSerializer.Deserialize<string>(await r.Content.ReadAsStringAsync(ctx.RequestAborted));
        return status switch {
          "REQUIERE_CONFIRMACION" => Results.Redirect($"/admin/pedidos/{pedidoId:D}?eliminar=movimientos&detalle={detalleId:D}"),
          "STOCK" => Results.Redirect($"/admin/pedidos/{pedidoId:D}?eliminar=stock"),
          "ELIMINADO" => Results.Redirect($"/admin/pedidos/{pedidoId:D}?eliminar=ok"),
          "YA_CANCELADO" => Results.Redirect($"/admin/pedidos/{pedidoId:D}?eliminar=ya"),
          _ => Results.Redirect($"/admin/pedidos/{pedidoId:D}?eliminar=error")
        };
    }
}
