using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;

namespace MillyKpopGo.Catalogos;

// Lecturas con JWT del usuario y RLS. Escrituras: endpoints POST existentes (003 y 004).
public sealed class CatalogosUiService(IHttpClientFactory factory, IConfiguration config)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public Task<List<CompradorUi>> CompradoresAsync(HttpContext ctx, CancellationToken ct = default) => ConsultarAsync<CompradorUi>(ctx, "compradores?select=id,nombre,alias,correo,whatsapp,activo,auth_user_id&order=creado_en.desc&limit=500", ct);
    public Task<List<ArticuloUi>> ArticulosAsync(HttpContext ctx, CancellationToken ct = default) => ConsultarAsync<ArticuloUi>(ctx, "articulos?select=id,nombre,sku,codigo_barras,grupo,variante,precio_referencia,activo&limit=1000", ct);
    private async Task<List<T>> ConsultarAsync<T>(HttpContext ctx, string path, CancellationToken ct)
    {
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) throw new InvalidOperationException("La sesión de Supabase no está disponible. Inicia sesión nuevamente.");
        var url = config["Supabase:Url"]?.TrimEnd('/') ?? throw new InvalidOperationException("Falta Supabase:Url");
        var key = config["Supabase:PublishableKey"] ?? throw new InvalidOperationException("Falta Supabase:PublishableKey");
        using var http = factory.CreateClient();
        http.BaseAddress = new Uri(url + "/rest/v1/");
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.Add("apikey", key);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        using var response = await http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Supabase rechazó la consulta ({(int)response.StatusCode}).");
        return await response.Content.ReadFromJsonAsync<List<T>>(Options, ct) ?? [];
    }
}
public sealed class CompradorUi
{
    public Guid Id { get; set; }
    public string? Nombre { get; set; }
    public string? Alias { get; set; }
    public string? Correo { get; set; }
    public string? Whatsapp { get; set; }
    public bool Activo { get; set; }
    [JsonPropertyName("auth_user_id")] public Guid? AuthUserId { get; set; }
}
public sealed class ArticuloUi
{
    public Guid Id { get; set; }
    public string? Nombre { get; set; }
    public string? Sku { get; set; }
    [JsonPropertyName("codigo_barras")] public string? CodigoBarras { get; set; }
    public string? Grupo { get; set; }
    public string? Variante { get; set; }
    [JsonPropertyName("precio_referencia")] public decimal? PrecioReferencia { get; set; }
    public bool Activo { get; set; }
}
