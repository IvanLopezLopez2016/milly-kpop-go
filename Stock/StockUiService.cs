using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;

namespace MillyKpopGo.Stock;

public sealed class StockUiService(IHttpClientFactory factory, IConfiguration config)
{
    public async Task<StockVista> ObtenerAsync(HttpContext ctx, bool admin, CancellationToken cancellationToken = default)
    {
        if (!ctx.User.IsInRole(admin ? "ADMIN" : "CLIENTE"))
            throw new UnauthorizedAccessException("Tu sesión no tiene el rol necesario para consultar Stock.");

        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt))
            throw new InvalidOperationException("No se encontró el token de Supabase. Cierra sesión e inicia nuevamente.");

        var url = config["Supabase:Url"]?.TrimEnd('/')
            ?? throw new InvalidOperationException("Falta Supabase:Url.");
        var key = config["Supabase:PublishableKey"]
            ?? throw new InvalidOperationException("Falta Supabase:PublishableKey.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ctx.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(18));
        using var http = factory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(20);
        http.BaseAddress = new Uri(url + "/rest/v1/");
        http.DefaultRequestHeaders.Add("apikey", key);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        try
        {
            var vista = new StockVista();
            if (admin)
            {
                vista.Articulos = await LeerAsync<StockItem>(http,
                    "articulos_stock?select=id,nombre,cantidad_disponible,precio_unitario,origen,nota,creado_en&order=creado_en.desc&limit=1000",
                    timeout.Token);
                vista.Compradores = await LeerAsync<StockComprador>(http,
                    "compradores?select=id,nombre,activo&activo=eq.true&order=nombre.asc&limit=1000",
                    timeout.Token);
            }
            else
            {
                using var respuesta = await http.PostAsJsonAsync("rpc/listar_stock_publico_go", new { }, timeout.Token);
                await VerificarRespuestaAsync(respuesta, "listar_stock_publico_go", timeout.Token);
                vista.Articulos = await respuesta.Content.ReadFromJsonAsync<List<StockItem>>(cancellationToken: timeout.Token) ?? [];
            }
            return vista;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Supabase tardó demasiado al consultar Stock. Intenta recargar la página.");
        }
    }

    private static async Task<List<T>> LeerAsync<T>(HttpClient http, string ruta, CancellationToken ct)
    {
        using var respuesta = await http.GetAsync(ruta, ct);
        await VerificarRespuestaAsync(respuesta, ruta.Split('?')[0], ct);
        return await respuesta.Content.ReadFromJsonAsync<List<T>>(cancellationToken: ct) ?? [];
    }

    private static async Task VerificarRespuestaAsync(HttpResponseMessage respuesta, string operacion, CancellationToken ct)
    {
        if (respuesta.IsSuccessStatusCode) return;
        var detalle = await respuesta.Content.ReadAsStringAsync(ct);
        if (detalle.Length > 500) detalle = detalle[..500];
        throw new InvalidOperationException($"Supabase rechazó {operacion} (HTTP {(int)respuesta.StatusCode}). {detalle}");
    }
}

public sealed class StockVista
{
    public List<StockItem> Articulos { get; set; } = [];
    public List<StockComprador> Compradores { get; set; } = [];
}

public sealed class StockItem
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    [JsonPropertyName("cantidad_disponible")] public int CantidadDisponible { get; set; }
    [JsonPropertyName("precio_unitario")] public decimal PrecioUnitario { get; set; }
    public string? Origen { get; set; }
    public string? Nota { get; set; }
}

public sealed class StockComprador
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activo { get; set; }
}
