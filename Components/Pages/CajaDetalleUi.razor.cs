using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components;
using MillyKpopGo.Pedidos;
using MudBlazor;
namespace MillyKpopGo.Components.Pages;
public partial class CajaDetalleUi : PedidosCajasBase
{
    [Parameter] public Guid Id {get;set;}
    [SupplyParameterFromQuery(Name="aviso")] public string? Aviso {get;set;}
    [Inject] private IAntiforgery Antiforgery {get;set;} = default!;
    private bool _notificado;
    protected string Token {get;set;} = "";
    protected override string CacheKey => "go-caja-84-" + Id.ToString("D");
    protected override Task<PedidosCajasView> LoadAsync(HttpContext ctx) => Data.ReadBoxesAsync(ctx);
    protected PcBox? Caja => View.Cajas.FirstOrDefault(x=>x.Id==Id);
    protected IEnumerable<PcDetail> Asignados => View.Detalles.Where(x=>x.CajaId==Id);
    protected IEnumerable<PcDetail> Disponibles => View.Detalles.Where(x=>x.CajaId is null && x.Estado is not "CANCELADO" and not "ENTREGADO_COMPLETADO");
    protected string EstadoSeleccionado {get;set;} = "EN_ESPERA";
    protected bool ActualizarArticulos {get;set;}
    protected bool ConfirmarRetiro {get;set;}
    protected Guid PendienteRetirar {get;set;}
    protected void SolicitarRetiro(Guid id) { PendienteRetirar=id; ConfirmarRetiro=true; }
    protected void CancelarRetiro() { PendienteRetirar=Guid.Empty; ConfirmarRetiro=false; }
    protected static readonly (string Key,string Text)[] Estados = [
        ("EN_ESPERA","En espera"),("PEDIDO","Pedido"),("DIRECCION_COREA","Dirección Corea"),("DIRECCION_JAPON","Dirección Japón"),("DIRECCION_USA","Dirección USA"),("COTIZANDO_ENVIO","Cotizando envío"),("ENVIADO","Enviado"),("ADUANA","Aduana"),("EN_MEXICO","En México"),("LISTO_PARA_ENTREGAR","Listo para entregar")];
    protected override void PersistExtra() => State.PersistAsJson("go84-box-csrf" + CacheKey, Token);
    protected override async Task OnInitializedAsync()
    {
        if(State.TryTakeFromJson<string>("go84-box-csrf" + CacheKey, out var cached)) Token = cached ?? "";
        else if(Context.HttpContext is { } ctx) Token = Antiforgery.GetAndStoreTokens(ctx).RequestToken ?? "";
        await base.OnInitializedAsync();
        EstadoSeleccionado = Caja?.Estado ?? "EN_ESPERA";
    }
    protected override void OnAfterRender(bool firstRender)
    {
        if(firstRender && !_notificado && (Aviso is "guardado" or "asignado" or "retirado"))
        {
            _notificado=true;
            Snackbar.Add(Aviso == "guardado" ? "Estado actualizado correctamente." : Aviso == "asignado" ? "Artículo asignado a la caja." : "Artículo retirado correctamente.",Severity.Success);
        }
    }
}
