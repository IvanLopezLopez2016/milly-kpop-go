-- NO ejecutar sin sustituir el UUID real.
-- PASO 1: crear manualmente el usuario ADMIN en Supabase > Authentication > Users.
-- PASO 2: copiar su UUID (no su correo), reemplazar la cadena inferior.
-- PASO 3: ejecutar esta sentencia en SQL Editor con un rol privilegiado.
-- SOLO para el primer administrador; no usar este procedimiento desde el navegador.
insert into public.perfiles (auth_user_id,rol,comprador_id)
values ('00000000-0000-0000-0000-000000000000'::uuid,'ADMIN',null);
