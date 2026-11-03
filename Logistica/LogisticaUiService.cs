using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;

namespace MillyKpopGo.Logistica;

// Solo lecturas: las escrituras continúan en los RPC protegidos de la Entrega 008.
public sealed class LogisticaUiService(IHttpClientFactory factory, IConfiguration config)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<LogisticaView> CargarAsync(HttpContext context, CancellationToken ct = default)
    {
        var jwt = await context.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) throw new InvalidOperationException("Sesión Supabase no disponible. Cierra sesión e inicia sesión otra vez.");
        var url = config["Supabase:Url"]?.TrimEnd('/') ?? throw new InvalidOperationException("Falta Supabase:Url");
        var key = config["Supabase:PublishableKey"] ?? throw new InvalidOperationException("Falta Supabase:PublishableKey");
        using var http = factory.CreateClient();
        http.BaseAddress = new Uri(url + "/rest/v1/");
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.Add("apikey", key);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var buyers = await Read<List<LogisticaBuyer>>(http, "compradores?select=id,nombre&limit=1000", ct);
        var orders = await Read<List<LogisticaOrder>>(http, "pedidos?select=id,comprador_id&limit=1000", ct);
        var articles = await Read<List<LogisticaArticle>>(http, "articulos?select=id,nombre&limit=1000", ct);
        var details = await Read<List<LogisticaDetail>>(http, "pedido_detalles?select=id,pedido_id,articulo_id,cantidad,precio_unitario,estado,tipo_entrega,fecha_limite,nota_publica&order=creado_en.desc&limit=1000", ct);
        var view = new LogisticaView();
        var buyerLookup = buyers.ToDictionary(b => b.Id, b => b.Nombre ?? "Comprador");
        var orderLookup = orders.ToDictionary(o => o.Id, o => o.CompradorId);
        var articleLookup = articles.ToDictionary(a => a.Id, a => a.Nombre ?? "Artículo");
        foreach (var d in details)
        {
            if (orderLookup.TryGetValue(d.PedidoId, out var buyerId))
            {
                d.CompradorId = buyerId;
                d.Comprador = buyerLookup.GetValueOrDefault(buyerId, "Comprador");
            }
            d.Articulo = articleLookup.GetValueOrDefault(d.ArticuloId, "Artículo");
        }
        view.Detalles = details;
        return view;
    }

    public async Task<List<LogisticaCargo>> CargarCargosAsync(HttpContext context, Guid detailId, CancellationToken ct = default)
    {
        var jwt = await context.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) throw new InvalidOperationException("Sesión Supabase no disponible.");
        var url = config["Supabase:Url"]?.TrimEnd('/') ?? throw new InvalidOperationException("Falta Supabase:Url");
        var key = config["Supabase:PublishableKey"] ?? throw new InvalidOperationException("Falta Supabase:PublishableKey");
        using var http = factory.CreateClient();
        http.BaseAddress = new Uri(url + "/rest/v1/");
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.Add("apikey", key);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return await Read<List<LogisticaCargo>>(http, $"cargos?select=id,tipo,monto,descripcion&pedido_detalle_id=eq.{detailId:D}&order=creado_en.desc&limit=1000", ct);
    }
    private static async Task<T> Read<T>(HttpClient http, string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Supabase rechazó la consulta ({(int)response.StatusCode}).");
        return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new InvalidOperationException("Respuesta vacía de Supabase.");
    }
}
public sealed class LogisticaView { public List<LogisticaDetail> Detalles {get;set;} = []; }
public sealed class LogisticaBuyer { public Guid Id {get;set;} public string? Nombre {get;set;} }
public sealed class LogisticaOrder { public Guid Id {get;set;} [JsonPropertyName("comprador_id")] public Guid CompradorId {get;set;} }
public sealed class LogisticaArticle { public Guid Id {get;set;} public string? Nombre {get;set;} }
public sealed class LogisticaDetail
{
    public Guid Id {get;set;}
    [JsonPropertyName("pedido_id")] public Guid PedidoId {get;set;}
    [JsonPropertyName("articulo_id")] public Guid ArticuloId {get;set;}
    public int Cantidad {get;set;}
    [JsonPropertyName("precio_unitario")] public decimal PrecioUnitario {get;set;}
    public string? Estado {get;set;}
    [JsonPropertyName("tipo_entrega")] public string? TipoEntrega {get;set;}
    [JsonPropertyName("fecha_limite")] public DateOnly? FechaLimite {get;set;}
    [JsonPropertyName("nota_publica")] public string? NotaPublica {get;set;}
    public Guid CompradorId {get;set;}
    public string Comprador {get;set;} = "Comprador";
    public string Articulo {get;set;} = "Artículo";
    public bool Finalizado => Estado is "CANCELADO" or "ENTREGADO_COMPLETADO";
}
public sealed class LogisticaCargo
{
    public Guid Id {get;set;}
    public string? Tipo {get;set;}
    public decimal Monto {get;set;}
    public string? Descripcion {get;set;}
}
