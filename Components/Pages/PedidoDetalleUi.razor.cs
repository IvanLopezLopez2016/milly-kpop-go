using Microsoft.AspNetCore.Components;
using MillyKpopGo.Pedidos;
namespace MillyKpopGo.Components.Pages;
public partial class PedidoDetalleUi : PedidosCajasBase
{
    [SupplyParameterFromQuery(Name="detalle")] public Guid DetalleMovimiento {get;set;}
    protected PcDetail? ArticuloConMovimiento => View.Detalles.FirstOrDefault(x => x.Id == DetalleMovimiento && x.PedidoId == Id);
    protected PcDetail? ParaEliminar {get;set;}
    protected bool ConfirmarVisible {get;set;}
    protected bool AvisoVisible {get;set;}
    protected void SolicitarEliminar(PcDetail detail) { ParaEliminar=detail; ConfirmarVisible=true; }
    protected override void OnParametersSet() { AvisoVisible = Eliminacion == "movimientos" && DetalleMovimiento != Guid.Empty; }
    [SupplyParameterFromQuery(Name = "eliminar")] public string? Eliminacion { get; set; }
    [Parameter] public Guid Id {get;set;}
    protected override string CacheKey => "go-pedido-84-" + Id.ToString("D");
    protected override Task<PedidosCajasView> LoadAsync(HttpContext ctx) => Data.ReadOrdersAsync(ctx);
    protected PcOrder? Pedido => View.Pedidos.FirstOrDefault(x => x.Id == Id);
    protected IEnumerable<PcDetail> Items => View.Detalles.Where(x => x.PedidoId == Id && x.Estado != "CANCELADO");
    protected decimal Total => Items.Sum(x => x.Cantidad * x.PrecioUnitario);
}
