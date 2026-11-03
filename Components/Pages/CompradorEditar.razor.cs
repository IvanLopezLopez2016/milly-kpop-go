using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MillyKpopGo.Catalogos;

namespace MillyKpopGo.Components.Pages;

public partial class CompradorEditar : ComponentBase, IDisposable
{
    [Parameter] public Guid Id { get; set; }
    [Inject] private CatalogosUiService Data { get; set; } = default!;
    [Inject] private IHttpContextAccessor Http { get; set; } = default!;
    [Inject] private IAntiforgery Anti { get; set; } = default!;
    [Inject] private PersistentComponentState State { get; set; } = default!;
    private PersistingComponentStateSubscription _subscription;
    protected CompradorUi? Registro { get; set; }
    protected string Token { get; set; } = "";
    protected string? Error { get; set; }
    protected bool Cargando { get; set; } = true;
    protected override async Task OnInitializedAsync()
    {
        var key = "milly-compradores-edit-" + Id.ToString("D");
        if (State.TryTakeFromJson<CompradorUi>(key, out var saved) && saved is not null)
        {
            Registro = saved;
            State.TryTakeFromJson<string>(key + "-csrf", out var csrf);
            Token = csrf ?? "";
        }
        else if (Http.HttpContext is { } ctx)
        {
            try
            {
                Registro = (await Data.CompradoresAsync(ctx)).FirstOrDefault(x => x.Id == Id);
                if (Registro == null) Error = "No se encontró el registro.";
                Token = Anti.GetAndStoreTokens(ctx).RequestToken ?? "";
            }
            catch (Exception ex) { Error = ex.Message; }
        }
        else Error = "No se pudo recuperar la sesión.";
        Cargando = false;
        _subscription = State.RegisterOnPersisting(() => { if (Registro != null) State.PersistAsJson(key, Registro); State.PersistAsJson(key + "-csrf", Token); return Task.CompletedTask; });
    }
    public void Dispose() => _subscription.Dispose();
}
