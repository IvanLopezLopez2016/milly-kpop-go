using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MillyKpopGo.Acceso;

public sealed class CodigosAccesoService(IHttpClientFactory clients, IConfiguration config, ILogger<CodigosAccesoService> log)
{
    private static readonly JsonSerializerOptions Opt = new(JsonSerializerDefaults.Web);
    private (string Url,string PublicKey,string AdminKey) Config()
    {
        var url=config["Supabase:Url"]?.TrimEnd('/') ?? "";
        var pub=config["Supabase:PublishableKey"] ?? "";
        var admin=config["Supabase:ServiceRoleKey"] ?? "";
        if(url.Length==0 || pub.Length==0 || admin.Length==0)
            throw new InvalidOperationException("Falta configurar Supabase:Url, PublishableKey o ServiceRoleKey en el servidor.");
        return (url,pub,admin);
    }
    private HttpClient Database()
    {
        var c=Config(); var h=clients.CreateClient();h.BaseAddress=new Uri(c.Url+"/rest/v1/");h.Timeout=TimeSpan.FromSeconds(30);
        h.DefaultRequestHeaders.Add("apikey",c.AdminKey);
        h.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",c.AdminKey);
        return h;
    }
    private HttpClient AuthAdmin()
    {
        var c=Config();var h=clients.CreateClient();h.BaseAddress=new Uri(c.Url+"/auth/v1/");h.Timeout=TimeSpan.FromSeconds(30);
        h.DefaultRequestHeaders.Add("apikey",c.AdminKey);
        h.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",c.AdminKey);
        return h;
    }
    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code)))).ToLowerInvariant();
    private static string Normalize(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    public async Task<(bool Ok,string Message,string? Code)> GenerarAsync(Guid id,CancellationToken ct)
    {
        using var db=Database();
        using var get=await db.GetAsync($"compradores?select=id,correo,activo,auth_user_id&id=eq.{id:D}&limit=1",ct);
        if(!get.IsSuccessStatusCode)return(false,"No se pudo consultar el comprador.",null);
        using var json=JsonDocument.Parse(await get.Content.ReadAsStringAsync(ct));
        if(json.RootElement.GetArrayLength()!=1)return(false,"Compradora no encontrada.",null);
        var buyer=json.RootElement[0];
        if(!buyer.GetProperty("activo").GetBoolean())return(false,"La compradora está inactiva.",null);
        if(buyer.GetProperty("auth_user_id").ValueKind!=JsonValueKind.Null)return(false,"Esta compradora ya tiene una cuenta vinculada.",null);
        if(string.IsNullOrWhiteSpace(buyer.GetProperty("correo").GetString()))return(false,"Registra primero un correo para la compradora.",null);
        var hex=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var code=string.Join("-",Enumerable.Range(0,4).Select(i=>hex.Substring(i*8,8)));
        using var req=new HttpRequestMessage(HttpMethod.Post,"activaciones_comprador?on_conflict=comprador_id");
        req.Headers.TryAddWithoutValidation("Prefer","resolution=merge-duplicates,return=minimal");
        req.Content=JsonContent.Create(new { comprador_id=id,codigo_hash=Hash(code),vence_en=DateTimeOffset.UtcNow.AddHours(48),utilizado_en=(DateTimeOffset?)null,intentos=0},options:Opt);
        using var resp=await db.SendAsync(req,ct);
        if(!resp.IsSuccessStatusCode)return(false,"No se pudo generar el código. ¿Ya ejecutaste 009_3_codigos.sql?",null);
        return(true,"Código generado. Se muestra una sola vez. Compártelo de forma privada por WhatsApp.",code);
    }
    public async Task<(bool Ok,string Message)> RegistrarAsync(string email,string code,string password,CancellationToken ct)
    {
        var generic="No se pudo activar la cuenta. Verifica correo y código, o solicita uno nuevo a la administradora.";
        if(string.IsNullOrWhiteSpace(email)||email.Length>254||string.IsNullOrWhiteSpace(code)||password.Length<12||password.Length>128)
            return(false,"Ingresa un correo y código válidos; la contraseña debe tener entre 12 y 128 caracteres.");
        var hash=Hash(code);
        Guid buyerId;
        using (var db=Database())
        {
            using var r=await db.PostAsJsonAsync("rpc/go_validar_activacion",new { p_correo=email.Trim(),p_hash=hash },Opt,ct);
            if(!r.IsSuccessStatusCode)return(false,"No se pudo verificar el código. Comprueba la migración SQL.");
            using var doc=JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
            if(doc.RootElement.ValueKind!=JsonValueKind.Array||doc.RootElement.GetArrayLength()!=1)return(false,generic);
            buyerId=doc.RootElement[0].GetProperty("comprador_id").GetGuid();
            email=doc.RootElement[0].GetProperty("correo").GetString()!;
        }
        Guid newUser;
        using (var auth=AuthAdmin())
        {
            using var resp=await auth.PostAsJsonAsync("admin/users",new { email,password,email_confirm=true },Opt,ct);
            if(!resp.IsSuccessStatusCode)
            {
                // No cambiar una cuenta ya existente: podría pertenecer a otra persona.
                return(false,"No fue posible crear la cuenta. Si este correo ya existe en Authentication, contacta a la administradora.");
            }
            using var doc=JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            newUser=doc.RootElement.GetProperty("id").GetGuid();
        }
        try
        {
            using var db=Database();
            using var resp=await db.PostAsJsonAsync("rpc/go_finalizar_activacion",new {p_comprador_id=buyerId,p_auth_user_id=newUser,p_hash=hash},Opt,ct);
            if(resp.IsSuccessStatusCode && (await resp.Content.ReadAsStringAsync(ct)).Trim().Equals("true",StringComparison.OrdinalIgnoreCase))
                return(true,"Cuenta activada. Ya puedes iniciar sesión.");
        }
        catch(Exception e) when(e is HttpRequestException or TaskCanceledException)
        { log.LogWarning(e,"Finalización de activación incompleta para comprador {Id}",buyerId); }
        // Limpieza compensatoria: solo el usuario que acabamos de crear, nunca uno anterior.
        try
        {
            using var auth=AuthAdmin();using var deleted=await auth.DeleteAsync($"admin/users/{newUser:D}",CancellationToken.None);
            if(!deleted.IsSuccessStatusCode) log.LogError("Revisar manualmente usuario Auth {Id}: rollback fallido",newUser);
        }
        catch(Exception e){log.LogError(e,"Revisar manualmente usuario Auth {Id}: rollback fallido",newUser);}
        return(false,"La activación no se completó. Contacta a la administradora antes de intentar otra vez.");
    }
}
