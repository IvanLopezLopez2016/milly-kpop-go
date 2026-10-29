$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$destino = Join-Path $root 'src\MillyKpopGo'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'No se encontró dotnet. Instala el SDK .NET 10.' }
if (Test-Path $destino) { throw 'La carpeta src\MillyKpopGo ya existe. No se sobrescribirá.' }
New-Item -ItemType Directory -Force -Path (Split-Path $destino) | Out-Null
& dotnet new blazor --interactivity Server --all-interactive --framework net10.0 -n MillyKpopGo -o $destino
if ($LASTEXITCODE -ne 0) { throw 'Falló la creación del proyecto Blazor.' }
Copy-Item (Join-Path $root 'starter\Components\Pages\Home.razor') (Join-Path $destino 'Components\Pages\Home.razor') -Force
Copy-Item (Join-Path $root 'starter\wwwroot\go.css') (Join-Path $destino 'wwwroot\go.css') -Force
$app = Join-Path $destino 'Components\App.razor'
$markup = Get-Content $app -Raw
if ($markup -notmatch 'go.css') { $markup = $markup.Replace('</head>', '    <link rel="stylesheet" href="go.css" />' + [Environment]::NewLine + '</head>') }
[System.IO.File]::WriteAllText($app, $markup)
Write-Host 'Proyecto generado. Para iniciarlo:' -ForegroundColor Green
Write-Host ('cd "' + $destino + '"')
Write-Host 'dotnet run'
