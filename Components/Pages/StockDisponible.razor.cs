using System.Globalization;
using Microsoft.AspNetCore.Components;
using MillyKpopGo.Stock;

namespace MillyKpopGo.Components.Pages;

public partial class StockDisponible : ComponentBase, IDisposable
{
    [Inject] protected StockUiService Data { get; set; } = default!;
    [Inject] protected IHttpContextAccessor Context { get; set; } = default!;
    [Inject] protected PersistentComponentState State { get; set; } = default!;
    protected StockVista Vista { get; set; } = new();
    protected string? Error { get; set; }
    protected bool Loading { get; set; } = true;
    protected int Pagina { get; set; }
    protected const int TamanoPagina = 5;
    protected int TotalPaginas => Math.Max(1, (int)Math.Ceiling(Vista.Articulos.Count / (double)TamanoPagina));
    protected IEnumerable<StockItem> ArticulosPagina => Vista.Articulos.Skip(Pagina * TamanoPagina).Take(TamanoPagina);
    protected void MoverPagina(int paso) => Pagina = Math.Clamp(Pagina + paso, 0, TotalPaginas - 1);
    protected static string Money(decimal n) => n.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));

    private PersistingComponentStateSubscription _subscription;
    private bool _registered;
    protected override async Task OnInitializedAsync()
    {
        try
        {
            if (State.TryTakeFromJson<StockVista>("stock-disponible-125", out var saved) && saved is not null)
                Vista = saved;
            else if (Context.HttpContext is { } ctx)
                Vista = await Data.ObtenerAsync(ctx, false);
            else
                Error = "No se pudo recuperar la sesión. Recarga la página o vuelve a iniciar sesión.";
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Loading = false; }
        _subscription = State.RegisterOnPersisting(() =>
        {
            State.PersistAsJson("stock-disponible-125", Vista);
            return Task.CompletedTask;
        });
        _registered = true;
    }
    public void Dispose() { if (_registered) _subscription.Dispose(); }
}
