using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;

namespace MillyKpopGo.Dashboard;

/// <summary>Consultas de solo lectura. Se utiliza exclusivamente el JWT del ADMIN y RLS de Supabase.</summary>
public sealed class DashboardUiService(IHttpClientFactory factory, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<HttpClient> CreateClientAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true || !context.User.IsInRole("ADMIN"))
            throw new UnauthorizedAccessException("Acceso exclusivo de ADMIN.");
        var token = await context.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Tu sesión ha caducado. Vuelve a iniciar sesión.");
        var url = configuration["Supabase:Url"]?.TrimEnd('/') ?? throw new InvalidOperationException("Falta Supabase:Url");
        var key = configuration["Supabase:PublishableKey"] ?? throw new InvalidOperationException("Falta Supabase:PublishableKey");
        var http = factory.CreateClient();
        http.BaseAddress = new Uri(url + "/rest/v1/");
        http.Timeout = TimeSpan.FromSeconds(40);
        http.DefaultRequestHeaders.Add("apikey", key);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    // Paginación explícita para no truncar silenciosamente el resumen al llegar a 1,000 filas.
    private static async Task<List<T>> ReadAllAsync<T>(HttpClient client, string path, CancellationToken ct)
    {
        var all = new List<T>();
        const int pageSize = 500;
        for (var offset = 0; ; offset += pageSize)
        {
            var separator = path.Contains('?') ? '&' : '?';
            using var response = await client.GetAsync($"{path}{separator}limit={pageSize}&offset={offset}", ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Error al consultar {typeof(T).Name}: HTTP {(int)response.StatusCode}.");
            var batch = await response.Content.ReadFromJsonAsync<List<T>>(JsonOptions, ct) ?? [];
            all.AddRange(batch);
            if (batch.Count < pageSize) break;
            if (offset >= 99500) throw new InvalidOperationException("El volumen de datos excede el límite del Dashboard. Se requiere agregación en servidor.");
        }
        return all;
    }

    public async Task<DashboardResumen> CargarAsync(HttpContext context, CancellationToken ct = default)
    {
        using var http = await CreateClientAsync(context);
        var compradores = await ReadAllAsync<DashboardComprador>(http, "compradores?select=id,nombre,alias,activo&order=nombre.asc", ct);
        var pedidos = await ReadAllAsync<DashboardPedido>(http, "pedidos?select=id,comprador_id", ct);
        var detalles = await ReadAllAsync<DashboardDetalle>(http, "pedido_detalles?select=id,pedido_id,cantidad,precio_unitario,estado", ct);
        var cargos = await ReadAllAsync<DashboardCargo>(http, "cargos?select=id,pedido_detalle_id,monto", ct);
        var pagos = await ReadAllAsync<DashboardPago>(http, "pagos?select=id,comprador_id,monto", ct);
        var aplicaciones = await ReadAllAsync<DashboardAplicacion>(http, "aplicaciones_pago?select=id,pago_id,pedido_detalle_id,cargo_id,monto", ct);
        var cajas = await ReadAllAsync<DashboardCaja>(http, "cajas?select=id,origen,estado", ct);

        var cancelled = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CANCELADO" };
        var delivered = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ENTREGADO", "ENTREGADO_COMPLETADO" };
        var validDetails = detalles.Where(d => !cancelled.Contains(d.Estado ?? "")).ToList();
        var detallesIds = validDetails.Select(d => d.Id).ToHashSet();
        var pedidoOwner = pedidos.ToDictionary(p => p.Id, p => p.CompradorId);
        var detalleOwner = detalles.Where(d => pedidoOwner.ContainsKey(d.PedidoId)).ToDictionary(d => d.Id, d => pedidoOwner[d.PedidoId]);
        var cargoOwner = cargos.Where(c => detalleOwner.ContainsKey(c.PedidoDetalleId)).ToDictionary(c => c.Id, c => detalleOwner[c.PedidoDetalleId]);
        var pagoOwner = pagos.ToDictionary(p => p.Id, p => p.CompradorId);
        var resumen = new DashboardResumen
        {
            CompradorasActivas = compradores.Count(c => c.Activo),
            Pedidos = pedidos.Count,
            ArticulosPendientes = validDetails.Count(d => !delivered.Contains(d.Estado ?? "")),
            ArticulosEntregados = validDetails.Count(d => delivered.Contains(d.Estado ?? "")),
            CajasActivas = cajas.Count(c => !delivered.Contains(c.Estado ?? "") && !cancelled.Contains(c.Estado ?? "")),
            VentasArticulos = validDetails.Sum(d => d.Cantidad * d.PrecioUnitario),
            TotalCargos = cargos.Sum(c => c.Monto),
            PagosRecibidos = pagos.Sum(p => p.Monto),
            // Aplicaciones por conceptos para distinguir deuda pendiente y fondos disponibles.
            SaldoSinAplicar = pagos.Sum(p => p.Monto) - aplicaciones.Sum(a => a.Monto)
        };
        resumen.PendienteNeto = Math.Max(0m, resumen.VentasArticulos + resumen.TotalCargos - resumen.PagosRecibidos);
        resumen.CajasPorOrigen = cajas.GroupBy(c => string.IsNullOrWhiteSpace(c.Origen) ? "SIN ORIGEN" : c.Origen!)
            .OrderBy(g => g.Key).Select(g => new DashboardGrupo(g.Key, g.Count())).ToList();
        resumen.ArticulosPorEstado = detalles.GroupBy(d => string.IsNullOrWhiteSpace(d.Estado) ? "SIN ESTADO" : d.Estado!)
            .OrderByDescending(g => g.Count()).Select(g => new DashboardGrupo(g.Key.Replace('_', ' '), g.Count())).ToList();

        var deuda = new Dictionary<Guid, decimal>();
        foreach (var d in validDetails)
            if (detalleOwner.TryGetValue(d.Id, out var owner)) deuda[owner] = deuda.GetValueOrDefault(owner) + d.Cantidad * d.PrecioUnitario;
        foreach (var c in cargos)
            if (cargoOwner.TryGetValue(c.Id, out var owner)) deuda[owner] = deuda.GetValueOrDefault(owner) + c.Monto;
        foreach (var p in pagos)
            deuda[p.CompradorId] = deuda.GetValueOrDefault(p.CompradorId) - p.Monto;
        resumen.CompradorasConAdeudo = compradores
            .Where(c => deuda.GetValueOrDefault(c.Id) > 0.009m)
            .Select(c => new DashboardAdeudo(c.Id, c.Alias is { Length: > 0 } ? c.Alias : c.Nombre ?? "Compradora", deuda[c.Id]))
            .OrderByDescending(c => c.Pendiente).Take(10).ToList();
        return resumen;
    }
}

