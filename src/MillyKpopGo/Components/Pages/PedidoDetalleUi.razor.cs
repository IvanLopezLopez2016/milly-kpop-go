using Microsoft.AspNetCore.Components;
using MillyKpopGo.Pedidos;
namespace MillyKpopGo.Components.Pages;
public partial class PedidoDetalleUi : PedidosCajasBase
{
    [Parameter] public Guid Id {get;set;}
    protected override string CacheKey => "go-pedido-84-" + Id.ToString("D");
    protected override Task<PedidosCajasView> LoadAsync(HttpContext ctx) => Data.ReadOrdersAsync(ctx);
    protected PcOrder? Pedido => View.Pedidos.FirstOrDefault(x => x.Id == Id);
    protected IEnumerable<PcDetail> Items => View.Detalles.Where(x => x.PedidoId == Id);
    protected decimal Total => Items.Sum(x => x.Cantidad * x.PrecioUnitario);
}
