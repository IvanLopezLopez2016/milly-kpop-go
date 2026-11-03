using Microsoft.AspNetCore.Components;
using MudBlazor;
using MillyKpopGo.Dashboard;

namespace MillyKpopGo.Components.Pages;

public partial class DashboardPage : ComponentBase, IDisposable
{
    [Inject] private DashboardUiService Dashboard { get; set; } = default!;
    [Inject] private IHttpContextAccessor Http { get; set; } = default!;
    [Inject] private PersistentComponentState State { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private PersistingComponentStateSubscription _subscription;
    protected DashboardResumen Resumen { get; set; } = new();
    protected bool Cargando { get; set; } = true;
    protected string? Error { get; set; }
    protected static string Dinero(decimal valor) => valor.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("es-MX"));
    protected static string Estado(string valor) => valor.Replace('_', ' ');

    protected override async Task OnInitializedAsync()
    {
        if (State.TryTakeFromJson<DashboardResumen>("milly-dashboard-010", out var stored) && stored is not null)
        {
            Resumen = stored;
            Cargando = false;
        }
        else if (Http.HttpContext is { } ctx)
        {
            try { Resumen = await Dashboard.CargarAsync(ctx); }
            catch (Exception ex) { Error = ex.Message; }
            finally { Cargando = false; }
        }
        else
        {
            Error = "No se pudo recuperar la sesión. Actualiza esta página desde el navegador.";
            Cargando = false;
        }
        _subscription = State.RegisterOnPersisting(() =>
        {
            State.PersistAsJson("milly-dashboard-010", Resumen);
            return Task.CompletedTask;
        });
    }

    protected void MostrarExito() => Snackbar.Add("Panel de MILLY K-POP GO listo", Severity.Success);
    public void Dispose() => _subscription.Dispose();
}
