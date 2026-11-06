using Microsoft.AspNetCore.Components;
using MudBlazor;
using MillyKpopGo.Pedidos;
namespace MillyKpopGo.Components.Pages;
public partial class PedidoDetalleUi : PedidosCajasBase
{
    protected Guid DetalleMovimiento {get;set;}
    protected PcDetail? ArticuloConMovimiento => View.Detalles.FirstOrDefault(x => x.Id == DetalleMovimiento && x.PedidoId == Id);
    protected PcDetail? ParaEliminar {get;set;}
    protected bool ConfirmarVisible {get;set;}
    protected bool AvisoVisible {get;set;}
    protected bool Procesando {get;set;}
    protected void SolicitarEliminar(PcDetail detail) { ParaEliminar=detail; ConfirmarVisible=true; }

    protected Task Confirmar()
    {
        if (ParaEliminar is null) return Task.CompletedTask;
        return EjecutarCancelacion(ParaEliminar.Id, aceptarMovimientos: false);
    }

    protected Task ConfirmarConMovimientos()
    {
        if (DetalleMovimiento == Guid.Empty) return Task.CompletedTask;
        return EjecutarCancelacion(DetalleMovimiento, aceptarMovimientos: true);
    }

    private async Task EjecutarCancelacion(Guid detalleId, bool aceptarMovimientos)
    {
        if (Context.HttpContext is not { } ctx)
        {
            Snackbar.Add("La sesión no está disponible. Vuelve a entrar desde el panel.", Severity.Error);
            return;
        }
        Procesando = true;
        try
        {
            var estado = await Data.CancelarDetalleAsync(Id, detalleId, aceptarMovimientos, ctx);
            switch (estado)
            {
                case "REQUIERE_CONFIRMACION":
                    DetalleMovimiento = detalleId;
                    ConfirmarVisible = false;
                    AvisoVisible = true;
                    break;
                case "STOCK":
                    Snackbar.Add("Artículo retirado del pedido y enviado a Stock.", Severity.Success);
                    await CerrarYRecargar(ctx);
                    break;
                case "ELIMINADO":
                    Snackbar.Add("Artículo eliminado. El total del pedido se actualizó.", Severity.Success);
                    await CerrarYRecargar(ctx);
                    break;
                case "YA_CANCELADO":
                    Snackbar.Add("El artículo ya no existe en este pedido.", Severity.Warning);
                    await CerrarYRecargar(ctx);
                    break;
                default:
                    Snackbar.Add("No se pudo eliminar el artículo. Verifica el estado del artículo y que tu sesión siga activa.", Severity.Error);
                    break;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            Snackbar.Add("No fue posible conectar con Supabase o la sesión caducó.", Severity.Error);
        }
        finally
        {
            Procesando = false;
        }
    }

    private async Task CerrarYRecargar(HttpContext ctx)
    {
        ConfirmarVisible = false;
        AvisoVisible = false;
        ParaEliminar = null;
        DetalleMovimiento = Guid.Empty;
        View = await Data.ReadOrdersAsync(ctx);
    }

    [Parameter] public Guid Id {get;set;}
    protected override string CacheKey => "go-pedido-84-" + Id.ToString("D");
    protected override Task<PedidosCajasView> LoadAsync(HttpContext ctx) => Data.ReadOrdersAsync(ctx);
    protected PcOrder? Pedido => View.Pedidos.FirstOrDefault(x => x.Id == Id);
    protected IEnumerable<PcDetail> Items => View.Detalles.Where(x => x.PedidoId == Id && x.Estado != "CANCELADO");
    protected decimal Total => Items.Sum(x => x.Cantidad * x.PrecioUnitario);
}
