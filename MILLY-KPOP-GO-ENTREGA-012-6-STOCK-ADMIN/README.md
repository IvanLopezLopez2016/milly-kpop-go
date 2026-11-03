# MILLY K-POP GO — Entrega 012.6: Stock ADMIN compacto + Eliminar

No toca `MainLayout.razor`, `MainLayout.razor.css`, `StockDisponible` (portal compradoras), ni la clase original `StockAdmin.razor.cs`.

## Qué cambia

- `/admin/stock`: tabla `MudTable` compacta, 980 px máximo, paginación y tema #7042A1/blanco.
- Botón **Asignar** abre `MudDialog` con formulario existente.
- Botón **Eliminar** abre `MudDialog` de confirmación: sin `confirm()` de JavaScript.
- Retirar del Stock pone `cantidad_disponible=0`, **no hace DELETE físico**. Conserva el origen de la cancelación y el historial. Unidades asignadas antes no son afectadas. El artículo ya no aparece en Stock administrativo (solo se muestran disponibles) ni en Stock para compradoras.
- El nuevo endpoint y RPC verifican rol `ADMIN`, token antifalsificación y JWT Supabase.

## Instalar

1. Respaldar datos de Supabase como buena práctica.
2. Ejecutar `database/012_6_retirar_stock.sql` en Supabase SQL Editor.
3. Detener ejecución local, extraer ZIP, ejecutar `Set-ExecutionPolicy -Scope Process Bypass` y `./APLICAR.ps1` desde su carpeta.
4. El instalador crea respaldo, modifica solo `StockAdmin.razor`, `StockAdmin.razor.css`, nuevo parcial `StockAdmin.Dialogs.razor.cs` y `StockEndpoints.cs`, ejecuta `dotnet build -c Release` y restaura archivos si falla.
5. Probar alta, asignación, eliminación y recarga del stock; después publicar con Git.

**Limitación:** no contamos con el proyecto completo ni el SDK .NET en este entorno; el build se ejecuta en tu Windows. El instalador se detiene si encuentra cambios incompatibles. La migración SQL se hace por separado y no se revierte automáticamente.
