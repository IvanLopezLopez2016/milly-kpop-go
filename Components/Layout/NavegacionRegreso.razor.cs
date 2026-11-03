using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace MillyKpopGo.Components.Layout;

public partial class NavegacionRegreso : ComponentBase, IDisposable
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IHttpContextAccessor HttpContextAccessor { get; set; } = default!;

    protected bool Mostrar { get; private set; }
    protected string VolverUrl { get; private set; } = "/admin";
    protected string VolverTexto { get; private set; } = "Volver al menú";

    protected override void OnInitialized()
    {
        Navigation.LocationChanged += OnLocationChanged;
        Actualizar();
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        _ = InvokeAsync(() => { Actualizar(); StateHasChanged(); });
    }

    private void Actualizar()
    {
        var uri = new Uri(Navigation.Uri);
        var ruta = uri.AbsolutePath.TrimEnd('/');
        Mostrar = ruta.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase);
        if (!Mostrar) return;

        // Solo rutas internas fijas; nunca aceptamos una URL arbitraria como destino.
        var segmento = ruta.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var modulo = segmento.Length > 1 ? segmento[1].ToLowerInvariant() : "";
        if (modulo == "dashboard")
        {
            VolverUrl = "/admin";
            VolverTexto = "Volver al menú";
            return;
        }

        // Si hay una subpágina, siempre se puede regresar a su listado.
        if (segmento.Length > 2 && modulo is "compradores" or "articulos" or "pedidos" or "cajas" or "pagos" or "logistica")
        {
            VolverUrl = "/admin/" + modulo;
            VolverTexto = "Volver a " + (modulo switch
            {
                "compradores" => "compradores", "articulos" => "artículos", "pedidos" => "pedidos",
                "cajas" => "cajas", "pagos" => "pagos", _ => "logística"
            });
            return;
        }

        var dashboard = uri.Query.Contains("origen=dashboard", StringComparison.OrdinalIgnoreCase)
            || HttpContextAccessor.HttpContext?.Request.Cookies["MillyGo.NavOrigin"] == "dashboard";
        VolverUrl = dashboard ? "/admin/dashboard" : "/admin";
        VolverTexto = dashboard ? "Volver al Dashboard" : "Volver al menú";
    }

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;
}
