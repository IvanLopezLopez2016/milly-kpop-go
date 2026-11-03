using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace MillyKpopGo.Articulos;

// Acceso exclusivamente desde el servidor con JWT autenticado y RLS de Supabase.
public static class ArticulosEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapArticulosEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/articulos")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "ADMIN" });
        // GET migrado a Components/Pages/Articulos.razor
        // GET migrado a Components/Pages/ArticuloEditar.razor
        group.MapPost("/guardar", Guardar);
        group.MapPost("/estado", CambiarEstado);
        return endpoints;
    }

    private static async Task<IResult> Listar(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg,
        IAntiforgery anti, string? q = null, string? aviso = null)
    {
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrEmpty(jwt)) return Results.Redirect("/logout");
        var (items, error) = await ObtenerLista(factory, cfg, jwt, ctx.RequestAborted);
        if (error != null) return Html(Pagina("Artículos", $"<div class='go-warning'>{E(error)}</div>"), 502);
        var filtro = (q ?? "").Trim();
        if (filtro.Length > 0)
            items = items.Where(x => new[] { x.Nombre, x.Sku, x.CodigoBarras, x.Grupo, x.Variante }
                .Any(s => (s ?? "").Contains(filtro, StringComparison.OrdinalIgnoreCase))).ToList();
        var csrf = anti.GetAndStoreTokens(ctx).RequestToken ?? "";
        var html = new StringBuilder();
        html.Append("<section class='go-panel'><h1>Catálogo de artículos</h1><p>Crea cada producto una vez y reutilízalo en futuros encargos. El precio es solo una referencia.</p>");
        if (aviso is "creado" or "editado" or "estado") html.Append("<div class='go-ok'>Operación guardada correctamente.</div>");
        html.Append("<form method='get' class='go-search' action='/admin/articulos'><input name='q' aria-label='Buscar artículo' placeholder='Nombre, SKU, código de barras, grupo o variante' value='")
            .Append(E(filtro)).Append("'><button>Buscar</button><a href='/admin/articulos' class='go-btn-secondary'>Limpiar</a></form>");
        html.Append("<div class='go-columns'><div class='go-box'><h2>Nuevo artículo</h2>").Append(Formulario(null, csrf))
            .Append("</div><div class='go-box'><h2>Artículos registrados <small>(").Append(items.Count).Append(")</small></h2>");
        if (items.Count == 0) html.Append("<p>No hay artículos para mostrar.</p>");
        else
        {
            html.Append("<div class='go-table-wrap'><table class='go-table'><thead><tr><th>Artículo</th><th>Códigos</th><th>Precio referencia</th><th>Estado</th><th></th></tr></thead><tbody>");
            foreach (var a in items)
            {
                html.Append("<tr><td><strong>").Append(E(a.Nombre)).Append("</strong><br><small>").Append(E(a.Grupo))
                    .Append(" · ").Append(E(a.Variante)).Append("</small></td><td>").Append(E(a.Sku))
                    .Append("<br><small>").Append(E(a.CodigoBarras)).Append("</small></td><td>")
                    .Append(a.PrecioReferencia.HasValue ? "$" + a.PrecioReferencia.Value.ToString("N2", CultureInfo.GetCultureInfo("es-MX")) : "Sin definir")
                    .Append("</td><td>").Append(a.Activo ? "Activo" : "Inactivo").Append("</td><td><a href='/admin/articulos/editar/")
                    .Append(a.Id.ToString("D")).Append("'>Editar</a></td></tr>");
            }
            html.Append("</tbody></table></div>");
        }
        html.Append("</div></div></section>");
        return Html(Pagina("Artículos", html.ToString()));
    }

    private static async Task<IResult> Editar(Guid id, HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti)
    {
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrEmpty(jwt)) return Results.Redirect("/logout");
        var (items, error) = await ObtenerLista(factory, cfg, jwt, ctx.RequestAborted);
        if (error != null) return Html(Pagina("Error", E(error)), 502);
        var item = items.FirstOrDefault(x => x.Id == id);
        if (item is null) return Results.NotFound();
        var csrf = anti.GetAndStoreTokens(ctx).RequestToken ?? "";
        var html = $"<section class='go-panel'><h1>Editar artículo</h1><div class='go-box go-edit'>{Formulario(item, csrf)}";
        html += $"<form method='post' action='/admin/articulos/estado'><input type='hidden' name='__RequestVerificationToken' value='{E(csrf)}'><input type='hidden' name='id' value='{item.Id:D}'><input type='hidden' name='activo' value='{(!item.Activo).ToString().ToLowerInvariant()}'><button class='go-btn-secondary' type='submit'>{(item.Activo ? "Desactivar" : "Reactivar")} artículo</button></form></div></section>";
        return Html(Pagina("Editar artículo", html));
    }

    private static async Task<IResult> Guardar(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti)
    {
        if (!await CsrfValido(ctx, anti)) return Results.BadRequest("Solicitud inválida.");
        var f = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var idText = f["id"].ToString();
        var editing = idText.Length > 0;
        if (editing && !Guid.TryParse(idText, out _)) return Results.BadRequest("Identificador inválido.");
        var nombre = f["nombre"].ToString().Trim();
        var sku = NullVacio(f["sku"].ToString());
        var codigo = NullVacio(f["codigo_barras"].ToString());
        var grupo = NullVacio(f["grupo"].ToString());
        var variante = NullVacio(f["variante"].ToString());
        var precioText = f["precio_referencia"].ToString().Trim();
        decimal? precio = null;
        if (!string.IsNullOrEmpty(precioText))
        {
            if (!decimal.TryParse(precioText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var p) || p < 0 || p > 9999999999.99m)
                return FormError("Precio inválido. Usa punto decimal, por ejemplo 180.50.");
            precio = p;
        }
        if (nombre.Length is < 2 or > 200 || (sku?.Length ?? 0) > 100 || (codigo?.Length ?? 0) > 150 ||
            (grupo?.Length ?? 0) > 120 || (variante?.Length ?? 0) > 160)
            return FormError("Revisa el nombre y la longitud de los campos.");
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrEmpty(jwt)) return Results.Redirect("/logout");
        using var http = CrearCliente(factory, cfg, jwt);
        using var req = new HttpRequestMessage(editing ? HttpMethod.Patch : HttpMethod.Post,
            editing ? $"articulos?id=eq.{Guid.Parse(idText):D}" : "articulos");
        req.Content = new StringContent(JsonSerializer.Serialize(new { nombre, sku, codigo_barras = codigo, grupo, variante, precio_referencia = precio }, JsonOptions), Encoding.UTF8, "application/json");
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        try
        {
            using var res = await http.SendAsync(req, ctx.RequestAborted);
            if (!res.IsSuccessStatusCode)
            {
                var error = await res.Content.ReadAsStringAsync(ctx.RequestAborted);
                return FormError(error.Contains("23505", StringComparison.Ordinal) ? "Ya existe un artículo con ese SKU." : "No se pudo guardar. Comprueba los datos y permisos.", (int)res.StatusCode);
            }
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ctx.RequestAborted));
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() != 1)
                return FormError("No se confirmó la escritura del artículo.", 403);
        }
        catch (HttpRequestException) { return FormError("No se pudo conectar con Supabase.", 502); }
        return Results.Redirect("/admin/articulos?aviso=" + (editing ? "editado" : "creado"));
    }

    private static async Task<IResult> CambiarEstado(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti)
    {
        if (!await CsrfValido(ctx, anti)) return Results.BadRequest("Solicitud inválida.");
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        if (!Guid.TryParse(form["id"].ToString(), out var id) || !bool.TryParse(form["activo"].ToString(), out var activo)) return Results.BadRequest("Datos inválidos.");
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrEmpty(jwt)) return Results.Redirect("/logout");
        using var http = CrearCliente(factory, cfg, jwt);
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"articulos?id=eq.{id:D}");
        req.Content = new StringContent(JsonSerializer.Serialize(new { activo }), Encoding.UTF8, "application/json");
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        try
        {
            using var res = await http.SendAsync(req, ctx.RequestAborted);
            if (!res.IsSuccessStatusCode) return FormError("No se pudo cambiar el estado.", 502);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ctx.RequestAborted));
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() != 1) return Results.NotFound();
        }
        catch (HttpRequestException) { return FormError("No se pudo conectar con Supabase.", 502); }
        return Results.Redirect("/admin/articulos?aviso=estado");
    }

    private static async Task<(List<Articulo> Items, string? Error)> ObtenerLista(IHttpClientFactory factory, IConfiguration cfg, string jwt, CancellationToken ct)
    {
        using var http = CrearCliente(factory, cfg, jwt);
        try
        {
            using var res = await http.GetAsync("articulos?select=id,sku,codigo_barras,nombre,grupo,variante,precio_referencia,activo&order=creado_en.desc&limit=500", ct);
            if (!res.IsSuccessStatusCode) return ([], "No se pudo consultar el catálogo. Revisa tu sesión y los permisos RLS.");
            return (await res.Content.ReadFromJsonAsync<List<Articulo>>(JsonOptions, ct) ?? [], null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        { return ([], "No fue posible obtener los artículos de Supabase."); }
    }

    private static HttpClient CrearCliente(IHttpClientFactory factory, IConfiguration cfg, string jwt)
    {
        var url = cfg["Supabase:Url"]?.TrimEnd('/') ?? throw new InvalidOperationException("Falta Supabase:Url");
        var key = cfg["Supabase:PublishableKey"] ?? throw new InvalidOperationException("Falta Supabase:PublishableKey");
        var http = factory.CreateClient();
        http.BaseAddress = new Uri(url + "/rest/v1/");
        http.DefaultRequestHeaders.Add("apikey", key);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        http.Timeout = TimeSpan.FromSeconds(20);
        return http;
    }

    private static string Formulario(Articulo? a, string csrf) => $"""
        <form method="post" action="/admin/articulos/guardar" class="go-buyer-form">
          <input type="hidden" name="__RequestVerificationToken" value="{E(csrf)}">
          <input type="hidden" name="id" value="{(a is null ? "" : a.Id.ToString("D"))}">
          <label>Nombre del artículo *<input name="nombre" required minlength="2" maxlength="200" value="{E(a?.Nombre)}"></label>
          <label>SKU interno<input name="sku" maxlength="100" value="{E(a?.Sku)}" placeholder="Opcional"></label>
          <label>Código de barras<input name="codigo_barras" maxlength="150" value="{E(a?.CodigoBarras)}" placeholder="Opcional"></label>
          <label>Grupo / artista<input name="grupo" maxlength="120" value="{E(a?.Grupo)}" placeholder="Ej. BTS, ENHYPEN"></label>
          <label>Variante<input name="variante" maxlength="160" value="{E(a?.Variante)}" placeholder="Ej. Weverse, versión random"></label>
          <label>Precio de referencia MXN<input type="number" name="precio_referencia" min="0" step="0.01" value="{(a?.PrecioReferencia.HasValue == true ? a.PrecioReferencia.Value.ToString("0.00", CultureInfo.InvariantCulture) : "")}"></label>
          <button type="submit">{(a is null ? "Registrar artículo" : "Guardar cambios")}</button>
          {(a is null ? "" : "<p><a href='/admin/articulos'>Volver al catálogo</a></p>")}
        </form>
        """;

    private static string Pagina(string title, string content) => $$"""
        <!doctype html><html lang="es"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{{E(title)}} · Milly K-Pop GO</title><link rel="stylesheet" href="/go.css"><link rel="stylesheet" href="/compradores.css"></head>
        <body><main class="go-shell"><header class="go-header"><div><small>MILLY K-POP GO · ADMIN</small><h1>{{E(title)}}</h1></div><nav><a href="/admin">Inicio</a> · <a href="/admin/compradores">Compradores</a> · <a href="/logout">Salir</a></nav></header>{{content}}</main></body></html>
        """;

    private static IResult FormError(string msg, int status = 400) => Html(Pagina("Error en artículo", $"<div class='go-warning'>{E(msg)}</div><a href='/admin/articulos'>Volver</a>"), status);
    private static IResult Html(string html, int status = 200) => Results.Content(html, "text/html; charset=utf-8", statusCode: status);
    private static string? NullVacio(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static async Task<bool> CsrfValido(HttpContext ctx, IAntiforgery anti)
    {
        try { await anti.ValidateRequestAsync(ctx); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }

    private sealed class Articulo
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("sku")] public string? Sku { get; set; }
        [JsonPropertyName("codigo_barras")] public string? CodigoBarras { get; set; }
        [JsonPropertyName("nombre")] public string? Nombre { get; set; }
        [JsonPropertyName("grupo")] public string? Grupo { get; set; }
        [JsonPropertyName("variante")] public string? Variante { get; set; }
        [JsonPropertyName("precio_referencia")] public decimal? PrecioReferencia { get; set; }
        [JsonPropertyName("activo")] public bool Activo { get; set; }
    }
}
