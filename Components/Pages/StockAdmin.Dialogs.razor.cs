using MillyKpopGo.Stock;

namespace MillyKpopGo.Components.Pages;

// Estado visual de los diálogos. No modifica StockAdmin.razor.cs existente.
public partial class StockAdmin
{
    protected StockItem? ArticuloAsignar { get; set; }
    protected StockItem? ArticuloEliminar { get; set; }
    protected bool AsignarVisible { get; set; }
    protected bool EliminarVisible { get; set; }

    protected void AbrirAsignacion(StockItem item)
    {
        ArticuloAsignar = item;
        AsignarVisible = true;
    }
    protected void CerrarAsignacion() => AsignarVisible = false;

    protected void CerrarEliminar() => EliminarVisible = false;
}