public sealed class DashboardResumen
{
    public int CompradorasActivas { get; set; }
    public int Pedidos { get; set; }
    public int ArticulosPendientes { get; set; }
    public int ArticulosEntregados { get; set; }
    public int CajasActivas { get; set; }
    public decimal VentasArticulos { get; set; }
    public decimal TotalCargos { get; set; }
    public decimal PagosRecibidos { get; set; }
    public decimal PendienteNeto { get; set; }
    public decimal SaldoSinAplicar { get; set; }
    public List<DashboardAdeudo> CompradorasConAdeudo { get; set; } = [];
    public List<DashboardGrupo> ArticulosPorEstado { get; set; } = [];
    public List<DashboardGrupo> CajasPorOrigen { get; set; } = [];
}
public sealed record DashboardAdeudo(Guid CompradorId, string Nombre, decimal Pendiente);
public sealed record DashboardGrupo(string Nombre, int Cantidad);
public sealed class DashboardComprador { public Guid Id { get; set; } public string? Nombre { get; set; } public string? Alias { get; set; } public bool Activo { get; set; } }
public sealed class DashboardPedido { public Guid Id { get; set; } [JsonPropertyName("comprador_id")] public Guid CompradorId { get; set; } }
public sealed class DashboardDetalle { public Guid Id { get; set; } [JsonPropertyName("pedido_id")] public Guid PedidoId { get; set; } public int Cantidad { get; set; } [JsonPropertyName("precio_unitario")] public decimal PrecioUnitario { get; set; } public string? Estado { get; set; } }
public sealed class DashboardCargo { public Guid Id { get; set; } [JsonPropertyName("pedido_detalle_id")] public Guid PedidoDetalleId { get; set; } public decimal Monto { get; set; } }
public sealed class DashboardPago { public Guid Id { get; set; } [JsonPropertyName("comprador_id")] public Guid CompradorId { get; set; } public decimal Monto { get; set; } }
public sealed class DashboardAplicacion { public Guid Id { get; set; } [JsonPropertyName("pago_id")] public Guid PagoId { get; set; } [JsonPropertyName("pedido_detalle_id")] public Guid? PedidoDetalleId { get; set; } [JsonPropertyName("cargo_id")] public Guid? CargoId { get; set; } public decimal Monto { get; set; } }
public sealed class DashboardCaja { public Guid Id { get; set; } public string? Origen { get; set; } public string? Estado { get; set; } }
