using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MillyKpopGo.Auth;

/// <summary>Solo utiliza la clave publishable; nunca solicita service_role.</summary>
public sealed class ActivacionCuentaService(IHttpClientFactory factory, IConfiguration config)
{
    private HttpClient Client()
    {
        var url = config["Supabase:Url"]?.TrimEnd('/');
        var key = config["Supabase:PublishableKey"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Supabase no está configurado.");
        var http = factory.CreateClient();
        http.BaseAddress = new Uri(url + "/auth/v1/");
        http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", key);
        http.Timeout = TimeSpan.FromSeconds(25);
        return http;
    }

    public async Task<(bool Ok, string Message)> EstablecerContrasenaAsync(
        string? tokenHash, string? tipo, string? password, CancellationToken ct = default)
    {
        if (tipo is not ("invite" or "recovery") || string.IsNullOrWhiteSpace(tokenHash)
            || tokenHash.Length > 2048)
            return (false, "El enlace no es válido. Solicita uno nuevo.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12 || password.Length > 128)
            return (false, "La contraseña debe tener entre 12 y 128 caracteres.");
        try
        {
            using var http = Client();
            // El token del email se verifica SOLAMENTE al enviar la contraseña, no al abrir el enlace.
            using var verify = await http.PostAsJsonAsync("verify", new { token_hash = tokenHash, type = tipo }, ct);
            if (!verify.IsSuccessStatusCode)
                return (false, "El enlace ha expirado o ya fue utilizado. Solicita uno nuevo.");
            using var json = JsonDocument.Parse(await verify.Content.ReadAsStringAsync(ct));
            if (!json.RootElement.TryGetProperty("access_token", out var tokenNode))
                return (false, "No fue posible verificar la sesión de activación.");
            var accessToken = tokenNode.GetString();
            if (string.IsNullOrEmpty(accessToken))
                return (false, "No fue posible verificar la sesión de activación.");
            using var req = new HttpRequestMessage(HttpMethod.Put, "user");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Content = JsonContent.Create(new { password });
            using var response = await http.SendAsync(req, ct);
            if (!response.IsSuccessStatusCode)
                return (false, "No fue posible guardar la contraseña. Solicita un nuevo enlace e inténtalo de nuevo.");
            return (true, "Contraseña establecida. Ya puedes iniciar sesión.");
        }
        catch (OperationCanceledException) { return (false, "La operación tardó demasiado. Inténtalo nuevamente."); }
        catch (HttpRequestException) { return (false, "No se pudo contactar Supabase. Inténtalo nuevamente."); }
        catch (JsonException) { return (false, "Supabase devolvió una respuesta inesperada."); }
        catch (InvalidOperationException) { return (false, "La conexión de Supabase no está configurada."); }
    }

    public async Task SolicitarRecuperacionAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320 || !email.Contains('@')) return;
        try
        {
            using var http = Client();
            // La plantilla Recovery usa .SiteURL /activar-cuenta con token_hash; no usa el fragmento #access_token.
            using var response = await http.PostAsJsonAsync("recover", new { email = email.Trim() }, ct);
            // Intencionalmente no revelar si la cuenta existe ni el estado del envío.
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            // No propagar errores al visitante; respuesta genérica de privacidad.
        }
    }
}
