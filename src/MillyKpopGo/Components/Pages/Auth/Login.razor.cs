using Microsoft.AspNetCore.Components;

namespace MillyKpopGo.Components.Pages.Auth;

public partial class Login
{
    [SupplyParameterFromQuery(Name = "error")]
    public string? Error { get; set; }

    protected bool MostrarError => Error is not null;
}
