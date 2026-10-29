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

namespace MillyKpopGo.Logistica;

public static class LogisticaEndpoints
{
    private static readonly JsonSerializerOptions Opt = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");
    private static readonly (string Key, string Name)[] States =
    [
        ("EN_ESPERA", "En espera"), ("PEDIDO", "Pedido"), ("DIRECCION_COREA", "Dirección Corea"),
        ("DIRECCION_JAPON", "Dirección Japón"), ("DIRECCION_USA", "Dirección USA"),
        ("COTIZANDO_ENVIO", "Cotizando envío"), ("ENVIADO", "Enviado"), ("ADUANA", "Aduana"),
        ("EN_MEXICO", "En México"), ("LISTO_PARA_ENTREGAR", "Listo para entregar"),
        ("ENTREGADO_COMPLETADO", "Entregado / completado")
    ];
    private static readonly (string Key, string Name)[] Delivery =
    [
        ("ENVIO", "Envío"), ("PERSONAL", "Personal"), ("URGENTE_KA", "Urgente KA"),
        ("URGENTE_MX", "Urgente MX"), ("ENVIO_CON_ALMACEN", "Envío con almacén")
    ];
    private static readonly (string Key, string Name)[] Charges =
    [
        ("EMS", "EMS"), ("ADUANA", "Aduana"), ("CRUCE", "Cruce"),
        ("COMISION", "Comisión"), ("MULTA", "Multa"), ("ENVIO_LOCAL", "Envío local"), ("OTRO", "Otro")
    ];

