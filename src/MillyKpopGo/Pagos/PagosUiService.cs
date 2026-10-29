using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;

namespace MillyKpopGo.Pagos;

public sealed class PagosUiService(IHttpClientFactory factory, IConfiguration cfg)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private async Task<HttpClient> Client(HttpContext ctx)
    {
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) throw new InvalidOperationException("Sesión caducada. Vuelve a iniciar sesión.");
        var url = cfg["Supabase:Url"]?.TrimEnd('/') ?? throw new InvalidOperationException("Falta Supabase:Url");
        var key = cfg["Supabase:PublishableKey"] ?? throw new InvalidOperationException("Falta Supabase:PublishableKey");
        var client = factory.CreateClient();
        client.BaseAddress = new Uri(url + "/rest/v1/");
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("apikey",key);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",jwt);
        return client;
    }
    private static async Task<List<T>> Read<T>(HttpClient client,string path)
    {
        using var r = await client.GetAsync(path);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"No se pudo consultar Supabase: HTTP {(int)r.StatusCode}.");
        return await r.Content.ReadFromJsonAsync<List<T>>(Json) ?? [];
    }
    public async Task<PagosVista> ObtenerCompradores(HttpContext ctx)
    {
        using var client = await Client(ctx);
        return new PagosVista { Compradores = await Read<PagoComprador>(client,"compradores?select=id,nombre,alias,activo&order=nombre.asc&limit=1000") };
    }
    public async Task<PagosVista> ObtenerDetalle(HttpContext ctx,Guid compradorId)
    {
        using var client = await Client(ctx);
        var v = new PagosVista();
        v.Compradores = await Read<PagoComprador>(client,$"compradores?select=id,nombre,alias,activo&id=eq.{compradorId:D}&limit=1");
        if (v.Compradores.Count == 0) return v;
        v.Pagos = await Read<PagoAbono>(client,$"pagos?select=id,monto,fecha,metodo,referencia,nota_interna&comprador_id=eq.{compradorId:D}&order=fecha.desc&limit=1000");
        var pedidos = await Read<PagoPedido>(client,$"pedidos?select=id,comprador_id&comprador_id=eq.{compradorId:D}&limit=1000");
        v.Articulos = await Read<PagoArticulo>(client,"articulos?select=id,nombre&limit=1000");
        if (pedidos.Count > 0)
        {
            var ids = string.Join(",",pedidos.Select(p=>p.Id.ToString("D")));
            v.Detalles = await Read<PagoDetalle>(client,$"pedido_detalles?select=id,pedido_id,articulo_id,cantidad,precio_unitario,estado&pedido_id=in.({ids})&limit=1000");
        }
        if (v.Detalles.Count > 0)
        {
            var ids = string.Join(",",v.Detalles.Select(p=>p.Id.ToString("D")));
            v.Cargos = await Read<PagoCargo>(client,$"cargos?select=id,pedido_detalle_id,tipo,monto&pedido_detalle_id=in.({ids})&limit=1000");
        }
        if (v.Pagos.Count > 0)
        {
            var ids = string.Join(",",v.Pagos.Select(p=>p.Id.ToString("D")));
            v.Aplicaciones = await Read<PagoAplicacion>(client,$"aplicaciones_pago?select=id,pago_id,pedido_detalle_id,cargo_id,monto&pago_id=in.({ids})&limit=1000");
        }
        return v;
    }
}
public sealed class PagosVista
{
    public List<PagoComprador> Compradores {get;set;} = [];
    public List<PagoAbono> Pagos {get;set;} = [];
    public List<PagoArticulo> Articulos {get;set;} = [];
    public List<PagoDetalle> Detalles {get;set;} = [];
    public List<PagoCargo> Cargos {get;set;} = [];
    public List<PagoAplicacion> Aplicaciones {get;set;} = [];
    public string NombreArticulo(Guid id) => Articulos.FirstOrDefault(x=>x.Id==id)?.Nombre ?? "Artículo";
    public decimal AplicadoPago(Guid id) => Aplicaciones.Where(x=>x.PagoId==id).Sum(x=>x.Monto);
    public decimal Disponible(PagoAbono p) => p.Monto - AplicadoPago(p.Id);
    public decimal AplicadoDetalle(Guid id) => Aplicaciones.Where(x=>x.PedidoDetalleId==id).Sum(x=>x.Monto);
    public decimal AplicadoCargo(Guid id) => Aplicaciones.Where(x=>x.CargoId==id).Sum(x=>x.Monto);
    public decimal PendienteDetalle(PagoDetalle d) => d.Cantidad*d.PrecioUnitario - AplicadoDetalle(d.Id);
    public decimal PendienteCargo(PagoCargo c) => c.Monto - AplicadoCargo(c.Id);
    public decimal TotalProductos => Detalles.Where(x=>x.Estado!="CANCELADO").Sum(x=>x.Cantidad*x.PrecioUnitario);
    public decimal TotalCargos => Cargos.Sum(x=>x.Monto);
    public decimal TotalRecibido => Pagos.Sum(x=>x.Monto);
    public decimal SaldoFavor => TotalRecibido - Aplicaciones.Sum(x=>x.Monto);
    public decimal PendienteAsignar => Detalles.Where(x=>x.Estado!="CANCELADO").Sum(PendienteDetalle) + Cargos.Sum(PendienteCargo);
    public decimal PendienteNeto => Math.Max(0,PendienteAsignar-SaldoFavor);
    public List<PagoAbono> Disponibles => Pagos.Where(x=>Disponible(x)>0).ToList();
    public List<PagoDetalle> DetallesPendientes => Detalles.Where(x=>x.Estado!="CANCELADO" && PendienteDetalle(x)>0).ToList();
    public List<PagoCargo> CargosPendientes => Cargos.Where(x=>PendienteCargo(x)>0).ToList();
}
public sealed class PagoComprador { public Guid Id {get;set;} public string? Nombre {get;set;} public string? Alias {get;set;} public bool Activo {get;set;} }
public sealed class PagoAbono { public Guid Id {get;set;} public decimal Monto {get;set;} public DateTimeOffset Fecha {get;set;} public string? Metodo {get;set;} public string? Referencia {get;set;} [JsonPropertyName("nota_interna")] public string? NotaInterna {get;set;} }
public sealed class PagoPedido { public Guid Id {get;set;} [JsonPropertyName("comprador_id")] public Guid CompradorId {get;set;} }
public sealed class PagoArticulo { public Guid Id {get;set;} public string? Nombre {get;set;} }
public sealed class PagoDetalle { public Guid Id {get;set;} [JsonPropertyName("pedido_id")] public Guid PedidoId {get;set;} [JsonPropertyName("articulo_id")] public Guid ArticuloId {get;set;} public int Cantidad {get;set;} [JsonPropertyName("precio_unitario")] public decimal PrecioUnitario {get;set;} public string? Estado {get;set;} }
public sealed class PagoCargo { public Guid Id {get;set;} [JsonPropertyName("pedido_detalle_id")] public Guid PedidoDetalleId {get;set;} public string? Tipo {get;set;} public decimal Monto {get;set;} }
public sealed class PagoAplicacion { public Guid Id {get;set;} [JsonPropertyName("pago_id")] public Guid PagoId {get;set;} [JsonPropertyName("pedido_detalle_id")] public Guid? PedidoDetalleId {get;set;} [JsonPropertyName("cargo_id")] public Guid? CargoId {get;set;} public decimal Monto {get;set;} }
