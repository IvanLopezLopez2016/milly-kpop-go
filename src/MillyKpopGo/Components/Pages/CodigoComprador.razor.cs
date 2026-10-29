using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components;
using MillyKpopGo.Catalogos;

namespace MillyKpopGo.Components.Pages;

public partial class CodigoComprador : ComponentBase, IDisposable
{
    [Parameter] public Guid Id { get; set; }
    [Inject] private CatalogosUiService Data { get; set; } = default!;
    [Inject] private IHttpContextAccessor Http { get; set; } = default!;
    [Inject] private IAntiforgery Antiforgery { get; set; } = default!;
    [Inject] private PersistentComponentState Persist { get; set; } = default!;
    private PersistingComponentStateSubscription _subscription;
    protected CompradorUi? Comprador { get; set; }
    protected bool Cargando { get; set; } = true;
    protected string Token { get; set; } = "";
    protected string? Error { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Persist.TryTakeFromJson<CompradorUi>("milly-codigo-comprador", out var buyer) && buyer is not null && buyer.Id == Id)
        {
            Comprador = buyer;
            Persist.TryTakeFromJson<string>("milly-codigo-csrf", out var saved);
            Token = saved ?? "";
        }
        else if (Http.HttpContext is { } ctx)
        {
            try
            {
                Comprador = (await Data.CompradoresAsync(ctx)).FirstOrDefault(x => x.Id == Id);
                Token = Antiforgery.GetAndStoreTokens(ctx).RequestToken ?? "";
                if (Comprador is null) Error = "El comprador no existe.";
            }
            catch (Exception ex) { Error = ex.Message; }
        }
        else Error = "No se pudo consultar la sesión. Regresa al listado de compradores.";
        Cargando = false;
        _subscription = Persist.RegisterOnPersisting(() =>
        {
            if (Comprador is not null) Persist.PersistAsJson("milly-codigo-comprador", Comprador);
            Persist.PersistAsJson("milly-codigo-csrf", Token);
            return Task.CompletedTask;
        });
    }
    public void Dispose() => _subscription.Dispose();
}
