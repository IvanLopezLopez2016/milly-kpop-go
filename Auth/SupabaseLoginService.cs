using System.Net.Http.Json;
using System.Text.Json;

namespace MillyKpopGo.Auth;

public sealed class SupabaseLoginService(IHttpClientFactory factory, IConfiguration configuration)
{
    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct)
    {
        var url = configuration["Supabase:Url"]?.TrimEnd('/');
        var key = configuration["Supabase:PublishableKey"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            return LoginResult.Fail("Falta configurar Supabase:Url o Supabase:PublishableKey.");

        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("apikey", key);
        HttpResponseMessage signIn;
        try
        {
            signIn = await http.PostAsJsonAsync($"{url}/auth/v1/token?grant_type=password",
                new { email, password }, ct);
        }
        catch (HttpRequestException)
        {
            return LoginResult.Fail("No fue posible conectar con Supabase.");
        }
        using (signIn)
        {
            if (!signIn.IsSuccessStatusCode)
            {
                var error = await signIn.Content.ReadAsStringAsync(ct);

                System.Diagnostics.Debug.WriteLine(
                    $"SUPABASE AUTH ERROR: HTTP {(int)signIn.StatusCode} - {error}"
                );

                return LoginResult.Fail(
                    "Supabase rechazÃ³ la autenticaciÃ³n."
                );
            }
            using var tokenJson = JsonDocument.Parse(await signIn.Content.ReadAsStringAsync(ct));
            var accessToken = tokenJson.RootElement.GetProperty("access_token").GetString();
            if (string.IsNullOrWhiteSpace(accessToken)) return LoginResult.Fail("Supabase no devolviÃ³ una sesiÃ³n vÃ¡lida.");

            using var userReq = new HttpRequestMessage(HttpMethod.Get, $"{url}/auth/v1/user");
            userReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var userResponse = await http.SendAsync(userReq, ct);
            if (!userResponse.IsSuccessStatusCode) return LoginResult.Fail("No fue posible verificar la identidad.");
            using var userJson = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync(ct));
            var userId = userJson.RootElement.GetProperty("id").GetString();
            if (!Guid.TryParse(userId, out var parsedUserId)) return LoginResult.Fail("Identidad no vÃ¡lida.");

            // REST consulta con el JWT del usuario: las polÃ­ticas RLS se aplican aquÃ­.
            using var roleReq = new HttpRequestMessage(HttpMethod.Get,
                $"{url}/rest/v1/perfiles?select=rol&auth_user_id=eq.{parsedUserId:D}&limit=1");
            roleReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var roleResponse = await http.SendAsync(roleReq, ct);
            if (!roleResponse.IsSuccessStatusCode) return LoginResult.Fail("No fue posible verificar los permisos.");
            using var rolesJson = JsonDocument.Parse(await roleResponse.Content.ReadAsStringAsync(ct));
            if (rolesJson.RootElement.GetArrayLength() != 1) return LoginResult.Fail("Tu cuenta todavÃ­a no tiene un perfil asignado.");
            var role = rolesJson.RootElement[0].GetProperty("rol").GetString();
            if (role is not ("ADMIN" or "CLIENTE")) return LoginResult.Fail("Perfil no autorizado.");
            return new LoginResult(true, string.Empty, parsedUserId.ToString("D"), email, role, accessToken);
        }
    }
}

public sealed record LoginResult(bool Success, string Message, string? UserId, string? Email, string? Role, string? AccessToken = null)
{
    public static LoginResult Fail(string message) => new(false, message, null, null, null);
}
