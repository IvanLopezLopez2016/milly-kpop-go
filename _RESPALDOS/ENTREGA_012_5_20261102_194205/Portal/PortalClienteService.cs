using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;

namespace MillyKpopGo.Portal;

// Todas las lecturas utilizan el JWT de la persona autenticada: Supabase aplica RLS.
// Nunca usar service_role ni la clave secreta en este servicio.
public sealed class PortalClienteService(IHttpClientFactory factory, IConfiguration config)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PortalVista> ObtenerAsync(HttpContext context, CancellationToken ct = default)
    {
        if (!context.User.IsInRole("CLIENTE")) throw new UnauthorizedAccessException("Acceso exclusivo del comprador.");
        var identity = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(identity, out var authId)) throw new InvalidOperationException("No se encontró la identidad del usuario.");
        var jwt = await context.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) throw new InvalidOperationException("No está disponible la sesión de Supabase. Cierra sesión y vuelve a entrar.");
        var url = config["Supabase:Url"]?.TrimEnd('/') ?? throw new InvalidOperationException("Falta Supabase:Url.");
        var key = config["Supabase:PublishableKey"] ?? throw new InvalidOperationException("Falta Supabase:PublishableKey.");
        using var http = factory.CreateClient();
        http.BaseAddress = new Uri(url + "/rest/v1/");
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.Add("apikey", key);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        // Comprobar ambos vínculos, no confiar en un comprador_id enviado desde el navegador.
        var perfil = await Read<PortalPerfil>(http, $"perfiles?select=auth_user_id,comprador_id,rol&auth_user_id=eq.{authId:D}&limit=1", ct);
        if (perfil.Count != 1 || perfil[0].Rol != "CLIENTE" || perfil[0].CompradorId is null)
            throw new InvalidOperationException("Tu cuenta todavía no está vinculada a un comprador. Contacta a la administración.");
        var compradorId = perfil[0].CompradorId.Value;
        var compradores = await Read<PortalComprador>(http, $"compradores?select=id,auth_user_id,nombre,alias&auth_user_id=eq.{authId:D}&id=eq.{compradorId:D}&limit=1", ct);
        if (compradores.Count != 1) throw new InvalidOperationException("Tu cuenta no está vinculada correctamente. Contacta a la administración.");
        var vista = new PortalVista { Comprador = compradores[0] };
        vista.Pedidos = await Read<PortalPedido>(http, $"pedidos?select=id,fecha&comprador_id=eq.{compradorId:D}&order=fecha.desc&limit=1000", ct);
        vista.Pagos = await Read<PortalPago>(http, $"pagos?select=id,monto,fecha,metodo,referencia&comprador_id=eq.{compradorId:D}&order=fecha.desc&limit=1000", ct);
        if (vista.Pedidos.Count > 0)
        {
            var ids = string.Join(",", vista.Pedidos.Select(p => p.Id.ToString("D")));
            vista.Detalles = await Read<PortalDetalle>(http, $"pedido_detalles?select=id,pedido_id,articulo_id,cantidad,precio_unitario,estado,tipo_entrega,nota_publica,caja_id,creado_en&pedido_id=in.({ids})&order=creado_en.desc&limit=1000", ct);
        }
        if (vista.Detalles.Count > 0)
        {
            var ids = string.Join(",", vista.Detalles.Select(d => d.ArticuloId.ToString("D")).Distinct());
            vista.Articulos = await Read<PortalArticulo>(http, $"articulos?select=id,nombre,grupo,variante&id=in.({ids})&limit=1000", ct);
            var detailIds = string.Join(",", vista.Detalles.Select(d => d.Id.ToString("D")));
            vista.Cargos = await Read<PortalCargo>(http, $"cargos?select=id,pedido_detalle_id,tipo,monto,descripcion&pedido_detalle_id=in.({detailIds})&limit=1000", ct);
        }
        if (vista.Pagos.Count > 0)
        {
            var ids = string.Join(",", vista.Pagos.Select(p => p.Id.ToString("D")));
            vista.Aplicaciones = await Read<PortalAplicacion>(http, $"aplicaciones_pago?select=id,pago_id,pedido_detalle_id,cargo_id,monto&pago_id=in.({ids})&limit=1000", ct);
        }
        var cajasIds = vista.Detalles
        .Where(d => d.CajaId.HasValue)
        .Select(d => d.CajaId!.Value.ToString("D"))
        .Distinct()
        .ToList();

            if (cajasIds.Count > 0)
            {
                var ids = string.Join(",", cajasIds);

                vista.Cajas = await Read<PortalCaja>(
                    http,
                    $"cajas?select=id,codigo&id=in.({ids})",
                    ct
                );
            }
        return vista;
    }

    private static async Task<List<T>> Read<T>(HttpClient http, string path, CancellationToken ct)
    {
        using var result = await http.GetAsync(path, ct);
        if (!result.IsSuccessStatusCode) throw new InvalidOperationException($"Supabase rechazó la consulta (HTTP {(int)result.StatusCode}).");
        return await result.Content.ReadFromJsonAsync<List<T>>(Json, ct) ?? [];
    }
}

