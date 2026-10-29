using Microsoft.AspNetCore.Components;
using MillyKpopGo.Auth;

namespace MillyKpopGo.Components.Pages;

public partial class ActivarCuenta : ComponentBase
{
    [Inject] protected ActivacionCuentaService Activacion { get; set; } = default!;
    [SupplyParameterFromQuery(Name = "token_hash")] public string? TokenHash { get; set; }
    [SupplyParameterFromQuery(Name = "type")] public string? Tipo { get; set; }
    protected string Contrasena { get; set; } = "";
    protected string Confirmacion { get; set; } = "";
    protected string? Error { get; set; }
    protected bool Procesando { get; set; }
    protected bool Finalizado { get; set; }
    protected bool EnlaceValido => Tipo is "invite" or "recovery" &&
                                   !string.IsNullOrWhiteSpace(TokenHash) && TokenHash.Length <= 2048;

    protected async Task GuardarAsync()
    {
        if (Procesando || Finalizado) return;
        Error = null;
        if (Contrasena.Length < 12 || Contrasena.Length > 128)
        { Error = "La contraseña debe tener entre 12 y 128 caracteres."; return; }
        if (!string.Equals(Contrasena, Confirmacion, StringComparison.Ordinal))
        { Error = "Las contraseñas no coinciden."; return; }
        Procesando = true;
        try
        {
            var result = await Activacion.EstablecerContrasenaAsync(TokenHash, Tipo, Contrasena);
            Finalizado = result.Ok;
            if (!result.Ok) Error = result.Message;
            Contrasena = "";
            Confirmacion = "";
        }
        finally { Procesando = false; }
    }
}
