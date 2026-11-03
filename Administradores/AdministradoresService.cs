using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

namespace MillyKpopGo.Administradores;

public sealed class AdministradoresService(IHttpClientFactory factory, IConfiguration config, ILogger<AdministradoresService> logger)
{
    public async Task<(bool Ok, string Message)> CrearAsync(ClaimsPrincipal usuario, string correo, string password, CancellationToken ct)
    {
        if (usuario.Identity?.IsAuthenticated != true || !usuario.IsInRole("ADMIN"))
            return (false, "No tienes permiso para crear administradores.");

        correo = (correo ?? "").Trim().ToLowerInvariant();
        if (!System.Net.Mail.MailAddress.TryCreate(correo, out var address) ||
            !string.Equals(address.Address, correo, StringComparison.OrdinalIgnoreCase) || correo.Length > 254)
            return (false, "Ingresa un correo válido.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12 || password.Length > 128)
            return (false, "La contraseña debe tener entre 12 y 128 caracteres.");

        var url = config["Supabase:Url"]?.TrimEnd('/');
        var key = config["Supabase:ServiceRoleKey"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            return (false, "Falta configurar Supabase:Url o Supabase:ServiceRoleKey en el servidor.");

        using var auth = factory.CreateClient();
        auth.Timeout = TimeSpan.FromSeconds(30);
        auth.DefaultRequestHeaders.TryAddWithoutValidation("apikey", key);
        auth.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        Guid nuevoId;
        try
        {
            // Crear cuenta sin enviar correo: el administrador entrega la contraseña inicial personalmente.
            using var create = await auth.PostAsJsonAsync(url + "/auth/v1/admin/users",
                new { email = correo, password, email_confirm = true }, ct);
            if (!create.IsSuccessStatusCode)
            {
                if (create.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
                    return (false, "Ya existe una cuenta con ese correo o Supabase rechazó el registro.");
                return (false, $"Supabase no pudo crear el usuario (HTTP {(int)create.StatusCode}).");
            }
            using var body = JsonDocument.Parse(await create.Content.ReadAsStringAsync(ct));
            if (!body.RootElement.TryGetProperty("id", out var id) || !Guid.TryParse(id.GetString(), out nuevoId))
                return (false, "Supabase creó la cuenta pero no devolvió el ID. Comprueba Auth antes de intentar nuevamente.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(e, "Error de conexión creando administrador");
            return (false, "No fue posible conectar con Supabase.");
        }

        try
        {
            using var db = factory.CreateClient();
            db.Timeout = TimeSpan.FromSeconds(30);
            db.DefaultRequestHeaders.TryAddWithoutValidation("apikey", key);
            db.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var request = new HttpRequestMessage(HttpMethod.Post, url + "/rest/v1/perfiles");
            request.Content = JsonContent.Create(new { auth_user_id = nuevoId, rol = "ADMIN", comprador_id = (Guid?)null });
            using var inserted = await db.SendAsync(request, ct);
            if (inserted.IsSuccessStatusCode)
                return (true, "Administrador creado. Ya puede iniciar sesión desde la página de acceso.");
            logger.LogError("Perfil ADMIN no insertado para usuario {Id}, HTTP {Status}", nuevoId, (int)inserted.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Fallo al asignar perfil ADMIN a usuario {Id}", nuevoId);
        }

        // Si falla el perfil, retirar solo la cuenta recién creada para no dejarla sin vincular.
        try
        {
            using var rollback = await auth.DeleteAsync(url + $"/auth/v1/admin/users/{nuevoId:D}", CancellationToken.None);
            if (!rollback.IsSuccessStatusCode)
                logger.LogError("Eliminar manualmente usuario Auth {Id}; rollback HTTP {Status}", nuevoId, (int)rollback.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Revisar usuario Auth {Id}; no se completó rollback", nuevoId);
        }
        return (false, "No se pudo asignar el perfil ADMIN. Comprueba Supabase antes de reintentar.");
    }
}
