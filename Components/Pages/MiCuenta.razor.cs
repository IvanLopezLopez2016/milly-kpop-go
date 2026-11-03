using System.Globalization;
using Microsoft.AspNetCore.Components;
using MillyKpopGo.Portal;

namespace MillyKpopGo.Components.Pages;

public partial class MiCuenta : ComponentBase, IDisposable
{
    [Inject] protected PortalClienteService Data { get; set; } = default!;
    [Inject] protected IHttpContextAccessor Context { get; set; } = default!;
    [Inject] protected PersistentComponentState State { get; set; } = default!;
    private PersistingComponentStateSubscription _subscription;
    private bool _registered;
    [SupplyParameterFromQuery(Name = "tab")]
    public string? Tab { get; set; }

    //protected string TabActual => Tab?.ToLowerInvariant() switch
    //{
    //    "cargos" => "cargos",
    //    "abonos" => "abonos",
    //    _ => "articulos"
    //};
    protected string TabActual { get; set; } = "articulos";

    protected PortalVista Vista { get; set; } = new();
    protected string? Error { get; set; }
    protected bool Loading { get; set; } = true;
    protected string Nombre => Vista.Comprador?.Alias is { Length: > 0 } alias ? alias : Vista.Comprador?.Nombre ?? "Comprador";
    protected static string Dinero(decimal value) => value.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));
    protected static string Etiqueta(string? value) => string.IsNullOrWhiteSpace(value) ? "Sin definir" : value.Replace('_', ' ');
    protected string NombreCargo(Guid detalleId) => Vista.Articulo(Vista.Detalles.FirstOrDefault(d => d.Id == detalleId)?.ArticuloId ?? Guid.Empty);

    protected override void OnParametersSet()
    {
        if (Tab is not null)
        {
            TabActual = Tab.ToLowerInvariant() switch
            {
                "cargos" => "cargos",
                "abonos" => "abonos",
                _ => "articulos"
            };
        }
    }

    protected override async Task OnInitializedAsync()
    {
        if (State.TryTakeFromJson<PortalVista>("go9-mi-cuenta", out var cached) && cached is not null)
            Vista = cached;
        else if (Context.HttpContext is { } context)
        {
            try { Vista = await Data.ObtenerAsync(context); }
            catch (Exception ex) { Error = ex.Message; }
        }
        else Error = "No se pudo recuperar la sesión. Vuelve a iniciar sesión.";
        Loading = false;
        _subscription = State.RegisterOnPersisting(() => { State.PersistAsJson("go9-mi-cuenta", Vista); return Task.CompletedTask; });
        _registered = true;
    }
    public void Dispose() { if (_registered) _subscription.Dispose(); }


    protected void CambiarTab(string tab)
    {
        TabActual = tab;
    }
}
