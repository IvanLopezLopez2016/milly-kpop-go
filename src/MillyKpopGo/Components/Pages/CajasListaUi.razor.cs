using MillyKpopGo.Pedidos;
namespace MillyKpopGo.Components.Pages;
public partial class CajasListaUi : PedidosCajasBase
{
    protected override string CacheKey => "go-cajas-84";
    protected override Task<PedidosCajasView> LoadAsync(HttpContext ctx) => Data.ReadBoxesAsync(ctx);
    protected IEnumerable<PcBox> Filtrados => string.IsNullOrWhiteSpace(Busqueda) ? View.Cajas : View.Cajas.Where(x=> (x.Codigo + " " + x.Descripcion + " " + x.Origen + " " + x.Estado).Contains(Busqueda.Trim(),StringComparison.OrdinalIgnoreCase));
    protected int Cantidad(Guid id) => View.Detalles.Count(d=>d.CajaId==id);
}
