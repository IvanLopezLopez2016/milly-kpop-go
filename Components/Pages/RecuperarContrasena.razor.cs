using Microsoft.AspNetCore.Components;
using MillyKpopGo.Auth;

namespace MillyKpopGo.Components.Pages;

public partial class RecuperarContrasena : ComponentBase
{
    [Inject] protected ActivacionCuentaService Activacion { get; set; } = default!;
    protected string Correo { get; set; } = "";
    protected string? Error { get; set; }
    protected bool Procesando { get; set; }
    protected bool Enviado { get; set; }

    protected async Task EnviarAsync()
    {
        if (Procesando || Enviado) return;
        Error = null;
        if (Correo.Length > 320 || !Correo.Contains('@') || Correo.Length < 5)
        { Error = "Ingresa un correo electrónico válido."; return; }
        Procesando = true;
        try { await Activacion.SolicitarRecuperacionAsync(Correo); Enviado = true; }
        finally { Procesando = false; }
    }
}