    public static IEndpointRouteBuilder MapLogisticaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/logistica").RequireAuthorization(new AuthorizeAttribute { Roles = "ADMIN" });
        group.MapGet("", List);
        group.MapGet("/{id:guid}", Detail);
        group.MapPost("/{id:guid}/cargo", AddCharge).DisableAntiforgery();
        group.MapPost("/{id:guid}/entrega", UpdateDelivery).DisableAntiforgery();
        return app;
    }

    private static async Task<IResult> List(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, string? q = null)
    {
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) return Results.Redirect("/logout");
        using var http = Client(factory, cfg, jwt);
        var buyers = await Get<List<Buyer>>(http, "compradores?select=id,nombre&limit=1000", ctx.RequestAborted);
        var orders = await Get<List<Order>>(http, "pedidos?select=id,comprador_id&limit=1000", ctx.RequestAborted);
        var items = await Get<List<Item>>(http, "articulos?select=id,nombre&limit=1000", ctx.RequestAborted);
        var details = await Get<List<DetailRow>>(http, "pedido_detalles?select=id,pedido_id,articulo_id,caja_id,cantidad,estado,tipo_entrega&order=creado_en.desc&limit=1000", ctx.RequestAborted);
        if (buyers is null || orders is null || items is null || details is null) return Error("No se pudieron consultar artículos y pedidos.", 502);
        var b = buyers.ToDictionary(x => x.Id, x => x.Nombre ?? "Comprador");
        var o = orders.ToDictionary(x => x.Id, x => x.CompradorId);
        var a = items.ToDictionary(x => x.Id, x => x.Nombre ?? "Artículo");
        var html = new StringBuilder("<section class='go-panel'><h1>EMS, aduanas y entregas</h1><p>Selecciona un artículo para registrar cargos y seguimiento.</p>");
        html.Append("<form class='go-search' method='get'><input name='q' placeholder='Comprador o artículo' value='").Append(E(q)).Append("'><button>Buscar</button><a href='/admin/logistica'>Limpiar</a></form>");
        html.Append("<div class='go-log-table'><table><thead><tr><th>Comprador</th><th>Artículo</th><th>Cantidad</th><th>Estado</th><th>Entrega</th><th></th></tr></thead><tbody>");
        var count = 0;
        foreach (var d in details)
        {
            var buyer = o.TryGetValue(d.PedidoId, out var buyerId) ? b.GetValueOrDefault(buyerId, "Comprador") : "Comprador";
            var item = a.GetValueOrDefault(d.ArticuloId, "Artículo");
            if (!string.IsNullOrWhiteSpace(q) && !(buyer + " " + item).Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            count++;
            html.Append("<tr><td>").Append(E(buyer)).Append("</td><td>").Append(E(item)).Append("</td><td>").Append(d.Cantidad)
                .Append("</td><td>").Append(E(Label(States, d.Estado))).Append("</td><td>").Append(E(Label(Delivery, d.TipoEntrega)))
                .Append("</td><td><a href='/admin/logistica/").Append(d.Id.ToString("D")).Append("'>Administrar</a></td></tr>");
        }
        if (count == 0) html.Append("<tr><td colspan='6'>Sin resultados.</td></tr>");
        html.Append("</tbody></table></div></section>");
        return Html(Page("Logística", html.ToString()));
    }

    private static async Task<IResult> Detail(Guid id, HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti, string? aviso = null)
    {
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) return Results.Redirect("/logout");
        using var http = Client(factory, cfg, jwt);
        var rows = await Get<List<DetailRow>>(http, $"pedido_detalles?select=id,pedido_id,articulo_id,cantidad,precio_unitario,estado,tipo_entrega,fecha_limite,nota_publica&id=eq.{id:D}&limit=1", ctx.RequestAborted);
        if (rows is null) return Error("No se pudo cargar el artículo.", 502);
        if (rows.Count == 0) return Results.NotFound();
        var d = rows[0];
        var articles = await Get<List<Item>>(http, $"articulos?select=id,nombre&id=eq.{d.ArticuloId:D}", ctx.RequestAborted);
        var orders = await Get<List<Order>>(http, $"pedidos?select=id,comprador_id&id=eq.{d.PedidoId:D}", ctx.RequestAborted);
        if (articles is null || orders is null || orders.Count == 0) return Error("No se pudo cargar el comprador o artículo.", 502);
        var buyers = await Get<List<Buyer>>(http, $"compradores?select=id,nombre&id=eq.{orders[0].CompradorId:D}", ctx.RequestAborted);
        var cargos = await Get<List<Charge>>(http, $"cargos?select=id,tipo,monto,descripcion&pedido_detalle_id=eq.{id:D}&order=creado_en.desc&limit=1000", ctx.RequestAborted);
        if (buyers is null || cargos is null) return Error("No se pudieron consultar los cargos.", 502);
        var token = E(anti.GetAndStoreTokens(ctx).RequestToken);
        var name = articles?.FirstOrDefault()?.Nombre ?? "Artículo";
        var owner = buyers.FirstOrDefault()?.Nombre ?? "Comprador";
        var finalized = d.Estado is "CANCELADO" or "ENTREGADO_COMPLETADO";
        var h = new StringBuilder("<section class='go-panel'><p><a href='/admin/logistica'>← Volver a logística</a></p><h1>").Append(E(name)).Append("</h1><p>").Append(E(owner)).Append(" · ").Append(d.Cantidad).Append(" pieza(s)</p>");
        if (aviso == "cargo" || aviso == "entrega") h.Append("<p class='go-log-ok'>Cambios guardados correctamente.</p>");
        h.Append("<div class='go-log-stats'><div>Precio de artículos<strong>").Append(Money(d.Cantidad * d.PrecioUnitario)).Append("</strong></div><div>Otros cargos<strong>").Append(Money(cargos.Sum(x => x.Monto))).Append("</strong></div></div>");
        h.Append("<div class='go-log-box'><h2>Registrar cargo adicional</h2>");
        if (finalized) h.Append("<p>El artículo está finalizado y no admite cargos adicionales.</p>");
        else
        {
            h.Append("<form method='post' action='/admin/logistica/").Append(id.ToString("D")).Append("/cargo' class='go-log-form'><input type='hidden' name='__RequestVerificationToken' value='").Append(token).Append("'>");
            h.Append("<label>Tipo<select name='tipo' required>").Append(Options(Charges, null)).Append("</select></label>");
            h.Append("<label>Monto MXN<input name='monto' type='number' min='0.01' max='99999999.99' step='0.01' required></label>");
            h.Append("<label>Descripción<input name='descripcion' maxlength='500'></label><button>Registrar cargo</button></form>");
        }
        h.Append("<h3>Cargos registrados</h3><div class='go-log-table'><table><thead><tr><th>Concepto</th><th>Importe</th><th>Descripción</th></tr></thead><tbody>");
        foreach (var c in cargos) h.Append("<tr><td>").Append(E(Label(Charges, c.Tipo))).Append("</td><td>").Append(Money(c.Monto)).Append("</td><td>").Append(E(c.Descripcion)).Append("</td></tr>");
        if (cargos.Count == 0) h.Append("<tr><td colspan='3'>Aún no hay cargos.</td></tr>");
        h.Append("</tbody></table></div><p>Los cargos se pagan desde <a href='/admin/pagos/").Append(orders[0].CompradorId.ToString("D")).Append("'>Pagos y saldos</a>.</p></div>");
        h.Append("<div class='go-log-box'><h2>Seguimiento y entrega</h2>");
        if (finalized) h.Append("<p>El artículo está finalizado. Para ajustes posteriores se requerirá un proceso auditado.</p>");
        else
        {
            h.Append("<form method='post' action='/admin/logistica/").Append(id.ToString("D")).Append("/entrega' class='go-log-form'><input type='hidden' name='__RequestVerificationToken' value='").Append(token).Append("'>");
            h.Append("<label>Modalidad<select name='tipo_entrega'><option value=''>Sin definir</option>").Append(Options(Delivery, d.TipoEntrega)).Append("</select></label>");
            h.Append("<label>Estado<select name='estado'>").Append(Options(States, d.Estado)).Append("</select></label>");
            h.Append("<label>Fecha límite<input type='date' name='fecha_limite' value='").Append(d.FechaLimite?.ToString("yyyy-MM-dd") ?? "").Append("'></label>");
            h.Append("<label>Nota visible al cliente<textarea name='nota_publica' maxlength='800'>").Append(E(d.NotaPublica)).Append("</textarea></label>");
            h.Append("<button>Guardar seguimiento</button></form><p>Para marcar Entregado / completado, todos los conceptos de este artículo deben estar pagados.</p>");
        }
        h.Append("</div></section>");
        return Html(Page("EMS y entregas", h.ToString()));
    }

    private static async Task<IResult> AddCharge(Guid id, HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti)
    {
        if (!await CsrfOk(ctx, anti)) return Error("Token de formulario inválido.", 400);
        var f = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var tipo = f["tipo"].ToString(); var description = f["descripcion"].ToString();
        if (!Charges.Any(x => x.Key == tipo) || description.Length > 500 || !decimal.TryParse(f["monto"].ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0 || amount > 99999999.99m || decimal.Round(amount, 2) != amount)
            return Error("Revisa tipo e importe del cargo.");
        return await Rpc(ctx, factory, cfg, "registrar_cargo_go", new { p_detalle_id = id, p_tipo = tipo, p_monto = amount, p_descripcion = description }, id, "cargo");
    }
    private static async Task<IResult> UpdateDelivery(Guid id, HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti)
    {
        if (!await CsrfOk(ctx, anti)) return Error("Token de formulario inválido.", 400);
        var f = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var state = f["estado"].ToString(); var tipo = f["tipo_entrega"].ToString(); var note = f["nota_publica"].ToString(); var dateString = f["fecha_limite"].ToString();
        if (!States.Any(x => x.Key == state) || (tipo.Length > 0 && !Delivery.Any(x => x.Key == tipo)) || note.Length > 800) return Error("Estado, modalidad o nota inválidos.");
        DateOnly? date = null;
        if (dateString.Length > 0)
        {
            if (!DateOnly.TryParseExact(dateString, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return Error("Fecha límite inválida.");
            date = d;
        }
        return await Rpc(ctx, factory, cfg, "actualizar_entrega_go", new { p_detalle_id = id, p_tipo_entrega = tipo.Length == 0 ? null : tipo, p_estado = state, p_fecha_limite = date, p_nota_publica = note }, id, "entrega");
    }
    private static async Task<IResult> Rpc(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, string rpc, object payload, Guid id, string notice)
    {
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) return Results.Redirect("/logout");
        using var http = Client(factory, cfg, jwt);
        try
        {
            using var result = await http.PostAsJsonAsync("rpc/" + rpc, payload, Opt, ctx.RequestAborted);
            if (!result.IsSuccessStatusCode)
            {
                var body = await result.Content.ReadAsStringAsync(ctx.RequestAborted);
                ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Logistica").LogWarning("Supabase RPC {Rpc} rechazó solicitud HTTP {Status}: {Detail}", rpc, (int)result.StatusCode, body.Length > 400 ? body[..400] : body);
                return Error("Supabase rechazó el cambio. Revisa permisos, importes, pagos y migración 008. HTTP " + (int)result.StatusCode);
            }
            return Results.Redirect("/admin/logistica/" + id.ToString("D") + "?aviso=" + notice);
        }
        catch (OperationCanceledException ex)
             when (ctx.RequestAborted.IsCancellationRequested)
        {
            // El navegador canceló la petición.
            Console.WriteLine(
                $"Solicitud cancelada por el cliente: {ex.Message}"
            );

            return Results.StatusCode(499);
        }
        catch (OperationCanceledException ex)
        {
            // La petición HTTP agotó su tiempo de espera.
            Console.WriteLine(
                $"Timeout al consultar Supabase: {ex.Message}"
            );

            return Error(
                "Supabase tardó demasiado en responder.",
                504
            );
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine(
                $"Error HTTP de Supabase: {ex.Message}"
            );

            return Error(
                "No se pudo contactar Supabase.",
                502
            );
        }
    }
    private static HttpClient Client(IHttpClientFactory factory, IConfiguration cfg, string jwt)
    {
        var url = cfg["Supabase:Url"]?.TrimEnd('/') ?? throw new InvalidOperationException("Falta Supabase:Url");
        var key = cfg["Supabase:PublishableKey"] ?? throw new InvalidOperationException("Falta Supabase:PublishableKey");
        var http = factory.CreateClient(); http.BaseAddress = new Uri(url + "/rest/v1/");
        http.DefaultRequestHeaders.Add("apikey", key); http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        http.Timeout = TimeSpan.FromSeconds(30); return http;
    }
    private static async Task<T?> Get<T>(HttpClient http, string uri, CancellationToken ct)
    {
        try { using var r = await http.GetAsync(uri, ct); if (!r.IsSuccessStatusCode) return default; return await r.Content.ReadFromJsonAsync<T>(Opt, ct); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException) { return default; }
    }
    private static async Task<bool> CsrfOk(HttpContext ctx, IAntiforgery anti)
    { try { await anti.ValidateRequestAsync(ctx); return true; } catch (AntiforgeryValidationException) { return false; } }
    private static string Options((string Key, string Name)[] source, string? selected)
    {
        var b = new StringBuilder(); foreach (var x in source) b.Append("<option value='").Append(E(x.Key)).Append("'").Append(x.Key == selected ? " selected" : "").Append(">").Append(E(x.Name)).Append("</option>"); return b.ToString();
    }
    private static string Label((string Key, string Name)[] source, string? key) => source.FirstOrDefault(x => x.Key == key).Name ?? (key ?? "Sin definir");
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string Money(decimal v) => "$" + v.ToString("N2", Mx);
    private static IResult Html(string body, int status = 200) => Results.Content(body, "text/html; charset=utf-8", statusCode: status);
    private static IResult Error(string msg, int status = 400) => Html(Page("Error", "<section class='go-panel'><p class='go-log-error'>" + E(msg) + "</p><a href='/admin/logistica'>Volver a logística</a></section>"), status);
    private static string Page(string title, string content) => $$"""
    <!doctype html><html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{{E(title)}} · Milly K-Pop GO</title>
    <link rel="stylesheet" href="/go.css"><link rel="stylesheet" href="/logistica.css"></head><body><main class="go-shell"><header class="go-header"><div><small>MILLY K-POP GO · ADMIN</small><h1>{{E(title)}}</h1></div>
    <nav><a href="/admin">Inicio</a> · <a href="/admin/compradores">Compradores</a> · <a href="/admin/articulos">Artículos</a> · <a href="/admin/pedidos">Pedidos</a> · <a href="/admin/cajas">Cajas</a> · <a href="/admin/pagos">Pagos</a> · <a href="/admin/logistica">Logística</a> · <a href="/logout">Salir</a></nav></header>{{content}}</main></body></html>
    """;
    private sealed class Buyer { [JsonPropertyName("id")] public Guid Id { get; set; } [JsonPropertyName("nombre")] public string? Nombre { get; set; } }
    private sealed class Order { [JsonPropertyName("id")] public Guid Id { get; set; } [JsonPropertyName("comprador_id")] public Guid CompradorId { get; set; } }
    private sealed class Item { [JsonPropertyName("id")] public Guid Id { get; set; } [JsonPropertyName("nombre")] public string? Nombre { get; set; } }
    private sealed class DetailRow
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("pedido_id")] public Guid PedidoId { get; set; }
        [JsonPropertyName("articulo_id")] public Guid ArticuloId { get; set; }
        [JsonPropertyName("caja_id")] public Guid? CajaId { get; set; }
        [JsonPropertyName("cantidad")] public int Cantidad { get; set; }
        [JsonPropertyName("precio_unitario")] public decimal PrecioUnitario { get; set; }
        [JsonPropertyName("estado")] public string? Estado { get; set; }
        [JsonPropertyName("tipo_entrega")] public string? TipoEntrega { get; set; }
        [JsonPropertyName("fecha_limite")] public DateOnly? FechaLimite { get; set; }
        [JsonPropertyName("nota_publica")] public string? NotaPublica { get; set; }
    }
    private sealed class Charge
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("tipo")] public string? Tipo { get; set; }
        [JsonPropertyName("monto")] public decimal Monto { get; set; }
        [JsonPropertyName("descripcion")] public string? Descripcion { get; set; }
    }
}
