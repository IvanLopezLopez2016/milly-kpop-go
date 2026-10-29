using MillyKpopGo.Pedidos;
namespace MillyKpopGo.Components.Pages;
public partial class PedidosLista : PedidosCajasBase
{
    protected override string CacheKey => "go-pedidos-84";
    protected override Task<PedidosCajasView> LoadAsync(HttpContext ctx) => Data.ReadOrdersAsync(ctx);
    protected IEnumerable<PcOrder> Filtrados => string.IsNullOrWhiteSpace(Busqueda) ? View.Pedidos : View.Pedidos.Where(x => View.Comprador(x.CompradorId).Contains(Busqueda.Trim(),StringComparison.OrdinalIgnoreCase) || x.Id.ToString().Contains(Busqueda.Trim(),StringComparison.OrdinalIgnoreCase));
    protected decimal Total(Guid id) => View.Detalles.Where(d => d.PedidoId == id).Sum(d => d.Cantidad * d.PrecioUnitario);
    protected int Articulos(Guid id) => View.Detalles.Count(d => d.PedidoId == id);
}
