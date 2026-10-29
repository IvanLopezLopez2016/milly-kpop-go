using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MillyKpopGo.Logistica;

namespace MillyKpopGo.Components.Pages;

public partial class LogisticaDetalle : ComponentBase, IDisposable
{
    [Parameter] public Guid Id {get;set;}
    [SupplyParameterFromQuery(Name="aviso")] public string? Aviso {get;set;}
    [Inject] private LogisticaUiService Data {get;set;} = default!;
    [Inject] private IHttpContextAccessor Http {get;set;} = default!;
    [Inject] private IAntiforgery Anti {get;set;} = default!;
    [Inject] private PersistentComponentState State {get;set;} = default!;
    [Inject] private ISnackbar Snackbar {get;set;} = default!;
    private PersistingComponentStateSubscription _subscription;
    private bool _notificado;
    protected LogisticaDetail? Registro {get;set;}
    protected List<LogisticaCargo> Cargos {get;set;} = [];
    protected string Token {get;set;} = "";
    protected bool Loading {get;set;} = true;
    protected string? Error {get;set;}
    protected static string Dinero(decimal x) => x.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));
    protected static readonly (string Key, string Value)[] TiposCargo = [
        ("EMS","EMS"),("ADUANA","Aduana"),("CRUCE","Cruce"),("COMISION","Comisión"),
        ("MULTA","Multa"),("ENVIO_LOCAL","Envío local"),("OTRO","Otro")];
    protected static readonly (string Key, string Value)[] TiposEntrega = [
        ("ENVIO","Envío"),("PERSONAL","Personal"),("URGENTE_KA","Urgente KA"),
        ("URGENTE_MX","Urgente MX"),("ENVIO_CON_ALMACEN","Envío con almacén")];
    protected static readonly (string Key, string Value)[] Estados = [
        ("EN_ESPERA","En espera"),("PEDIDO","Pedido"),("DIRECCION_COREA","Dirección Corea"),
        ("DIRECCION_JAPON","Dirección Japón"),("DIRECCION_USA","Dirección USA"),
        ("COTIZANDO_ENVIO","Cotizando envío"),("ENVIADO","Enviado"),("ADUANA","Aduana"),
        ("EN_MEXICO","En México"),("LISTO_PARA_ENTREGAR","Listo para entregar"),
        ("ENTREGADO_COMPLETADO","Entregado / completado")];
    protected override async Task OnInitializedAsync()
    {
        var cacheKey = "mil-log-det-" + Id.ToString("D");
        if (State.TryTakeFromJson<LogisticaDetail>(cacheKey, out var cached) && cached is not null)
        {
            Registro = cached;
            State.TryTakeFromJson<List<LogisticaCargo>>(cacheKey + "-charges", out var charges);
            Cargos = charges ?? [];
            State.TryTakeFromJson<string>(cacheKey + "-csrf", out var csrf);
            Token = csrf ?? "";
            Loading = false;
        }
        else if (Http.HttpContext is { } context)
        {
            try
            {
                var view = await Data.CargarAsync(context);
                Registro = view.Detalles.FirstOrDefault(x => x.Id == Id);
                if (Registro is null) Error = "Artículo no encontrado.";
                else
                {
                    Cargos = await Data.CargarCargosAsync(context, Id);
                    Token = Anti.GetAndStoreTokens(context).RequestToken ?? "";
                }
            }
            catch (Exception ex) { Error = ex.Message; }
            Loading = false;
        }
        else { Error = "Sesión no disponible. Vuelve a Logística desde el panel."; Loading = false; }
        _subscription = State.RegisterOnPersisting(() =>
        {
            if (Registro is not null)
            {
                State.PersistAsJson(cacheKey, Registro);
                State.PersistAsJson(cacheKey + "-charges", Cargos);
                State.PersistAsJson(cacheKey + "-csrf", Token);
            }
            return Task.CompletedTask;
        });
    }
    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender && !_notificado && Aviso is "cargo" or "entrega")
        {
            _notificado = true;
            Snackbar.Add(Aviso == "cargo" ? "Cargo registrado correctamente." : "Seguimiento actualizado correctamente.", Severity.Success);
        }
    }
    public void Dispose() => _subscription.Dispose();
}
