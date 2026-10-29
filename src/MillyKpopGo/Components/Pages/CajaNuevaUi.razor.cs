using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components;
namespace MillyKpopGo.Components.Pages;
public partial class CajaNuevaUi : Microsoft.AspNetCore.Components.ComponentBase, IDisposable
{
    [Inject] private IHttpContextAccessor Http {get;set;} = default!;
    [Inject] private IAntiforgery Anti {get;set;} = default!;
    [Inject] private PersistentComponentState State {get;set;} = default!;
    private PersistingComponentStateSubscription _subscription;
    protected string Token {get;set;} = "";
    protected override void OnInitialized()
    {
        if(State.TryTakeFromJson<string>("go84-newbox-csrf",out var cached)) Token=cached ?? "";
        else if(Http.HttpContext is { } ctx) Token=Anti.GetAndStoreTokens(ctx).RequestToken ?? "";
        _subscription=State.RegisterOnPersisting(() => { State.PersistAsJson("go84-newbox-csrf",Token); return Task.CompletedTask; });
    }
    public void Dispose() => _subscription.Dispose();
}
