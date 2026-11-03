using System.Globalization;
using Microsoft.AspNetCore.Components;
using MillyKpopGo.Stock;
namespace MillyKpopGo.Components.Pages;
public partial class StockAdmin : ComponentBase, IDisposable
{
    [Inject] protected StockUiService Data {get;set;} = default!;
    [Inject] protected IHttpContextAccessor Context {get;set;} = default!;
    [Inject] protected PersistentComponentState State {get;set;} = default!;
    [SupplyParameterFromQuery(Name="resultado")] public string? Resultado {get;set;}
    protected StockVista Vista {get;set;} = new();
    protected string? Error {get;set;}
    protected bool Loading {get;set;} = true;
    private PersistingComponentStateSubscription _subscription;
    private bool _registered;
    protected static string Money(decimal n) => n.ToString("C2",CultureInfo.GetCultureInfo("es-MX"));
    protected override async Task OnInitializedAsync()
    {
        if (State.TryTakeFromJson<StockVista>("stock-admin-125",out var saved) && saved is not null) Vista=saved;
        else if (Context.HttpContext is { } ctx) { try { Vista=await Data.ObtenerAsync(ctx,true); } catch(Exception ex) { Error=ex.Message; } }
        else Error="No se pudo recuperar la sesión. Recarga la página.";
        Loading=false;
        _subscription=State.RegisterOnPersisting(()=>{State.PersistAsJson("stock-admin-125",Vista);return Task.CompletedTask;});
        _registered=true;
    }
    public void Dispose() {if(_registered)_subscription.Dispose();}
}
