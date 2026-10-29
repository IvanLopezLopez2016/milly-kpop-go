using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace MillyKpopGo.Compradores;

// Operaciones HTTP en el SERVIDOR. Se usa el JWT del ADMIN con las políticas RLS
// de Supabase, nunca una key administrativa. La cookie ASP.NET protege el JWT.
public static class CompradoresEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapCompradoresEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/compradores")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "ADMIN" });

        group.MapGet("", Listar);
        group.MapGet("/editar/{id:guid}", Editar);
        group.MapPost("/guardar", Guardar);
        group.MapPost("/estado", CambiarEstado);
        return endpoints;
    }

    private static async Task<IResult> Listar(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti, string? q = null, string? aviso = null)
    {
        var token = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token)) return Results.Redirect("/logout");
        var (items, error) = await ObtenerLista(factory, cfg, token, ctx.RequestAborted);
        if (error != null) return Html(Pagina("Compradores", $"<div class='go-warning'>{E(error)}</div><a href='/admin'>Volver</a>"), 502);
        var filtro = (q ?? "").Trim();
        if (filtro.Length > 0)
            items = items.Where(x => (x.Nombre ?? "").Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                                     (x.Alias ?? "").Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                                     (x.Correo ?? "").Contains(filtro, StringComparison.OrdinalIgnoreCase)).ToList();
        var csrf = anti.GetAndStoreTokens(ctx).RequestToken ?? "";
        var html = new StringBuilder();
        html.Append("<section class='go-panel'><h1>Compradores</h1><p>Registra una sola vez a cada persona. Las invitaciones estarán disponibles en una próxima entrega.</p>");
        if (aviso == "creado") html.Append("<div class='go-ok'>Comprador registrado correctamente.</div>");
        if (aviso == "editado") html.Append("<div class='go-ok'>Cambios guardados.</div>");
        if (aviso == "estado") html.Append("<div class='go-ok'>Estado actualizado.</div>");
        html.Append("<form method='get' action='/admin/compradores' class='go-search'><input aria-label='Buscar comprador' name='q' placeholder='Buscar por nombre, alias o correo' value='").Append(E(filtro)).Append("'><button>Buscar</button><a class='go-btn-secondary' href='/admin/compradores'>Limpiar</a></form>");
        html.Append("<div class='go-columns'><div class='go-box'><h2>Nuevo comprador</h2>");
        html.Append(Formulario(null, csrf));
        html.Append("</div><div class='go-box'><h2>Compradores registrados <small>(").Append(items.Count).Append(")</small></h2>");
        if (items.Count == 0) html.Append("<p>No hay compradores para mostrar.</p>");
        else
        {
            html.Append("<div class='go-table-wrap'><table class='go-table'><thead><tr><th>Comprador</th><th>Contacto</th><th>Portal</th><th>Estado</th><th></th></tr></thead><tbody>");
            foreach (var item in items)
            {
                html.Append("<tr><td><strong>").Append(E(item.Nombre)).Append("</strong><br><small>").Append(E(item.Alias)).Append("</small></td><td>")
                    .Append(E(item.Correo)).Append("<br><small>").Append(E(item.Whatsapp)).Append("</small></td><td>")
                    .Append(item.AuthUserId.HasValue ? "Vinculado" : "Sin acceso").Append("</td><td>")
                    .Append(item.Activo ? "Activo" : "Inactivo").Append("</td><td><a href='/admin/compradores/editar/")
                    .Append(item.Id.ToString("D")).Append("'>Editar</a></td></tr>");
            }
            html.Append("</tbody></table></div>");
        }
        html.Append("</div></div></section>");
        return Html(Pagina("Compradores", html.ToString()));
    }

    private static async Task<IResult> Editar(Guid id, HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti)
    {
        var token = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token)) return Results.Redirect("/logout");
        var (items, error) = await ObtenerLista(factory, cfg, token, ctx.RequestAborted);
        if (error != null) return Html(Pagina("Error", $"<div class='go-warning'>{E(error)}</div>"), 502);
        var comprador = items.FirstOrDefault(x => x.Id == id);
        if (comprador is null) return Results.NotFound();
        var csrf = anti.GetAndStoreTokens(ctx).RequestToken ?? "";
        var cuerpo = $"<section class='go-panel'><h1>Editar comprador</h1><div class='go-box go-edit'>{Formulario(comprador, csrf)}";
        cuerpo += $"<form method='post' action='/admin/compradores/estado'><input type='hidden' name='__RequestVerificationToken' value='{E(csrf)}'><input type='hidden' name='id' value='{comprador.Id:D}'><input type='hidden' name='activo' value='{(!comprador.Activo).ToString().ToLowerInvariant()}'><button class='go-btn-secondary' type='submit'>{(comprador.Activo ? "Desactivar" : "Reactivar")} comprador</button></form>";
        cuerpo += "</div></section>";
        return Html(Pagina("Editar comprador", cuerpo));
    }

    private static async Task<IResult> Guardar(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti)
    {
        if (!await CsrfValido(ctx, anti)) return Results.BadRequest("Solicitud inválida.");
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var idText = form["id"].ToString();
        var editando = !string.IsNullOrEmpty(idText);
        if (editando && !Guid.TryParse(idText, out _)) return Results.BadRequest("ID inválido.");
        var nombre = form["nombre"].ToString().Trim();
        var alias = NullSiVacio(form["alias"].ToString());
        var correo = NullSiVacio(form["correo"].ToString())?.ToLowerInvariant();
        var whatsapp = NullSiVacio(form["whatsapp"].ToString());
        if (nombre.Length is < 2 or > 160 || (alias?.Length ?? 0) > 100 || (correo?.Length ?? 0) > 254 || (whatsapp?.Length ?? 0) > 40 ||
            (correo != null && (!correo.Contains('@') || correo.Contains(' '))))
            return Html(Pagina("Error al guardar", "<div class='go-warning'>Revisa el nombre (2–160 caracteres), alias, correo y WhatsApp.</div><a href='/admin/compradores'>Volver</a>"), 400);
        var token = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token)) return Results.Redirect("/logout");
        using var http = CrearCliente(factory, cfg, token);
        var url = editando ? $"compradores?id=eq.{Guid.Parse(idText):D}" : "compradores";
        var payload = new { nombre, alias, correo, whatsapp };
        using var req = new HttpRequestMessage(editando ? HttpMethod.Patch : HttpMethod.Post, url);
        req.Content = JsonContent(payload);
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        try
        {
            using var res = await http.SendAsync(req, ctx.RequestAborted);
            if (!res.IsSuccessStatusCode)
            {
                var raw = await res.Content.ReadAsStringAsync(ctx.RequestAborted);
                var mensaje = raw.Contains("23505") ? "Ya existe un comprador con ese correo." : "No se pudo guardar. Verifica los datos y permisos en Supabase.";
                return Html(Pagina("Error al guardar", $"<div class='go-warning'>{E(mensaje)}</div><a href='/admin/compradores'>Volver</a>"), (int)res.StatusCode);
            }
            var response = await res.Content.ReadAsStringAsync(ctx.RequestAborted);
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() != 1)
                return Html(Pagina("Sin cambios", "<div class='go-warning'>No se encontró un registro editable o falta autorización.</div><a href='/admin/compradores'>Volver</a>"), 403);
        }
        catch (HttpRequestException)
        {
            return Html(Pagina("Sin conexión", "<div class='go-warning'>No fue posible conectar con Supabase.</div><a href='/admin/compradores'>Volver</a>"), 502);
        }
        return Results.Redirect("/admin/compradores?aviso=" + (editando ? "editado" : "creado"));
    }

    private static async Task<IResult> CambiarEstado(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti)
    {
        if (!await CsrfValido(ctx, anti)) return Results.BadRequest("Solicitud inválida.");
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        if (!Guid.TryParse(form["id"].ToString(), out var id) || !bool.TryParse(form["activo"].ToString(), out var activo)) return Results.BadRequest("Datos inválidos.");
        var token = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token)) return Results.Redirect("/logout");
        using var http = CrearCliente(factory, cfg, token);
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"compradores?id=eq.{id:D}");
        req.Content = JsonContent(new { activo });
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        try
        {
            using var res = await http.SendAsync(req, ctx.RequestAborted);
            if (!res.IsSuccessStatusCode) return Html(Pagina("Error", "<p>No se pudo actualizar el estado.</p>"), 502);
            var json = await res.Content.ReadAsStringAsync(ctx.RequestAborted);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetArrayLength() != 1) return Results.NotFound();
        }
        catch (HttpRequestException) { return Html(Pagina("Error", "<p>Supabase no está disponible.</p>"), 502); }
        return Results.Redirect("/admin/compradores?aviso=estado");
    }

    private static async Task<(List<Comprador> Items, string? Error)> ObtenerLista(IHttpClientFactory factory, IConfiguration cfg, string token, CancellationToken ct)
    {
        using var http = CrearCliente(factory, cfg, token);
        try
        {
            using var res = await http.GetAsync("compradores?select=id,nombre,alias,correo,whatsapp,activo,auth_user_id,creado_en&order=creado_en.desc&limit=500", ct);
            if (!res.IsSuccessStatusCode) return ([], "No se pudieron consultar compradores. Comprueba los permisos de Supabase.");
            var datos = await res.Content.ReadFromJsonAsync<List<Comprador>>(JsonOptions, ct);
            return (datos ?? [], null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        { return ([], "No fue posible obtener la información de Supabase."); }
    }

    private static HttpClient CrearCliente(IHttpClientFactory factory, IConfiguration cfg, string jwt)
    {
        var url = cfg["Supabase:Url"]?.TrimEnd('/') ?? throw new InvalidOperationException("Falta Supabase:Url");
        var apiKey = cfg["Supabase:PublishableKey"] ?? throw new InvalidOperationException("Falta Supabase:PublishableKey");
        var http = factory.CreateClient();
        http.BaseAddress = new Uri(url + "/rest/v1/");
        http.DefaultRequestHeaders.Add("apikey", apiKey);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        http.Timeout = TimeSpan.FromSeconds(20);
        return http;
    }

    private static StringContent JsonContent<T>(T value) => new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");
    private static string? NullSiVacio(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static IResult Html(string markup, int status = 200) => Results.Content(markup, "text/html; charset=utf-8", statusCode: status);
    private static async Task<bool> CsrfValido(HttpContext ctx, IAntiforgery anti)
    {
        try { await anti.ValidateRequestAsync(ctx); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }

    private static string Formulario(Comprador? c, string csrf) => $"""
        <form method="post" action="/admin/compradores/guardar" class="go-buyer-form">
          <input type="hidden" name="__RequestVerificationToken" value="{E(csrf)}">
          <input type="hidden" name="id" value="{(c == null ? "" : c.Id.ToString("D"))}">
          <label>Nombre completo *<input name="nombre" required minlength="2" maxlength="160" value="{E(c?.Nombre)}"></label>
          <label>Alias<input name="alias" maxlength="100" value="{E(c?.Alias)}"></label>
          <label>Correo<input type="email" name="correo" maxlength="254" value="{E(c?.Correo)}"></label>
          <label>WhatsApp<input type="tel" name="whatsapp" maxlength="40" value="{E(c?.Whatsapp)}"></label>
          <button type="submit">{(c == null ? "Registrar comprador" : "Guardar cambios")}</button>
          {(c == null ? "" : "<p><a href='/admin/compradores'>← Volver al listado</a></p>")}
        </form>
        """;

    private static string Pagina(string titulo, string contenido) => $$"""
        <!doctype html><html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{{E(titulo)}} · Milly K-Pop GO</title><link rel="stylesheet" href="/go.css"><link rel="stylesheet" href="/compradores.css"></head>
        <body><main class="go-shell"><header class="go-header"><div><small>MILLY K-POP GO · ADMIN</small><h1>{{E(titulo)}}</h1></div><nav><a href="/admin">Inicio</a> · <a href="/logout">Salir</a></nav></header>{{contenido}}</main></body></html>
        """;

    private sealed class Comprador
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("nombre")] public string? Nombre { get; set; }
        [JsonPropertyName("alias")] public string? Alias { get; set; }
        [JsonPropertyName("correo")] public string? Correo { get; set; }
        [JsonPropertyName("whatsapp")] public string? Whatsapp { get; set; }
        [JsonPropertyName("activo")] public bool Activo { get; set; }
        [JsonPropertyName("auth_user_id")] public Guid? AuthUserId { get; set; }
    }
}
