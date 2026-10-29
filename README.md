# MILLY K-POP GO — Entrega 001 (arranque)

**Objetivo:** preparar la base técnica para GO con ADMIN y CLIENTE. **Todavía NO es un sistema operativo de pedidos.**

## 1. Arrancar Blazor (Windows PowerShell)

```powershell
cd "C:\ruta\MILLY-KPOP-GO-ENTREGA-001"
.\INICIAR.ps1
cd .\src\MillyKpopGo
dotnet run
```

Si PowerShell bloquea el script, abre `INICIAR.ps1`, inspecciónalo, o usa una consola de PowerShell con la política apropiada según las reglas de tu equipo. No es necesario desactivar permanentemente ninguna protección. El script requiere el SDK .NET 10; genera el proyecto desde la plantilla oficial y coloca una pantalla de demostración.

`dotnet run` mostrará en consola la URL local, por ejemplo `http://localhost:XXXX`. No publicar todavía.

## 2. Preparar Supabase

1. Crear proyecto en Supabase y guardar la contraseña de PostgreSQL fuera del repositorio.
2. Abrir **SQL Editor** y ejecutar `database/001_schema.sql` en un proyecto *nuevo*.
3. Ir a **Authentication > Providers > Email** y deshabilitar el registro público / permitir nuevas altas (según nombre de opción en el dashboard). Comprobar que clientes no pueden autorregistrarse.
4. Crear manualmente un usuario ADMIN en **Authentication > Users**. Confirmar su correo y configurar su contraseña por un procedimiento seguro.
5. Copiar su UUID e introducirlo en una copia local de `database/002_admin_bootstrap_EJEMPLO.sql` en lugar del UUID de ceros. Ejecutar la sentencia. **No ejecutar el archivo sin modificar.**
6. No pegar `service_role` / secret keys en archivos Razor, JS, repositorios ni mensajes. La integración de Auth y la conexión de datos se realizará en la siguiente entrega.

## 3. Regla de seguridad

En esta primera entrega la pantalla Blazor es una demo **sin autenticación** y no hace consultas de datos. No cargar compradores reales mediante ella. Las tablas SQL están bajo RLS, y clientes solo pueden consultar sus propias filas.

**Advertencia:** aunque RLS restringe accesos de usuarios, las políticas administrativas autorizan todas las operaciones desde un cliente con sesión de ADMIN. Antes de conectar escrituras implementaremos RPC/servicios transaccionales que validen que las aplicaciones de pago no superen los importes ni crucen compradores. También implementaremos reglas de creación de invitaciones exclusivamente desde servidor.

## 4. Qué queda para siguientes entregas

- Auth Supabase en Blazor con sesión segura y políticas de autorización.
- CRUD funcional de compradores y artículos, incluyendo búsqueda y variantes.
- Pedidos, cajas, cargos, pagos, saldos, almacén y entregas.
- Panel CLIENTE en modo solo lectura, escaneo y importador de Excel.
- Pruebas de RLS con dos usuarios de ejemplo.

## 5. Si ocurre un error

Comparte la salida de `dotnet --version` y el mensaje completo de `dotnet run` (sin contraseñas ni cadenas de conexión).
