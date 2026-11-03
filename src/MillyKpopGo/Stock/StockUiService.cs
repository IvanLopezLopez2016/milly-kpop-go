using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;

namespace MillyKpopGo.Stock;

public sealed class StockUiService(IHttpClientFactory factory, IConfiguration config)
{
    public async Task<StockVista> ObtenerAsync(HttpContext ctx, bool admin)
    {
        if (!ctx.User.IsInRole(admin ? "ADMIN" : "CLIENTE"))
            throw new UnauthorizedAccessException("Sin permisos para consultar el Stock.");
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) throw new InvalidOperationException("Inicia sesion nuevamente.");
        var url = config["Supabase:Url"]?.TrimEnd('/') ?? throw new InvalidOperationException("Falta Supabase:Url");
        var key = config["Supabase:PublishableKey"] ?? throw new InvalidOperationException("Falta Supabase:PublishableKey");
        using var http = factory.CreateClient();
        http.BaseAddress = new Uri(url + "/rest/v1/");
        http.DefaultRequestHeaders.Add("apikey",key);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",jwt);
        var view = new StockVista();
        if (admin)
        {
            using var r = await http.GetAsync("articulos_stock?select=id,nombre,cantidad_disponible,precio_unitario,origen,nota,creado_en&order=creado_en.desc&limit=1000");
            r.EnsureSuccessStatusCode();
            view.Articulos = await r.Content.ReadFromJsonAsync<List<StockItem>>() ?? [];
            using var buyers = await http.GetAsync("compradores?select=id,nombre,activo&activo=eq.true&order=nombre.asc&limit=1000");
            buyers.EnsureSuccessStatusCode();
            view.Compradores = await buyers.Content.ReadFromJsonAsync<List<StockComprador>>() ?? [];
        }
        else
        {
            using var r = await http.PostAsJsonAsync("rpc/listar_stock_publico_go",new { });
            r.EnsureSuccessStatusCode();
            view.Articulos = await r.Content.ReadFromJsonAsync<List<StockItem>>() ?? [];
        }
        return view;
    }
}
public sealed class StockVista
{
    public List<StockItem> Articulos {get;set;} = [];
    public List<StockComprador> Compradores {get;set;} = [];
}
public sealed class StockItem
{
    public Guid Id {get;set;}
    public string Nombre {get;set;} = "";
    [JsonPropertyName("cantidad_disponible")] public int CantidadDisponible {get;set;}
    [JsonPropertyName("precio_unitario")] public decimal PrecioUnitario {get;set;}
    public string? Origen {get;set;}
    public string? Nota {get;set;}
}
public sealed class StockComprador
{
    public Guid Id {get;set;}
    public string Nombre {get;set;} = "";
    public bool Activo {get;set;}
}
