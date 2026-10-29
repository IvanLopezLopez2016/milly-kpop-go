using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MillyKpopGo.Catalogos;

namespace MillyKpopGo.Components.Pages;

public partial class Articulos : ComponentBase, IDisposable
{
    [SupplyParameterFromQuery(Name = "aviso")] public string? Aviso { get; set; }
    [Inject] private CatalogosUiService Data { get; set; } = default!;
    [Inject] private IHttpContextAccessor Http { get; set; } = default!;
    [Inject] private IAntiforgery Anti { get; set; } = default!;
    [Inject] private PersistentComponentState State { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    private PersistingComponentStateSubscription _subscription;
    private bool _notificado;
    protected List<ArticuloUi> Registros { get; set; } = [];
    protected string Busqueda { get; set; } = "";
    protected string Token { get; set; } = "";
    protected bool Cargando { get; set; } = true;
    protected string? Error { get; set; }
    protected IEnumerable<ArticuloUi> Filtrados => string.IsNullOrWhiteSpace(Busqueda) ? Registros : Registros.Where(x => (x.Nombre ?? "").Contains(Busqueda.Trim(), StringComparison.OrdinalIgnoreCase) || (x.Sku ?? "").Contains(Busqueda.Trim(), StringComparison.OrdinalIgnoreCase) || (x.CodigoBarras ?? "").Contains(Busqueda.Trim(), StringComparison.OrdinalIgnoreCase) || (x.Grupo ?? "").Contains(Busqueda.Trim(), StringComparison.OrdinalIgnoreCase) || (x.Variante ?? "").Contains(Busqueda.Trim(), StringComparison.OrdinalIgnoreCase));
    protected override async Task OnInitializedAsync()
    {
        if (State.TryTakeFromJson<List<ArticuloUi>>("milly-articulos-list", out var cache) && cache != null)
        {
            Registros = cache;
            State.TryTakeFromJson<string>("milly-articulos-csrf", out var saved);
            Token = saved ?? "";
        }
        else if (Http.HttpContext is { } ctx)
        {
            try { Registros = await Data.ArticulosAsync(ctx); Token = Anti.GetAndStoreTokens(ctx).RequestToken ?? ""; }
            catch (Exception ex) { Error = ex.Message; }
        }
        else Error = "No se pudo recuperar la sesión. Vuelve a entrar desde el panel.";
        Cargando = false;
        _subscription = State.RegisterOnPersisting(() => { State.PersistAsJson("milly-articulos-list", Registros); State.PersistAsJson("milly-articulos-csrf", Token); return Task.CompletedTask; });
    }
    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender && !_notificado && Aviso is "creado" or "editado" or "estado")
        {
            _notificado = true;
            Snackbar.Add(Aviso == "creado" ? "Registro creado correctamente." : Aviso == "editado" ? "Cambios guardados correctamente." : "Estado actualizado correctamente.", Severity.Success);
        }
    }
    public void Dispose() => _subscription.Dispose();
}