public sealed class PortalPerfil
{
    [JsonPropertyName("auth_user_id")] public Guid AuthUserId { get; set; }
    [JsonPropertyName("comprador_id")] public Guid? CompradorId { get; set; }
    public string? Rol { get; set; }
}
public sealed class PortalComprador
{
    public Guid Id { get; set; }
    [JsonPropertyName("auth_user_id")] public Guid? AuthUserId { get; set; }
    public string Nombre { get; set; } = "";
    public string? Alias { get; set; }
}
public sealed class PortalPedido
{
    public Guid Id { get; set; }
    public DateTimeOffset Fecha { get; set; }
}
public sealed class PortalDetalle
{
    public Guid Id { get; set; }
    [JsonPropertyName("pedido_id")] public Guid PedidoId { get; set; }
    [JsonPropertyName("articulo_id")] public Guid ArticuloId { get; set; }
    [JsonPropertyName("caja_id")] public Guid? CajaId { get; set; }
    public int Cantidad { get; set; }
    [JsonPropertyName("precio_unitario")] public decimal PrecioUnitario { get; set; }
    public string? Estado { get; set; }
    [JsonPropertyName("tipo_entrega")] public string? TipoEntrega { get; set; }
    [JsonPropertyName("nota_publica")] public string? NotaPublica { get; set; }
    [JsonPropertyName("creado_en")] public DateTimeOffset CreadoEn { get; set; }
}
public sealed class PortalArticulo
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    public string? Grupo { get; set; }
    public string? Variante { get; set; }
}
public sealed class PortalCargo
{
    public Guid Id { get; set; }
    [JsonPropertyName("pedido_detalle_id")] public Guid PedidoDetalleId { get; set; }
    public string? Tipo { get; set; }
    public decimal Monto { get; set; }
    public string? Descripcion { get; set; }
}
public sealed class PortalPago
{
    public Guid Id { get; set; }
    public decimal Monto { get; set; }
    public DateTimeOffset Fecha { get; set; }
    public string? Metodo { get; set; }
    public string? Referencia { get; set; }
}
public sealed class PortalAplicacion
{
    public Guid Id { get; set; }
    [JsonPropertyName("pago_id")] public Guid PagoId { get; set; }
    [JsonPropertyName("pedido_detalle_id")] public Guid? PedidoDetalleId { get; set; }
    [JsonPropertyName("cargo_id")] public Guid? CargoId { get; set; }
    public decimal Monto { get; set; }
}
public sealed class PortalVista
{
    public List<PortalCaja> Cajas { get; set; } = [];
    public string NombreCaja(Guid? cajaId)
    {
        if (cajaId is null)
            return "Sin asignar";

        return Cajas.FirstOrDefault(c => c.Id == cajaId.Value)?.Codigo
            ?? "Sin asignar";
    }
    public PortalComprador? Comprador { get; set; }
    public List<PortalPedido> Pedidos { get; set; } = [];
    public List<PortalDetalle> Detalles { get; set; } = [];
    public List<PortalArticulo> Articulos { get; set; } = [];
    public List<PortalCargo> Cargos { get; set; } = [];
    public List<PortalPago> Pagos { get; set; } = [];
    public List<PortalAplicacion> Aplicaciones { get; set; } = [];
    public string Articulo(Guid id) => Articulos.FirstOrDefault(a => a.Id == id)?.Nombre ?? "Artículo";
    public decimal PagadoDetalle(Guid id) => Aplicaciones.Where(a => a.PedidoDetalleId == id).Sum(a => a.Monto);
    public decimal PagadoCargo(Guid id) => Aplicaciones.Where(a => a.CargoId == id).Sum(a => a.Monto);
    public decimal DisponiblePago(Guid id) => Pagos.FirstOrDefault(p => p.Id == id)?.Monto - Aplicaciones.Where(a => a.PagoId == id).Sum(a => a.Monto) ?? 0;
    public decimal TotalArticulos => Detalles.Where(d => d.Estado != "CANCELADO").Sum(d => d.Cantidad * d.PrecioUnitario);
    public decimal TotalCargos => Cargos.Sum(c => c.Monto);
    public decimal TotalPagos => Pagos.Sum(p => p.Monto);
    public decimal SaldoAFavor => TotalPagos - Aplicaciones.Sum(a => a.Monto);
    public decimal PendientePorAplicar => Detalles.Where(d => d.Estado != "CANCELADO").Sum(d => Math.Max(0, d.Cantidad * d.PrecioUnitario - PagadoDetalle(d.Id)))
        + Cargos.Sum(c => Math.Max(0, c.Monto - PagadoCargo(c.Id)));
    public decimal PendienteNeto => Math.Max(0, PendientePorAplicar - SaldoAFavor);
}

public sealed class PortalCaja
{
    public Guid Id { get; set; }
    public string Codigo { get; set; } = "";
}
