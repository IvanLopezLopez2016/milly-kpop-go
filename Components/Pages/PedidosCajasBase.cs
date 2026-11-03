using System.Globalization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MillyKpopGo.Pedidos;
namespace MillyKpopGo.Components.Pages;
public abstract class PedidosCajasBase : ComponentBase, IDisposable
{
    [Inject] protected PedidosCajasUiService Data {get;set;} = default!;
    [Inject] protected IHttpContextAccessor Context {get;set;} = default!;
    [Inject] protected PersistentComponentState State {get;set;} = default!;
    [Inject] protected ISnackbar Snackbar {get;set;} = default!;
    private PersistingComponentStateSubscription _subscription;
    protected PedidosCajasView View {get;set;} = new();
    protected string? Error {get;set;}
    protected bool Loading {get;set;} = true;
    protected string Busqueda {get;set;} = "";
    protected abstract string CacheKey {get;}
    protected abstract Task<PedidosCajasView> LoadAsync(HttpContext ctx);
    protected override async Task OnInitializedAsync()
    {
        if (State.TryTakeFromJson<PedidosCajasView>(CacheKey, out var cached) && cached is not null) View = cached;
        else if (Context.HttpContext is { } ctx)
        {
            try { View = await LoadAsync(ctx); }
            catch(Exception ex) { Error = ex.Message; }
        }
        else Error = "La sesión no se pudo recuperar. Vuelve a entrar desde el panel.";
        Loading = false;
        _subscription = State.RegisterOnPersisting(() => { State.PersistAsJson(CacheKey, View); PersistExtra(); return Task.CompletedTask; });
    }
    protected virtual void PersistExtra() {}
    public void Dispose() => _subscription.Dispose();
    protected static string Money(decimal value) => value.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));
 }
