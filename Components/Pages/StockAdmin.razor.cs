using System.Globalization;
using Microsoft.AspNetCore.Components;
using MillyKpopGo.Stock;

namespace MillyKpopGo.Components.Pages;

public partial class StockAdmin : ComponentBase, IDisposable
{
    [Inject] protected StockUiService Data { get; set; } = default!;
    [Inject] protected IHttpContextAccessor Context { get; set; } = default!;
    [Inject] protected PersistentComponentState State { get; set; } = default!;
    [SupplyParameterFromQuery(Name = "resultado")] public string? Resultado { get; set; }
    protected StockVista Vista { get; set; } = new();
    protected string? Error { get; set; }
    protected bool Loading { get; set; } = true;
    protected StockItem? Seleccionado { get; set; }
    protected string? Dialogo { get; set; }
    protected int Pagina { get; set; }
    protected const int TamanoPagina = 6;
    protected IEnumerable<StockItem> Disponibles => Vista.Articulos.Where(x => x.CantidadDisponible > 0);
    protected int TotalPaginas => Math.Max(1, (int)Math.Ceiling(Disponibles.Count() / (double)TamanoPagina));
    protected IEnumerable<StockItem> ArticulosPagina => Disponibles.Skip(Pagina * TamanoPagina).Take(TamanoPagina);
    protected void MoverPagina(int paso) => Pagina = Math.Clamp(Pagina + paso, 0, TotalPaginas - 1);
    protected void AbrirAsignar(StockItem x) { Seleccionado = x; Dialogo = "asignar"; }
    protected void AbrirEliminar(StockItem x) { Seleccionado = x; Dialogo = "eliminar"; }
    protected void CerrarDialogo() { Seleccionado = null; Dialogo = null; }
    protected static string Money(decimal n) => n.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));

    private PersistingComponentStateSubscription _subscription;
    private bool _registered;
    protected override async Task OnInitializedAsync()
    {
        try
        {
            if (State.TryTakeFromJson<StockVista>("stock-admin-125", out var saved) && saved is not null)
                Vista = saved;
            else if (Context.HttpContext is { } ctx)
                Vista = await Data.ObtenerAsync(ctx, true);
            else
                Error = "No se pudo recuperar la sesión. Recarga la página o vuelve a iniciar sesión.";
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Loading = false; }
        _subscription = State.RegisterOnPersisting(() =>
        {
            State.PersistAsJson("stock-admin-125", Vista);
            return Task.CompletedTask;
        });
        _registered = true;
    }
    public void Dispose() { if (_registered) _subscription.Dispose(); }
}
