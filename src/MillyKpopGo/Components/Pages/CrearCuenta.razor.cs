using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components;
namespace MillyKpopGo.Components.Pages;
public partial class CrearCuenta : ComponentBase, IDisposable
{
    [SupplyParameterFromQuery(Name="error")] public string? Error { get; set; }
    [Inject] private IHttpContextAccessor Http { get; set; } = default!;
    [Inject] private IAntiforgery Antiforgery { get; set; } = default!;
    [Inject] private PersistentComponentState State { get; set; } = default!;
    private PersistingComponentStateSubscription _subscription;
    protected string Token { get; set; } = "";
    protected override void OnInitialized()
    {
        if(!State.TryTakeFromJson<string>("go-crear-cuenta-antiforgery",out var saved))
            saved=Http.HttpContext is { } context ? Antiforgery.GetAndStoreTokens(context).RequestToken : null;
        Token=saved??"";
        _subscription=State.RegisterOnPersisting(() => { State.PersistAsJson("go-crear-cuenta-antiforgery",Token);return Task.CompletedTask; });
    }
    public void Dispose()=>_subscription.Dispose();
}
