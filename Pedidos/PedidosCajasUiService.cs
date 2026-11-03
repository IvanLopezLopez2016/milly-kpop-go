using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;

namespace MillyKpopGo.Pedidos;

public sealed class PedidosCajasUiService(IHttpClientFactory factory, IConfiguration config)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private async Task<HttpClient> ClientAsync(HttpContext context)
    {
        var token = await context.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Sesión de Supabase no disponible. Inicia sesión de nuevo.");
        var url = config["Supabase:Url"]?.TrimEnd('/') ?? throw new InvalidOperationException("Falta Supabase:Url");
        var key = config["Supabase:PublishableKey"] ?? throw new InvalidOperationException("Falta Supabase:PublishableKey");
        var http = factory.CreateClient();
        http.BaseAddress = new Uri(url + "/rest/v1/");
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.Add("apikey", key);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
    private static async Task<List<T>> Read<T>(HttpClient http, string path, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Consulta Supabase rechazada ({(int)response.StatusCode}).");
        return await response.Content.ReadFromJsonAsync<List<T>>(Json, ct) ?? [];
    }
    public async Task<PedidosCajasView> ReadOrdersAsync(HttpContext context)
    {
        using var http = await ClientAsync(context);
        var data = new PedidosCajasView();
        data.Compradores = await Read<PcBuyer>(http,"compradores?select=id,nombre,alias,activo&limit=1000");
        data.Articulos = await Read<PcArticle>(http,"articulos?select=id,nombre,sku,variante,precio_referencia,activo&limit=1000");
        data.Pedidos = await Read<PcOrder>(http,"pedidos?select=id,comprador_id,fecha,nota_interna&order=fecha.desc&limit=1000");
        data.Detalles = await Read<PcDetail>(http,"pedido_detalles?select=id,pedido_id,articulo_id,caja_id,cantidad,precio_unitario,estado&limit=1000");
        return data;
    }
    public async Task<PedidosCajasView> ReadBoxesAsync(HttpContext context)
    {
        using var http = await ClientAsync(context);
        var data = new PedidosCajasView();
        data.Cajas = await Read<PcBox>(http,"cajas?select=id,codigo,descripcion,origen,estado,notas,creado_en&order=creado_en.desc&limit=1000");
        data.Pedidos = await Read<PcOrder>(http,"pedidos?select=id,comprador_id&limit=1000");
        data.Compradores = await Read<PcBuyer>(http,"compradores?select=id,nombre&limit=1000");
        data.Articulos = await Read<PcArticle>(http,"articulos?select=id,nombre,variante&limit=1000");
        data.Detalles = await Read<PcDetail>(http,"pedido_detalles?select=id,pedido_id,articulo_id,caja_id,cantidad,precio_unitario,estado&order=creado_en.desc&limit=1000");
        return data;
    }
}
public sealed class PedidosCajasView
{
    public List<PcBuyer> Compradores {get;set;} = [];
    public List<PcArticle> Articulos {get;set;} = [];
    public List<PcOrder> Pedidos {get;set;} = [];
    public List<PcDetail> Detalles {get;set;} = [];
    public List<PcBox> Cajas {get;set;} = [];
    public string Comprador(Guid id) => Compradores.FirstOrDefault(c => c.Id == id)?.Nombre ?? "Comprador";
    public string Articulo(Guid id) => Articulos.FirstOrDefault(c => c.Id == id)?.Nombre ?? "Artículo";
    public string CompradorDePedido(Guid id) => Comprador(Pedidos.FirstOrDefault(x=>x.Id == id)?.CompradorId ?? Guid.Empty);
    public string Etiqueta(PcDetail detail) => $"{CompradorDePedido(detail.PedidoId)} · {Articulo(detail.ArticuloId)} ({detail.Cantidad})";
}
public sealed class PcBuyer
{
    public Guid Id {get;set;}
    public string? Nombre {get;set;}
    public string? Alias {get;set;}
    public bool Activo {get;set;}
}
public sealed class PcArticle
{
    public Guid Id {get;set;}
    public string? Nombre {get;set;}
    public string? Sku {get;set;}
    public string? Variante {get;set;}
    [JsonPropertyName("precio_referencia")] public decimal? PrecioReferencia {get;set;}
    public bool Activo {get;set;}
}
public sealed class PcOrder
{
    public Guid Id {get;set;}
    [JsonPropertyName("comprador_id")] public Guid CompradorId {get;set;}
    public DateTimeOffset Fecha {get;set;}
    [JsonPropertyName("nota_interna")] public string? NotaInterna {get;set;}
}
public sealed class PcDetail
{
    public Guid Id {get;set;}
    [JsonPropertyName("pedido_id")] public Guid PedidoId {get;set;}
    [JsonPropertyName("articulo_id")] public Guid ArticuloId {get;set;}
    [JsonPropertyName("caja_id")] public Guid? CajaId {get;set;}
    public int Cantidad {get;set;}
    [JsonPropertyName("precio_unitario")] public decimal PrecioUnitario {get;set;}
    public string? Estado {get;set;}
}
public sealed class PcBox
{
    public Guid Id {get;set;}
    public string? Codigo {get;set;}
    public string? Descripcion {get;set;}
    public string? Origen {get;set;}
    public string? Estado {get;set;}
    public string? Notas {get;set;}
}
