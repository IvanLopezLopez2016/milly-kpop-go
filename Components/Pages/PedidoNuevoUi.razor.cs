using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components;
using MillyKpopGo.Pedidos;
namespace MillyKpopGo.Components.Pages;
public partial class PedidoNuevoUi : PedidosCajasBase
{
    [Inject] private IAntiforgery Antiforgery {get;set;} = default!;
    protected override string CacheKey => "go-pedido-nuevo-84";
    protected override Task<PedidosCajasView> LoadAsync(HttpContext ctx) => Data.ReadOrdersAsync(ctx);
    protected string Token {get;set;} = "";
    private readonly List<int> indices = [0];
    protected IEnumerable<int> Indices => indices;
    protected void AddRow() { if(indices.Count < 30) { for(var i=0;i<30;i++) if(!indices.Contains(i)) { indices.Add(i); break; } } }
    protected void RemoveRow(int i) { if(indices.Count > 1) indices.Remove(i); }
    protected override void PersistExtra() => State.PersistAsJson("go84-order-csrf" + CacheKey, Token);
    protected override async Task OnInitializedAsync()
    {
        if(State.TryTakeFromJson<string>("go84-order-csrf" + CacheKey, out var cached)) Token = cached ?? "";
        else if(Context.HttpContext is { } ctx) Token = Antiforgery.GetAndStoreTokens(ctx).RequestToken ?? "";
        await base.OnInitializedAsync();
    }
}
