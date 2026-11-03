-- Entrega 012.6 — Retirar artículo de Stock (sin destruir historial).
-- Ejecutar en Supabase SQL Editor antes de instalar el código.
-- Solo afecta las unidades AÚN DISPONIBLES; las ya asignadas permanecen intactas.
create or replace function public.retirar_stock_go(p_stock_id uuid)
returns boolean
language plpgsql
security invoker
set search_path = ''
as $$
declare
    v_unidades integer;
begin
    if not private.es_admin() then
        raise exception 'NO_AUTORIZADO' using errcode = '42501';
    end if;
    select s.cantidad_disponible
      into v_unidades
      from public.articulos_stock s
     where s.id = p_stock_id
     for update;
    if not found then
        raise exception 'STOCK_NO_ENCONTRADO';
    end if;
    if v_unidades <= 0 then
        raise exception 'SIN_EXISTENCIAS';
    end if;
    -- Eliminación lógica del remanente: se conserva el origen/historial.
    update public.articulos_stock
       set cantidad_disponible = 0
     where id = p_stock_id;
    return true;
end;
$$;
revoke all on function public.retirar_stock_go(uuid) from public, anon;
grant execute on function public.retirar_stock_go(uuid) to authenticated;
