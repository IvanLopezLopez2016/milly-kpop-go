using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MillyKpopGo.Components.Pages;

public partial class Admin : ComponentBase
{
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    protected void MostrarExito() => Snackbar.Add("¡MudBlazor está funcionando correctamente!", Severity.Success);
}
