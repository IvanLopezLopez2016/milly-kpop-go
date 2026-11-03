param([string]$ProjectRoot = 'C:\Proyectos\MILLY-KPOP-GO')
$ErrorActionPreference = 'Stop'
$app = Join-Path $ProjectRoot 'src\MillyKpopGo'
$source = $PSScriptRoot
$backup = Join-Path $ProjectRoot ('_RESPALDOS\ENTREGA_012_6_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
$utf8 = [System.Text.UTF8Encoding]::new($false)
$targets = @(
  'Components\Pages\StockAdmin.razor',
  'Components\Pages\StockAdmin.razor.css',
  'Components\Pages\StockAdmin.Dialogs.razor.cs',
  'Stock\StockEndpoints.cs'
)
$existed = @{}
$originals = @{}
try {
  $pagePath = Join-Path $app 'Components\Pages\StockAdmin.razor'
  $endpointPath = Join-Path $app 'Stock\StockEndpoints.cs'
  if (!(Test-Path $pagePath) -or !(Test-Path $endpointPath)) { throw 'Faltan archivos de la entrega 012.5. No se aplicaron cambios.' }
  $oldPage = [IO.File]::ReadAllText($pagePath)
  $oldEndpoints = [IO.File]::ReadAllText($endpointPath)
  if (!$oldPage.Contains('@page "/admin/stock"') -or !$oldPage.Contains('/admin/stock/agregar') -or !$oldPage.Contains('/admin/stock/asignar')) { throw 'StockAdmin.razor tiene estructura no compatible. No se cambiaron archivos.' }
  if (!$oldEndpoints.Contains('app.MapPost("/admin/stock/asignar"') -or !$oldEndpoints.Contains('private static async Task<IResult> Asignar(')) { throw 'StockEndpoints.cs tiene estructura no compatible. No se cambiaron archivos.' }
  if ($oldEndpoints.Contains('"/admin/stock/eliminar"')) { throw 'Ya existe una ruta de eliminación. Revisa si el parche está instalado.' }
  $partialPath = Join-Path $app 'Components\Pages\StockAdmin.Dialogs.razor.cs'
  if (Test-Path $partialPath) { throw 'StockAdmin.Dialogs.razor.cs ya existe. No se sobrescribirá.' }

  foreach ($rel in $targets) {
    $dest = Join-Path $app $rel
    $existed[$rel] = Test-Path $dest
    if ($existed[$rel]) { $originals[$rel] = [IO.File]::ReadAllBytes($dest) }
  }
  New-Item -ItemType Directory -Force -Path $backup | Out-Null
  foreach ($rel in $targets) {
    $dest = Join-Path $app $rel
    if ($existed[$rel]) {
      $out = Join-Path $backup $rel
      New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
      [IO.File]::WriteAllBytes($out, $originals[$rel])
    }
  }

  $routeAnchor = 'app.MapPost("/admin/stock/asignar", Asignar).RequireAuthorization(new AuthorizeAttribute { Roles="ADMIN" }).DisableAntiforgery();'
  if (!$oldEndpoints.Contains($routeAnchor)) { throw 'No se encontró el registro esperado de /admin/stock/asignar.' }
  $newEndpoints = $oldEndpoints.Replace($routeAnchor, $routeAnchor + "`r`n        " + 'app.MapPost("/admin/stock/eliminar", Eliminar).RequireAuthorization(new AuthorizeAttribute { Roles="ADMIN" }).DisableAntiforgery();')
  $methodAnchor = '    private static async Task<IResult> Asignar('
  $snippet = [IO.File]::ReadAllText((Join-Path $source 'snippets\StockEliminarEndpoint.cs.txt'))
  $newEndpoints = $newEndpoints.Replace($methodAnchor, $snippet + "`r`n" + $methodAnchor)
  [IO.File]::WriteAllText($endpointPath, $newEndpoints, $utf8)

  foreach ($rel in @('Components\Pages\StockAdmin.razor', 'Components\Pages\StockAdmin.razor.css', 'Components\Pages\StockAdmin.Dialogs.razor.cs')) {
    $src = Join-Path $source ('files\' + $rel)
    $dst = Join-Path $app $rel
    Copy-Item -LiteralPath $src -Destination $dst -Force
  }

  $project = Join-Path $app 'MillyKpopGo.csproj'
  if (!(Test-Path $project)) { throw "No se encontró el proyecto: $project" }
  & dotnet build $project -c Release
  if ($LASTEXITCODE -ne 0) { throw 'La compilación falló.' }
  Write-Host 'Entrega 012.6 aplicada y compilada. MainLayout no se modificó.' -ForegroundColor Green
  Write-Host "Respaldo: $backup"
}
catch {
  Write-Host "Error: $_" -ForegroundColor Red
  foreach ($rel in $targets) {
    if ($existed.ContainsKey($rel)) {
      $dest = Join-Path $app $rel
      if ($existed[$rel]) { [IO.File]::WriteAllBytes($dest, $originals[$rel]) }
      elseif (Test-Path $dest) { Remove-Item -LiteralPath $dest -Force }
    }
  }
  Write-Host 'Archivos restaurados (si se habían modificado). El SQL no se revierte automáticamente.' -ForegroundColor Yellow
  exit 1
}
