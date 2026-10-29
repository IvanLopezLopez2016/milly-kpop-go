using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using System.Net;

namespace MillyKpopGo.Acceso;

public static class CodigosAccesoEndpoints
{
    public static IEndpointRouteBuilder MapCodigosAccesoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/compradores/codigo/generar",Generar)
           .RequireAuthorization(new AuthorizeAttribute { Roles="ADMIN" }).DisableAntiforgery();
        app.MapPost("/registro",Registrar).DisableAntiforgery().RequireRateLimiting("registro");
        return app;
    }
    private static async Task<IResult> Generar(HttpContext ctx,IAntiforgery antiforgery,CodigosAccesoService svc)
    {
        try{await antiforgery.ValidateRequestAsync(ctx);}catch(AntiforgeryValidationException){return Results.BadRequest("Formulario vencido.");}
        var form=await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        if(!Guid.TryParse(form["id"],out var id))return Results.BadRequest("Comprador inválido.");
        try
        {
            var (ok,msg,code)=await svc.GenerarAsync(id,ctx.RequestAborted);
            if(!ok)return Results.Content(Page("No se generó el código",$"<h2>No fue posible generar el código</h2><p>{WebUtility.HtmlEncode(msg)}</p><p><a href='/admin/compradores'>Volver</a></p>"),"text/html; charset=utf-8",statusCode:400);
            ctx.Response.Headers.CacheControl="no-store";
            var html=$"<h1>Código de activación</h1><p>{WebUtility.HtmlEncode(msg)}</p><input readonly value='{WebUtility.HtmlEncode(code)}' aria-label='Código de activación'><p><small>Selecciona el código y cópialo (Ctrl+C). Vence en 48 horas. Generar otro invalida el anterior.</small></p><p><a href='/admin/compradores'>Volver a compradores</a></p>";
            return Results.Content(Page("Código de activación",html),"text/html; charset=utf-8");
        }
        catch(Exception e) when(e is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {return Results.Content(Page("Error","<p>No fue posible conectar con Supabase o falta configuración del servidor.</p><a href='/admin/compradores'>Volver</a>"),"text/html; charset=utf-8",statusCode:503);}
    }
    private static async Task<IResult> Registrar(HttpContext ctx,IAntiforgery antiforgery,CodigosAccesoService svc)
    {
        try{await antiforgery.ValidateRequestAsync(ctx);}catch(AntiforgeryValidationException){return Results.BadRequest("Formulario vencido.");}
        var form=await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var email=form["correo"].ToString().Trim();var code=form["codigo"].ToString();var pass=form["password"].ToString();
        if(pass != form["confirmar"].ToString())return Results.Redirect("/crear-cuenta?error=clave");
        try
        {
            var result=await svc.RegistrarAsync(email,code,pass,ctx.RequestAborted);
            return Results.Redirect(result.Ok?"/login?activada=1":"/crear-cuenta?error=registro");
        }
        catch(Exception e) when(e is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {return Results.Redirect("/crear-cuenta?error=servidor");}
    }
    private static string Page(string title,string body)=>$"<!doctype html><html lang='es'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>{WebUtility.HtmlEncode(title)} · MILLY K-POP GO</title><style>body{{font:16px system-ui;background:#fdf2f8;color:#3b2333;padding:28px}}main{{max-width:530px;margin:5vh auto;background:white;border-radius:16px;padding:30px;box-shadow:0 10px 40px #0001}}input{{font:1.1em system-ui;padding:14px;box-sizing:border-box;width:100%;border:1px solid #ddd;border-radius:8px}}a{{color:#be185d}}</style></head><body><main>{body}</main></body></html>";
}
