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

namespace MillyKpopGo.Pagos;

public static class PagosEndpoints
{
    private static readonly JsonSerializerOptions Opt = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");
    public static IEndpointRouteBuilder MapPagosEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/admin/pagos").RequireAuthorization(new AuthorizeAttribute { Roles = "ADMIN" });
        g.MapGet("", Lista);
        g.MapGet("/{id:guid}", CompradorDetalle);
        g.MapPost("/registrar", Registrar).DisableAntiforgery();
        g.MapPost("/{id:guid}/aplicar", Aplicar).DisableAntiforgery();
        return app;
    }

    private static async Task<IResult> Lista(HttpContext ctx, IHttpClientFactory factory, IConfiguration config, string? q = null)
    {
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) return Results.Redirect("/logout");
        using var http = Client(factory, config, jwt);
        var compradores = await Get<List<Comprador>>(http, "compradores?select=id,nombre,alias,activo&order=nombre.asc&limit=1000", ctx.RequestAborted);
        if (compradores is null) return Error("No se pudieron consultar compradores.", 502);
        if (!string.IsNullOrWhiteSpace(q)) compradores = compradores.Where(x => (x.Nombre + " " + x.Alias).Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var h = new StringBuilder("<section class='go-panel'><h1>Pagos y saldos</h1><p>Registra abonos, distribuye sus importes entre artículos y conserva remanentes a cuenta.</p>");
        h.Append("<form method='get' action='/admin/pagos' class='go-search'><input name='q' placeholder='Buscar comprador' value='").Append(E(q)).Append("'><button>Buscar</button><a href='/admin/pagos'>Limpiar</a></form>");
        h.Append("<div class='go-pay-list'>");
        foreach (var c in compradores)
            h.Append("<a class='go-pay-card' href='/admin/pagos/").Append(c.Id.ToString("D")).Append("'><strong>").Append(E(c.Nombre))
             .Append("</strong><span>").Append(E(c.Alias)).Append(c.Activo ? "" : " · Inactivo").Append("</span></a>");
        h.Append("</div></section>");
        return Html(Page("Pagos", h.ToString()));
    }

    private static async Task<IResult> CompradorDetalle(Guid id, HttpContext ctx, IHttpClientFactory factory, IConfiguration config, IAntiforgery anti, string? aviso = null)
    {
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) return Results.Redirect("/logout");
        using var http = Client(factory, config, jwt);
        var buyers = await Get<List<Comprador>>(http, $"compradores?select=id,nombre,alias,activo&id=eq.{id:D}&limit=1", ctx.RequestAborted);
        if (buyers is null) return Error("No se pudo consultar el comprador.", 502);
        if (buyers.Count == 0) return Results.NotFound();
        var comprador = buyers[0];
        var pagos = await Get<List<Pago>>(http, $"pagos?select=id,monto,fecha,metodo,referencia,nota_interna&comprador_id=eq.{id:D}&order=fecha.desc&limit=1000", ctx.RequestAborted);
        var pedidos = await Get<List<Pedido>>(http, $"pedidos?select=id,comprador_id&comprador_id=eq.{id:D}&limit=1000", ctx.RequestAborted);
        var articulos = await Get<List<Articulo>>(http, "articulos?select=id,nombre&limit=1000", ctx.RequestAborted);
        if (pagos is null || pedidos is null || articulos is null) return Error("No fue posible consultar pagos o pedidos.", 502);
        var detalles = new List<Detalle>();
        if (pedidos.Count > 0)
        {
            var ids = string.Join(",", pedidos.Select(x => x.Id.ToString("D")));
            detalles = await Get<List<Detalle>>(http, $"pedido_detalles?select=id,pedido_id,articulo_id,cantidad,precio_unitario,estado&pedido_id=in.({ids})&limit=1000", ctx.RequestAborted) ?? [];
        }
        var cargos = new List<Cargo>();
        if (detalles.Count > 0)
        {
            var ids = string.Join(",", detalles.Select(x => x.Id.ToString("D")));
            cargos = await Get<List<Cargo>>(http, $"cargos?select=id,pedido_detalle_id,tipo,monto&pedido_detalle_id=in.({ids})&limit=1000", ctx.RequestAborted) ?? [];
        }
        var aplicaciones = new List<Aplicacion>();
        if (pagos.Count > 0)
        {
            var ids = string.Join(",", pagos.Select(x => x.Id.ToString("D")));
            aplicaciones = await Get<List<Aplicacion>>(http, $"aplicaciones_pago?select=id,pago_id,pedido_detalle_id,cargo_id,monto&pago_id=in.({ids})&limit=1000", ctx.RequestAborted) ?? [];
        }
        var byArticulo = articulos.ToDictionary(x => x.Id, x => x.Nombre ?? "Artículo");
        var appliedPayments = aplicaciones.GroupBy(x => x.PagoId).ToDictionary(g => g.Key, g => g.Sum(x => x.Monto));
        var appliedDetails = aplicaciones.Where(x => x.PedidoDetalleId.HasValue).GroupBy(x => x.PedidoDetalleId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => x.Monto));
        var appliedCharges = aplicaciones.Where(x => x.CargoId.HasValue).GroupBy(x => x.CargoId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => x.Monto));
        var totalPaid = pagos.Sum(x => x.Monto);
        var totalAllocated = aplicaciones.Sum(x => x.Monto);
        var totalCharge = cargos.Sum(x => x.Monto);
        var totalItems = detalles.Where(x => x.Estado != "CANCELADO").Sum(x => x.Cantidad * x.PrecioUnitario);
        var credit = totalPaid - totalAllocated;
        var openItems = detalles.Where(x => x.Estado != "CANCELADO").Sum(x => x.Cantidad * x.PrecioUnitario - appliedDetails.GetValueOrDefault(x.Id));
        var openCharges = cargos.Sum(x => x.Monto - appliedCharges.GetValueOrDefault(x.Id));
        var csrf = anti.GetAndStoreTokens(ctx).RequestToken ?? "";
        var h = new StringBuilder("<section class='go-panel'><p><a href='/admin/pagos'>← Compradores</a></p><h1>Pagos de ").Append(E(comprador.Nombre)).Append("</h1>");
        if (aviso == "registrado" || aviso == "aplicado") h.Append("<div class='go-pay-ok'>Movimiento registrado correctamente.</div>");
        h.Append("<div class='go-pay-stats'><div><span>Productos + cargos</span><strong>").Append(Money(totalItems + totalCharge))
         .Append("</strong></div><div><span>Pagos recibidos</span><strong>").Append(Money(totalPaid))
         .Append("</strong></div><div><span>Saldo a favor sin aplicar</span><strong>").Append(Money(credit))
         .Append("</strong></div><div><span>Pendiente de distribuir</span><strong>").Append(Money(openItems + openCharges)).Append("</strong></div></div>");
        h.Append("<p class='go-pay-muted'>Importante: el saldo a favor sin aplicar todavía no reduce el pendiente de cada artículo. Para hacerlo, usa la sección Aplicar pago. El pendiente neto del comprador es ")
         .Append(Money(Math.Max(0, openItems + openCharges - credit))).Append(". No se incluyen envíos ni cargos que aún no hayas registrado.</p>");
        h.Append("<div class='go-pay-box'><h2>Registrar nuevo abono</h2><form method='post' action='/admin/pagos/registrar' class='go-pay-form'>")
         .Append(Csrf(csrf)).Append("<input type='hidden' name='comprador_id' value='").Append(id.ToString("D")).Append("'>")
         .Append("<label>Monto MXN<input name='monto' type='number' step='0.01' min='0.01' required></label>")
         .Append("<label>Fecha<input name='fecha' type='date' required value='").Append(DateTime.UtcNow.ToString("yyyy-MM-dd")).Append("'></label>")
         .Append("<label>Método<select name='metodo'><option>Transferencia</option><option>Efectivo</option><option>Mercado Pago</option><option>Otro</option></select></label>")
         .Append("<label>Referencia<input name='referencia' maxlength='180'></label><label>Nota interna<input name='nota' maxlength='1000'></label>")
         .Append("<button>Registrar abono</button></form></div>");
        h.Append("<div class='go-pay-box'><h2>Aplicar abono a un artículo o cargo</h2>");
        var available = pagos.Where(x => x.Monto > appliedPayments.GetValueOrDefault(x.Id)).ToList();
        var pendingDetails = detalles.Where(x => x.Estado != "CANCELADO" && x.Cantidad * x.PrecioUnitario > appliedDetails.GetValueOrDefault(x.Id)).ToList();
        var pendingCharges = cargos.Where(x => x.Monto > appliedCharges.GetValueOrDefault(x.Id)).ToList();
        if (available.Count == 0 || (pendingDetails.Count == 0 && pendingCharges.Count == 0))
            h.Append("<p>No hay abonos con saldo disponible o conceptos pendientes para aplicar.</p>");
        else
        {
            h.Append("<form method='post' action='/admin/pagos/").Append(id.ToString("D")).Append("/aplicar' class='go-pay-form'>").Append(Csrf(csrf));
            h.Append("<label>Pago disponible<select name='pago_id' required>");
            foreach (var pago in available)
                h.Append("<option value='").Append(pago.Id.ToString("D")).Append("'>").Append(E(pago.Fecha.ToString("dd/MM/yyyy"))).Append(" · Disponible ")
                 .Append(Money(pago.Monto - appliedPayments.GetValueOrDefault(pago.Id))).Append("</option>");
            h.Append("</select></label><label>Concepto por pagar<select name='destino' required>");
            foreach (var d in pendingDetails)
                h.Append("<option value='detalle:").Append(d.Id.ToString("D")).Append("'>").Append(E(byArticulo.GetValueOrDefault(d.ArticuloId, "Artículo")))
                 .Append(" · Pendiente ").Append(Money(d.Cantidad * d.PrecioUnitario - appliedDetails.GetValueOrDefault(d.Id))).Append("</option>");
            foreach (var c in pendingCharges)
                h.Append("<option value='cargo:").Append(c.Id.ToString("D")).Append("'>").Append(E(c.Tipo)).Append(" · Pendiente ")
                 .Append(Money(c.Monto - appliedCharges.GetValueOrDefault(c.Id))).Append("</option>");
            h.Append("</select></label><label>Importe a aplicar<input name='monto' type='number' min='0.01' step='0.01' required></label><button>Aplicar pago</button></form>");
        }
        h.Append("</div><div class='go-pay-box'><h2>Historial de abonos</h2><div class='go-pay-scroll'><table><thead><tr><th>Fecha</th><th>Importe</th><th>Aplicado</th><th>A favor</th><th>Método</th></tr></thead><tbody>");
        foreach (var p in pagos)
            h.Append("<tr><td>").Append(E(p.Fecha.ToString("dd/MM/yyyy"))).Append("</td><td>").Append(Money(p.Monto))
             .Append("</td><td>").Append(Money(appliedPayments.GetValueOrDefault(p.Id))).Append("</td><td>").Append(Money(p.Monto - appliedPayments.GetValueOrDefault(p.Id)))
             .Append("</td><td>").Append(E(p.Metodo)).Append("</td></tr>");
        h.Append("</tbody></table></div></div><div class='go-pay-box'><h2>Conceptos del comprador</h2><div class='go-pay-scroll'><table><thead><tr><th>Artículo / cargo</th><th>Importe</th><th>Aplicado</th><th>Pendiente</th></tr></thead><tbody>");
        foreach (var d in detalles.Where(x => x.Estado != "CANCELADO"))
            h.Append("<tr><td>").Append(E(byArticulo.GetValueOrDefault(d.ArticuloId, "Artículo"))).Append("</td><td>").Append(Money(d.Cantidad * d.PrecioUnitario))
             .Append("</td><td>").Append(Money(appliedDetails.GetValueOrDefault(d.Id))).Append("</td><td>").Append(Money(d.Cantidad * d.PrecioUnitario - appliedDetails.GetValueOrDefault(d.Id))).Append("</td></tr>");
        foreach (var c in cargos)
            h.Append("<tr><td>Cargo ").Append(E(c.Tipo)).Append("</td><td>").Append(Money(c.Monto)).Append("</td><td>").Append(Money(appliedCharges.GetValueOrDefault(c.Id)))
             .Append("</td><td>").Append(Money(c.Monto - appliedCharges.GetValueOrDefault(c.Id))).Append("</td></tr>");
        h.Append("</tbody></table></div></div></section>");
        return Html(Page("Pagos de comprador", h.ToString()));
    }

    private static async Task<IResult> Registrar(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti)
    {
        if (!await ValidCsrf(ctx, anti)) return Results.BadRequest("Solicitud inválida (CSRF).");
        var f = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        if (!Guid.TryParse(f["comprador_id"].ToString(), out var buyer) ||
            !decimal.TryParse(f["monto"].ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0 || amount > 99999999.99m || decimal.Round(amount, 2) != amount ||
            !DateOnly.TryParseExact(f["fecha"].ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return Error("Datos inválidos. Revisa fecha e importe.");
        var timestamp = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-6));
        var method = f["metodo"].ToString(); var reference = f["referencia"].ToString(); var note = f["nota"].ToString();
        if (method.Length > 80 || reference.Length > 180 || note.Length > 1000) return Error("Texto demasiado largo.");
        return await Rpc(ctx, factory, cfg, "registrar_pago_go", new { p_comprador_id = buyer, p_monto = amount, p_fecha = timestamp, p_metodo = method, p_referencia = reference, p_nota_interna = note }, buyer, "registrado");
    }

    private static async Task<IResult> Aplicar(Guid id, HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti)
    {
        if (!await ValidCsrf(ctx, anti)) return Results.BadRequest("Solicitud inválida (CSRF).");
        var f = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        if (!Guid.TryParse(f["pago_id"].ToString(), out var pago) ||
            !decimal.TryParse(f["monto"].ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0 || amount > 99999999.99m || decimal.Round(amount, 2) != amount) return Error("Pago o monto inválidos.");
        var parts = f["destino"].ToString().Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[1], out var destino) || (parts[0] != "detalle" && parts[0] != "cargo")) return Error("Destino inválido.");
        return await Rpc(ctx, factory, cfg, "aplicar_pago_go", new { p_pago_id = pago, p_pedido_detalle_id = parts[0] == "detalle" ? (Guid?)destino : null, p_cargo_id = parts[0] == "cargo" ? (Guid?)destino : null, p_monto = amount }, id, "aplicado");
    }

    private static async Task<IResult> Rpc(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, string name, object body, Guid buyer, string notice)
    {
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) return Results.Redirect("/logout");
        using var http = Client(factory, cfg, jwt);
        try
        {
            using var res = await http.PostAsJsonAsync("rpc/" + name, body, Opt, ctx.RequestAborted);
            if (!res.IsSuccessStatusCode) return Error("No se completó el movimiento. Verifica el monto, el comprador y que ejecutaste 007_pagos.sql. HTTP " + (int)res.StatusCode);
            return Results.Redirect("/admin/pagos/" + buyer.ToString("D") + "?aviso=" + notice);
        }
        catch (HttpRequestException) { return Error("No se pudo conectar con Supabase.", 502); }
    }
    private static async Task<T?> Get<T>(HttpClient http, string uri, CancellationToken ct)
    {
        try { using var r = await http.GetAsync(uri, ct); if (!r.IsSuccessStatusCode) return default; return await r.Content.ReadFromJsonAsync<T>(Opt, ct); }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException) { return default; }
    }
    private static HttpClient Client(IHttpClientFactory factory, IConfiguration cfg, string jwt)
    {
        var url = cfg["Supabase:Url"]?.TrimEnd('/') ?? throw new InvalidOperationException("Falta Supabase:Url");
        var key = cfg["Supabase:PublishableKey"] ?? throw new InvalidOperationException("Falta Supabase:PublishableKey");
        var http = factory.CreateClient(); http.BaseAddress = new Uri(url + "/rest/v1/");
        http.DefaultRequestHeaders.Add("apikey", key);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        http.Timeout = TimeSpan.FromSeconds(30); return http;
    }
    private static async Task<bool> ValidCsrf(HttpContext ctx, IAntiforgery anti)
    {
        try { await anti.ValidateRequestAsync(ctx); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }
    private static string Csrf(string token) => "<input type='hidden' name='__RequestVerificationToken' value='" + E(token) + "'>";
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string Money(decimal value) => "$" + value.ToString("N2", Mx);
    private static IResult Error(string msg, int status = 400) => Html(Page("Error", "<div class='go-warning'>" + E(msg) + "</div><p><a href='/admin/pagos'>Volver a pagos</a></p>"), status);
    private static IResult Html(string data, int status = 200) => Results.Content(data, "text/html; charset=utf-8", statusCode: status);
    private static string Page(string title, string body) => $$"""
        <!doctype html><html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{{E(title)}} · Milly K-Pop GO</title>
        <link rel="stylesheet" href="/go.css"><link rel="stylesheet" href="/compradores.css"><link rel="stylesheet" href="/pagos.css"></head>
        <body><main class="go-shell"><header class="go-header"><div><small>MILLY K-POP GO · ADMIN</small><h1>{{E(title)}}</h1></div><nav>
        <a href="/admin">Inicio</a> · <a href="/admin/compradores">Compradores</a> · <a href="/admin/articulos">Artículos</a> · <a href="/admin/pedidos">Pedidos</a> · <a href="/admin/cajas">Cajas</a> · <a href="/admin/pagos">Pagos</a> · <a href="/logout">Salir</a></nav></header>{{body}}</main></body></html>
        """;
    private sealed class Comprador { [JsonPropertyName("id")] public Guid Id { get; set; } [JsonPropertyName("nombre")] public string? Nombre { get; set; } [JsonPropertyName("alias")] public string? Alias { get; set; } [JsonPropertyName("activo")] public bool Activo { get; set; } }
    private sealed class Pedido { [JsonPropertyName("id")] public Guid Id { get; set; } [JsonPropertyName("comprador_id")] public Guid CompradorId { get; set; } }
    private sealed class Articulo { [JsonPropertyName("id")] public Guid Id { get; set; } [JsonPropertyName("nombre")] public string? Nombre { get; set; } }
    private sealed class Detalle { [JsonPropertyName("id")] public Guid Id { get; set; } [JsonPropertyName("pedido_id")] public Guid PedidoId { get; set; } [JsonPropertyName("articulo_id")] public Guid ArticuloId { get; set; } [JsonPropertyName("cantidad")] public int Cantidad { get; set; } [JsonPropertyName("precio_unitario")] public decimal PrecioUnitario { get; set; } [JsonPropertyName("estado")] public string? Estado { get; set; } }
    private sealed class Pago { [JsonPropertyName("id")] public Guid Id { get; set; } [JsonPropertyName("monto")] public decimal Monto { get; set; } [JsonPropertyName("fecha")] public DateTimeOffset Fecha { get; set; } [JsonPropertyName("metodo")] public string? Metodo { get; set; } }
    private sealed class Cargo { [JsonPropertyName("id")] public Guid Id { get; set; } [JsonPropertyName("pedido_detalle_id")] public Guid PedidoDetalleId { get; set; } [JsonPropertyName("tipo")] public string? Tipo { get; set; } [JsonPropertyName("monto")] public decimal Monto { get; set; } }
    private sealed class Aplicacion { [JsonPropertyName("id")] public Guid Id { get; set; } [JsonPropertyName("pago_id")] public Guid PagoId { get; set; } [JsonPropertyName("pedido_detalle_id")] public Guid? PedidoDetalleId { get; set; } [JsonPropertyName("cargo_id")] public Guid? CargoId { get; set; } [JsonPropertyName("monto")] public decimal Monto { get; set; } }
}
