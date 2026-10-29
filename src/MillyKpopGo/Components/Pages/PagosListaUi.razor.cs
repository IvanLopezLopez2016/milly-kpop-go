using Microsoft.AspNetCore.Components;
using MillyKpopGo.Pagos;
namespace MillyKpopGo.Components.Pages;
public partial class PagosListaUi : ComponentBase, IDisposable
{
    [Inject] protected PagosUiService Data {get;set;} = default!;
    [Inject] protected IHttpContextAccessor Context {get;set;} = default!;
    [Inject] protected PersistentComponentState State {get;set;} = default!;
    private PersistingComponentStateSubscription _subscription;
    protected PagosVista Vista {get;set;} = new();
    protected string? Error {get;set;}
    protected string Busqueda {get;set;} = "";
    protected bool Loading {get;set;} = true;
    protected IEnumerable<PagoComprador> Filtrados => string.IsNullOrWhiteSpace(Busqueda) ? Vista.Compradores : Vista.Compradores.Where(x=>(x.Nombre+" "+x.Alias).Contains(Busqueda.Trim(),StringComparison.OrdinalIgnoreCase));
    protected override async Task OnInitializedAsync()
    {
        if(State.TryTakeFromJson<PagosVista>("go85-pagos-lista",out var cached) && cached is not null) Vista=cached;
        else if(Context.HttpContext is { } ctx)
        {
            try { Vista=await Data.ObtenerCompradores(ctx); }
            catch(Exception ex) { Error=ex.Message; }
        }
        else Error="No se pudo recuperar la sesión. Vuelve al panel e inténtalo otra vez.";
        Loading=false;
        _subscription=State.RegisterOnPersisting(()=>{ State.PersistAsJson("go85-pagos-lista",Vista); return Task.CompletedTask; });
    }
    public void Dispose()=>_subscription.Dispose();
}
