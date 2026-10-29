using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components;
using MillyKpopGo.Pagos;
using MudBlazor;
namespace MillyKpopGo.Components.Pages;
public partial class PagosDetalleUi : ComponentBase, IDisposable
{
    [Parameter] public Guid Id {get;set;}
    [SupplyParameterFromQuery(Name="aviso")] public string? Aviso {get;set;}
    [Inject] protected PagosUiService Data {get;set;} = default!;
    [Inject] protected IHttpContextAccessor Context {get;set;} = default!;
    [Inject] protected PersistentComponentState State {get;set;} = default!;
    [Inject] protected IAntiforgery Antiforgery {get;set;} = default!;
    [Inject] protected ISnackbar Snackbar {get;set;} = default!;
    private PersistingComponentStateSubscription _subscription;
    private bool _notificado;
    protected PagosVista Vista {get;set;} = new();
    protected string Token {get;set;} = "";
    protected string? Error {get;set;}
    protected bool Loading {get;set;} = true;
    protected string FechaHoy => DateTime.Now.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    protected IEnumerable<PagoDetalle> DetallesVisibles => Vista.Detalles.Where(x => x.Estado != "CANCELADO");
    protected string Nombre => Vista.Compradores.FirstOrDefault()?.Nombre ?? "Comprador";
    protected static string Dinero(decimal n) => n.ToString("C2",CultureInfo.GetCultureInfo("es-MX"));
    private string Key => "go85-pagos-"+Id.ToString("D");
    protected override async Task OnInitializedAsync()
    {
        if(State.TryTakeFromJson<string>(Key+"-csrf",out var cachedToken)) Token=cachedToken??"";
        else if(Context.HttpContext is { } csrfCtx) Token=Antiforgery.GetAndStoreTokens(csrfCtx).RequestToken??"";
        if(State.TryTakeFromJson<PagosVista>(Key,out var cached) && cached is not null) Vista=cached;
        else if(Context.HttpContext is { } ctx)
        {
            try { Vista=await Data.ObtenerDetalle(ctx,Id); }
            catch(Exception ex) { Error=ex.Message; }
        }
        else Error="No fue posible recuperar la sesión. Accede nuevamente desde el panel.";
        Loading=false;
        _subscription=State.RegisterOnPersisting(()=>{State.PersistAsJson(Key,Vista);State.PersistAsJson(Key+"-csrf",Token);return Task.CompletedTask;});
    }
    protected override void OnAfterRender(bool firstRender)
    {
        if(firstRender && !_notificado && (Aviso is "registrado" or "aplicado"))
        {
            _notificado=true;
            Snackbar.Add(Aviso=="registrado" ? "Abono registrado correctamente." : "Pago aplicado correctamente.",Severity.Success);
        }
    }
    public void Dispose()=>_subscription.Dispose();
}
