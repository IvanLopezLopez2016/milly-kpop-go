using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using MillyKpopGo.Administradores;

namespace MillyKpopGo.Components.Pages;

public partial class Administradores : ComponentBase
{
    [Inject] private AdministradoresService Service { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;

    protected string Correo { get; set; } = "";
    protected string Clave { get; set; } = "";
    protected string Confirmacion { get; set; } = "";
    protected bool Guardando { get; set; }
    protected bool Exito { get; set; }
    protected string? Mensaje { get; set; }

    protected async Task CrearAsync()
    {
        if (Guardando) return;
        Mensaje = null;
        if (Clave != Confirmacion)
        {
            Mensaje = "Las contraseñas no coinciden.";
            return;
        }
        Guardando = true;
        try
        {
            var estado = await AuthenticationStateTask;
            var resultado = await Service.CrearAsync(estado.User, Correo, Clave, CancellationToken.None);
            Exito = resultado.Ok;
            Mensaje = resultado.Message;
            Snackbar.Add(resultado.Message, resultado.Ok ? Severity.Success : Severity.Error);
            if (resultado.Ok) { Correo = ""; Clave = ""; Confirmacion = ""; }
        }
        catch (Exception)
        {
            Exito = false;
            Mensaje = "Ocurrió un problema al crear el administrador. Revisa los registros del servidor.";
        }
        finally { Guardando = false; }
    }
}
