using Microsoft.AspNetCore.Components;
using MudBlazor;
using MillyKpopGo.Acceso;
using MillyKpopGo.Catalogos;

namespace MillyKpopGo.Components.Pages;

public partial class RestablecerContrasenaDialog : ComponentBase
{
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = default!;
    [Parameter, EditorRequired] public CompradorUi Comprador { get; set; } = default!;
    [Inject] private CodigosAccesoService Codigos { get; set; } = default!;

    protected string Clave { get; set; } = "";
    private bool _procesando;
    private bool _aplicada;
    private bool _exito;
    private string? _resultado;

    protected void Generar()
    {
        Clave = CodigosAccesoService.GenerarClave();
        _resultado = null;
        _aplicada = false;
    }

    protected async Task Aplicar()
    {
        if (string.IsNullOrEmpty(Clave)) return;
        _procesando = true;
        _resultado = null;
        try
        {
            var (ok, msg) = await Codigos.RestablecerContrasenaAsync(Comprador.Id, Clave, CancellationToken.None);
            _exito = ok;
            _resultado = msg;
            _aplicada = ok;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            _exito = false;
            _resultado = "No fue posible conectar con Supabase o falta configuración del servidor.";
        }
        finally
        {
            _procesando = false;
        }
    }

    protected void Cancelar() => Dialog.Close(DialogResult.Ok(_aplicada));
}
