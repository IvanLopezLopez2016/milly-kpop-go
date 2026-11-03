using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace MillyKpopGo.Cajas;

public static class CajasEndpoints
{
    private static readonly JsonSerializerOptions JsonOpt = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");
    private static readonly (string Key, string Value)[] Origins =
    [
        ("COREA", "Corea"), ("JAPON", "Japón"), ("USA", "Estados Unidos"),
        ("MEXICO", "México"), ("OTRO", "Otro")
    ];
    private static readonly (string Key, string Value)[] States =
    [
        ("EN_ESPERA", "En espera"), ("PEDIDO", "Pedido"),
        ("DIRECCION_COREA", "Dirección Corea"), ("DIRECCION_JAPON", "Dirección Japón"),
        ("DIRECCION_USA", "Dirección USA"), ("COTIZANDO_ENVIO", "Cotizando envío"),
        ("ENVIADO", "Enviado"), ("ADUANA", "Aduana"), ("EN_MEXICO", "En México"),
        ("LISTO_PARA_ENTREGAR", "Listo para entregar")
    ];

    public static IEndpointRouteBuilder MapCajasEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/admin/cajas").RequireAuthorization(new AuthorizeAttribute { Roles = "ADMIN" });
        // GET migrado a componentes Razor en entrega 008.4
        // GET migrado a componentes Razor en entrega 008.4
        g.MapPost("/guardar", Guardar).DisableAntiforgery();
        // GET migrado a componentes Razor en entrega 008.4
        g.MapPost("/{id:guid}/asignar", Asignar).DisableAntiforgery();
        g.MapPost("/{id:guid}/retirar", Retirar).DisableAntiforgery();
        g.MapPost("/{id:guid}/estado", CambiarEstado).DisableAntiforgery();
        return app;
    }

    private static async Task<IResult> Lista(HttpContext ctx, IHttpClientFactory factory, IConfiguration config, string? q = null)
    {
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) return Results.Redirect("/logout");
        using var http = Client(factory, config, jwt);
        var cajas = await Get<List<Caja>>(http, "cajas?select=id,codigo,descripcion,origen,estado,notas,creado_en&order=creado_en.desc&limit=1000", ctx.RequestAborted);
        if (cajas is null) return Fail("No fue posible consultar las cajas. Comprueba los permisos ADMIN.", 502);
        var filtro = (q ?? "").Trim();
        if (filtro.Length > 0)
            cajas = cajas.Where(c => new[] { c.Codigo, c.Descripcion, c.Origen, c.Estado }
                .Any(v => (v ?? "").Contains(filtro, StringComparison.OrdinalIgnoreCase))).ToList();
        var html = new StringBuilder("<section class='go-panel'><h1>Cajas internacionales</h1><p>Organiza tus encargos por caja de envío. Cada artículo puede pertenecer como máximo a una caja.</p>");
        html.Append("<p><a class='go-btn-secondary' href='/admin/cajas/nueva'>+ Nueva caja</a></p>")
            .Append("<form method='get' action='/admin/cajas' class='go-search'><input name='q' placeholder='Código, descripción, origen o estado' value='").Append(E(filtro))
            .Append("'><button>Buscar</button><a class='go-btn-secondary' href='/admin/cajas'>Limpiar</a></form>");
        if (cajas.Count == 0) html.Append("<div class='go-box'>No hay cajas que coincidan.</div>");
        else
        {
            html.Append("<div class='go-boxes-grid'>");
            foreach (var c in cajas)
                html.Append("<article class='go-boxes-card'><span class='go-boxes-muted'>").Append(E(OriginText(c.Origen)))
                    .Append(" · ").Append(E(StateText(c.Estado))).Append("</span><h2>").Append(E(c.Codigo))
                    .Append("</h2><p>").Append(E(c.Descripcion)).Append("</p><a href='/admin/cajas/")
                    .Append(c.Id.ToString("D")).Append("'>Ver caja y artículos</a></article>");
            html.Append("</div>");
        }
        html.Append("</section>");
        return Html(Page("Cajas", html.ToString()));
    }

    private static IResult Nueva(HttpContext ctx, IAntiforgery anti)
    {
        var csrf = anti.GetAndStoreTokens(ctx).RequestToken ?? "";
        var b = new StringBuilder("<section class='go-panel'><h1>Nueva caja</h1><div class='go-box'><form class='go-boxes-form' method='post' action='/admin/cajas/guardar'>");
        b.Append(Csrf(csrf)).Append("<label>Código único *<input name='codigo' maxlength='80' required placeholder='CAJA-07-COREA'></label>")
          .Append("<label>Descripción<input name='descripcion' maxlength='250' placeholder='Line Friends · Envío de enero'></label>")
          .Append("<label>Origen *<select name='origen' required>").Append(Options(Origins, "COREA")).Append("</select></label>")
          .Append("<label>Notas internas<textarea name='notas' maxlength='1500' rows='3'></textarea></label>")
          .Append("<div class='go-boxes-actions'><button>Crear caja</button><a href='/admin/cajas'>Cancelar</a></div></form></div></section>");
        return Html(Page("Nueva caja", b.ToString()));
    }

    private static async Task<IResult> Guardar(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti)
    {
        if (!await ValidCsrf(ctx, anti)) return Results.BadRequest("Solicitud inválida (CSRF).");
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var codigo = form["codigo"].ToString().Trim().ToUpperInvariant();
        var origen = form["origen"].ToString();
        var descripcion = TrimOrNull(form["descripcion"].ToString());
        var notas = TrimOrNull(form["notas"].ToString());
        if (codigo.Length is < 2 or > 80 || descripcion?.Length > 250 || notas?.Length > 1500 || !Origins.Any(x => x.Key == origen))
            return Fail("Revisa el código, el origen y los límites de texto.");
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) return Results.Redirect("/logout");
        using var http = Client(factory, cfg, jwt);
        using var req = new HttpRequestMessage(HttpMethod.Post, "cajas?select=id");
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        req.Content = JsonContent.Create(new { codigo, origen, descripcion, notas, estado = "EN_ESPERA" }, options: JsonOpt);
        try
        {
            using var res = await http.SendAsync(req, ctx.RequestAborted);
            if (!res.IsSuccessStatusCode) return Fail(res.StatusCode == System.Net.HttpStatusCode.Conflict ? "Ese código de caja ya existe." : "No se pudo crear la caja. Revisa permisos y datos.", 400);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ctx.RequestAborted));
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() != 1) return Fail("No se confirmó el alta de la caja.", 502);
            var id = doc.RootElement[0].GetProperty("id").GetGuid();
            return Results.Redirect("/admin/cajas/" + id.ToString("D"));
        }
        catch (HttpRequestException) { return Fail("No se pudo conectar a Supabase.", 502); }
    }

    private static async Task<IResult> Detalle(Guid id, HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti, string? aviso = null)
    {
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) return Results.Redirect("/logout");
        using var http = Client(factory, cfg, jwt);
        var caja = await Get<List<Caja>>(http, $"cajas?select=id,codigo,descripcion,origen,estado,notas,creado_en&id=eq.{id:D}&limit=1", ctx.RequestAborted);
        if (caja is null) return Fail("No se pudo consultar la caja.", 502);
        if (caja.Count == 0) return Results.NotFound();
        var asignados = await Get<List<DetalleItem>>(http, $"pedido_detalles?select=id,pedido_id,articulo_id,caja_id,cantidad,precio_unitario,estado&caja_id=eq.{id:D}&order=creado_en.desc&limit=1000", ctx.RequestAborted);
        var disponibles = await Get<List<DetalleItem>>(http, "pedido_detalles?select=id,pedido_id,articulo_id,caja_id,cantidad,precio_unitario,estado&caja_id=is.null&order=creado_en.desc&limit=1000", ctx.RequestAborted);
        var pedidos = await Get<List<Pedido>>(http, "pedidos?select=id,comprador_id&limit=1000", ctx.RequestAborted);
        var compradores = await Get<List<Comprador>>(http, "compradores?select=id,nombre&limit=1000", ctx.RequestAborted);
        var articulos = await Get<List<Articulo>>(http, "articulos?select=id,nombre,variante&limit=1000", ctx.RequestAborted);
        if (asignados is null || disponibles is null || pedidos is null || compradores is null || articulos is null)
            return Fail("No se pudo consultar los artículos para esta caja.", 502);
        var byPedido = pedidos.ToDictionary(x => x.Id, x => x.CompradorId);
        var byComprador = compradores.ToDictionary(x => x.Id, x => x.Nombre ?? "Comprador");
        var byArticulo = articulos.ToDictionary(x => x.Id, x => x.Nombre ?? "Artículo");
        string Label(DetalleItem item)
        {
            var comprador = byPedido.TryGetValue(item.PedidoId, out var buyerId) ? byComprador.GetValueOrDefault(buyerId, "Comprador") : "Comprador";
            return comprador + " · " + byArticulo.GetValueOrDefault(item.ArticuloId, "Artículo") + " (" + item.Cantidad + ")";
        }
        var csrf = anti.GetAndStoreTokens(ctx).RequestToken ?? "";
        var c = caja[0];
        var h = new StringBuilder("<section class='go-panel'><p><a href='/admin/cajas'>← Volver a cajas</a></p><h1>").Append(E(c.Codigo)).Append("</h1>");
        h.Append("<p>").Append(E(OriginText(c.Origen))).Append(" · ").Append(E(c.Descripcion)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(c.Notas)) h.Append("<p class='go-boxes-muted'>Nota: ").Append(E(c.Notas)).Append("</p>");
        if (aviso is "guardado" or "asignado" or "retirado") h.Append("<div class='go-ok'>Operación guardada correctamente.</div>");
        h.Append("<div class='go-box'><h2>Seguimiento de la caja</h2><form class='go-boxes-form' method='post' action='/admin/cajas/")
          .Append(id.ToString("D")).Append("/estado'>").Append(Csrf(csrf))
          .Append("<label>Estado <select name='estado'>").Append(Options(States, c.Estado)).Append("</select></label>")
          .Append("<label class='go-boxes-actions'><input type='checkbox' name='actualizar_articulos' value='true' style='width:auto'>También actualizar artículos de esta caja (excepto entregados, cancelados, stock o multa)</label>")
          .Append("<p class='go-boxes-note'>Si no marcas esta casilla, solo cambia el estado de la caja. Los estados de los artículos se conservan.</p>")
          .Append("<button>Actualizar estado</button></form></div>");
        h.Append("<div class='go-box'><h2>Artículos asignados (mostrando ").Append(asignados.Count).Append(")</h2>");
        if (asignados.Count == 0) h.Append("<p>Esta caja todavía no tiene artículos.</p>");
        else
        {
            h.Append("<div class='go-boxes-table-wrap'><table class='go-boxes-table'><thead><tr><th>Comprador y artículo</th><th>Cant.</th><th>Importe</th><th>Estado</th><th></th></tr></thead><tbody>");
            foreach (var d in asignados)
            {
                h.Append("<tr><td>").Append(E(Label(d))).Append("</td><td>").Append(d.Cantidad)
                 .Append("</td><td>").Append(Money(d.Cantidad * d.PrecioUnitario))
                 .Append("</td><td>").Append(E(StateText(d.Estado)))
                 .Append("</td><td>");
                if (d.Estado != "ENTREGADO_COMPLETADO")
                    h.Append("<form class='go-boxes-inline' method='post' action='/admin/cajas/").Append(id.ToString("D"))
                     .Append("/retirar'>").Append(Csrf(csrf)).Append("<input type='hidden' name='detalle_id' value='")
                     .Append(d.Id.ToString("D")).Append("'><button type='submit'>Retirar</button></form>");
                h.Append("</td></tr>");
            }
            h.Append("</tbody></table></div>");
        }
        h.Append("</div><div class='go-box'><h2>Asignar artículo pendiente</h2>");
        var candidates = disponibles.Where(d => d.Estado is not "ENTREGADO_COMPLETADO" and not "CANCELADO").ToList();
        if (candidates.Count == 0) h.Append("<p>No hay artículos sin caja disponibles.</p>");
        else
        {
            h.Append("<form class='go-boxes-form' method='post' action='/admin/cajas/").Append(id.ToString("D"))
             .Append("/asignar'>").Append(Csrf(csrf)).Append("<label>Pedido / artículo<select name='detalle_id' required>");
            foreach (var d in candidates)
                h.Append("<option value='").Append(d.Id.ToString("D")).Append("'>").Append(E(Label(d)))
                 .Append(" · ").Append(Money(d.PrecioUnitario)).Append(" c/u</option>");
            h.Append("</select></label><button>Asignar a caja</button></form>");
        }
        h.Append("<p class='go-boxes-muted'>La selección muestra hasta 1,000 renglones sin caja. Los artículos pendientes adicionales podrán requerir búsqueda o paginación en una futura versión.</p></div></section>");
        return Html(Page("Detalle de caja", h.ToString()));
    }

    private static async Task<IResult> Asignar(Guid id, HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti)
    {
        if (!await ValidCsrf(ctx, anti)) return Results.BadRequest("Solicitud inválida (CSRF).");
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        if (!Guid.TryParse(form["detalle_id"].ToString(), out var detalleId)) return Fail("Selecciona un artículo válido.");
        return await CallRpc(ctx, factory, cfg, "asignar_detalle_caja_go", new { p_detalle_id = detalleId, p_caja_id = id }, id, "asignado");
    }

    private static async Task<IResult> Retirar(Guid id, HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti)
    {
        if (!await ValidCsrf(ctx, anti)) return Results.BadRequest("Solicitud inválida (CSRF).");
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        if (!Guid.TryParse(form["detalle_id"].ToString(), out var detalleId)) return Fail("Identificador de artículo inválido.");
        return await CallRpc(ctx, factory, cfg, "retirar_detalle_caja_go", new { p_detalle_id = detalleId, p_caja_id = id }, id, "retirado");
    }

    private static async Task<IResult> CambiarEstado(Guid id, HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti)
    {
        if (!await ValidCsrf(ctx, anti)) return Results.BadRequest("Solicitud inválida (CSRF).");
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var estado = form["estado"].ToString();
        if (!States.Any(x => x.Key == estado)) return Fail("Estado inválido.");
        var sincronizar = form["actualizar_articulos"].ToString() == "true";
        return await CallRpc(ctx, factory, cfg, "actualizar_estado_caja_go", new { p_caja_id = id, p_estado = estado, p_actualizar_articulos = sincronizar }, id, "guardado");
    }

    private static async Task<IResult> CallRpc(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, string func, object data, Guid id, string aviso)
    {
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) return Results.Redirect("/logout");
        using var http = Client(factory, cfg, jwt);
        try
        {
            using var response = await http.PostAsJsonAsync("rpc/" + func, data, JsonOpt, ctx.RequestAborted);
            if (!response.IsSuccessStatusCode)
                return Fail("No se pudo completar la operación. Comprueba que ejecutaste 006_cajas.sql y que el artículo o caja cumplen las reglas. HTTP " + (int)response.StatusCode, 400);
            return Results.Redirect("/admin/cajas/" + id.ToString("D") + "?aviso=" + aviso);
        }
        catch (HttpRequestException) { return Fail("No se pudo conectar a Supabase.", 502); }
    }

    private static async Task<T?> Get<T>(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            using var res = await http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return default;
            return await res.Content.ReadFromJsonAsync<T>(JsonOpt, ct);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException) { return default; }
    }

    private static HttpClient Client(IHttpClientFactory factory, IConfiguration config, string jwt)
    {
        var root = config["Supabase:Url"]?.TrimEnd('/') ?? throw new InvalidOperationException("Falta Supabase:Url");
        var key = config["Supabase:PublishableKey"] ?? throw new InvalidOperationException("Falta Supabase:PublishableKey");
        var http = factory.CreateClient();
        http.BaseAddress = new Uri(root + "/rest/v1/");
        http.DefaultRequestHeaders.Add("apikey", key);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        http.Timeout = TimeSpan.FromSeconds(25);
        return http;
    }

    private static async Task<bool> ValidCsrf(HttpContext ctx, IAntiforgery anti)
    {
        try { await anti.ValidateRequestAsync(ctx); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }
    private static string Csrf(string token) => "<input type='hidden' name='__RequestVerificationToken' value='" + E(token) + "'>";
    private static string? TrimOrNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Options((string Key, string Value)[] values, string selected)
        => string.Join("", values.Select(x => "<option value='" + E(x.Key) + "'" + (x.Key == selected ? " selected" : "") + ">" + E(x.Value) + "</option>"));
    private static string OriginText(string? origen) => Origins.FirstOrDefault(x => x.Key == origen).Value ?? origen ?? "—";
    private static string StateText(string? estado) => States.FirstOrDefault(x => x.Key == estado).Value ?? estado ?? "—";
    private static IResult Fail(string message, int status = 400) => Html(Page("Error", "<div class='go-warning'>" + E(message) + "</div><p><a href='/admin/cajas'>Regresar a cajas</a></p>"), status);
    private static IResult Html(string html, int status = 200) => Results.Content(html, "text/html; charset=utf-8", statusCode: status);
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string Money(decimal value) => "$" + value.ToString("N2", Mx);
    private static string Page(string title, string body) => $$"""
        <!doctype html><html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{{E(title)}} · Milly K-Pop GO</title>
        <link rel="stylesheet" href="/go.css"><link rel="stylesheet" href="/compradores.css">
        <link rel="stylesheet" href="/pedidos.css"><link rel="stylesheet" href="/cajas.css"></head>
        <body><main class="go-shell"><header class="go-header"><div><small>MILLY K-POP GO · ADMIN</small><h1>{{E(title)}}</h1></div>
        <nav><a href="/admin">Inicio</a> · <a href="/admin/compradores">Compradores</a> · <a href="/admin/articulos">Artículos</a> · <a href="/admin/pedidos">Pedidos</a> · <a href="/admin/cajas">Cajas</a> · <a href="/logout">Salir</a></nav></header>{{body}}</main></body></html>
        """;

    private sealed class Caja
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("codigo")] public string? Codigo { get; set; }
        [JsonPropertyName("descripcion")] public string? Descripcion { get; set; }
        [JsonPropertyName("origen")] public string? Origen { get; set; }
        [JsonPropertyName("estado")] public string? Estado { get; set; }
        [JsonPropertyName("notas")] public string? Notas { get; set; }
    }
    private sealed class Pedido
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("comprador_id")] public Guid CompradorId { get; set; }
    }
    private sealed class Comprador
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("nombre")] public string? Nombre { get; set; }
    }
    private sealed class Articulo
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("nombre")] public string? Nombre { get; set; }
        [JsonPropertyName("variante")] public string? Variante { get; set; }
    }
    private sealed class DetalleItem
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("pedido_id")] public Guid PedidoId { get; set; }
        [JsonPropertyName("articulo_id")] public Guid ArticuloId { get; set; }
        [JsonPropertyName("caja_id")] public Guid? CajaId { get; set; }
        [JsonPropertyName("cantidad")] public int Cantidad { get; set; }
        [JsonPropertyName("precio_unitario")] public decimal PrecioUnitario { get; set; }
        [JsonPropertyName("estado")] public string? Estado { get; set; }
    }
}
