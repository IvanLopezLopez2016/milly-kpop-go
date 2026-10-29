using Microsoft.AspNetCore.Components;
using MudBlazor;
using MillyKpopGo.Logistica;

namespace MillyKpopGo.Components.Pages;

public partial class Logistica : ComponentBase, IDisposable
{
    [Inject] private LogisticaUiService Data {get;set;} = default!;
    [Inject] private IHttpContextAccessor Http {get;set;} = default!;
    [Inject] private PersistentComponentState State {get;set;} = default!;
    private PersistingComponentStateSubscription _subscription;
    protected LogisticaView View {get;set;} = new();
    protected string Busqueda {get;set;} = "";
    protected bool Loading {get;set;} = true;
    protected string? Error {get;set;}
    protected IEnumerable<LogisticaDetail> Filtrados => string.IsNullOrWhiteSpace(Busqueda) ? View.Detalles : View.Detalles.Where(x => (x.Comprador + " " + x.Articulo).Contains(Busqueda.Trim(), StringComparison.OrdinalIgnoreCase));
    protected override async Task OnInitializedAsync()
    {
        if (State.TryTakeFromJson<LogisticaView>("mil-log-list", out var restored) && restored is not null)
        { View = restored; Loading = false; }
        else if (Http.HttpContext is { } context)
        {
            try { View = await Data.CargarAsync(context); }
            catch (Exception ex) { Error = ex.Message; }
            Loading = false;
        }
        else { Loading = false; Error = "No se pudo recuperar la sesión. Vuelve a entrar a Logística desde el panel."; }
        _subscription = State.RegisterOnPersisting(() => { State.PersistAsJson("mil-log-list", View); return Task.CompletedTask; });
    }
    protected static string EstadoTexto(string? value) => value?.Replace('_',' ') ?? "Sin definir";
    protected static string EntregaTexto(string? value) => value?.Replace('_',' ') ?? "Sin definir";
    public void Dispose() => _subscription.Dispose();
}
