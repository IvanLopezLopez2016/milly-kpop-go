-- MILLY K-POP GO | Esquema 001 | Supabase PostgreSQL
-- Ejecutar en SQL Editor de un proyecto NUEVO. No ejecutar sobre una BD existente sin revisión.
-- Datos personales y pedidos permanecerán protegidos por RLS.
create extension if not exists pgcrypto;

create table public.compradores (
 id uuid primary key default gen_random_uuid(),
 auth_user_id uuid unique references auth.users(id) on delete set null,
 nombre text not null check (length(trim(nombre)) > 0),
 alias text,
 correo text,
 whatsapp text,
 activo boolean not null default true,
 creado_en timestamptz not null default now()
);
create unique index compradores_correo_unico on public.compradores (lower(correo)) where correo is not null;

create table public.perfiles (
 auth_user_id uuid primary key references auth.users(id) on delete cascade,
 rol text not null check (rol in ('ADMIN','CLIENTE')),
 comprador_id uuid unique references public.compradores(id) on delete restrict,
 creado_en timestamptz not null default now(),
 constraint cliente_debe_tener_comprador check ((rol = 'CLIENTE' and comprador_id is not null) or (rol = 'ADMIN' and comprador_id is null))
);

create schema if not exists private;
create or replace function private.es_admin() returns boolean language sql stable security definer set search_path = '' as $$
 select exists (select 1 from public.perfiles p where p.auth_user_id = (select auth.uid()) and p.rol = 'ADMIN');
$$;
revoke all on function private.es_admin() from public;
grant usage on schema private to authenticated;
grant execute on function private.es_admin() to authenticated;

create table public.articulos (
 id uuid primary key default gen_random_uuid(),
 sku text unique,
 codigo_barras text,
 nombre text not null check (length(trim(nombre))>0),
 grupo text,
 variante text,
 precio_referencia numeric(12,2) check (precio_referencia >= 0),
 activo boolean not null default true,
 creado_en timestamptz not null default now()
);
create index articulos_nombre_idx on public.articulos (nombre);
create index articulos_codigo_idx on public.articulos (codigo_barras);

create table public.cajas (
 id uuid primary key default gen_random_uuid(),
 codigo text not null unique,
 descripcion text,
 origen text not null check (origen in ('COREA','JAPON','USA','MEXICO','OTRO')),
 estado text not null default 'EN_ESPERA',
 notas text,
 creado_en timestamptz not null default now()
);

create table public.pedidos (
 id uuid primary key default gen_random_uuid(),
 comprador_id uuid not null references public.compradores(id),
 fecha timestamptz not null default now(),
 nota_interna text,
 creado_en timestamptz not null default now()
);
create index pedidos_comprador_idx on public.pedidos(comprador_id);

create table public.pedido_detalles (
 id uuid primary key default gen_random_uuid(),
 pedido_id uuid not null references public.pedidos(id) on delete restrict,
 articulo_id uuid not null references public.articulos(id),
 caja_id uuid references public.cajas(id),
 cantidad integer not null check (cantidad > 0),
 precio_unitario numeric(12,2) not null check (precio_unitario >= 0),
 estado text not null default 'EN_ESPERA' check (estado in (
 'EN_ESPERA','PEDIDO','DIRECCION_COREA','DIRECCION_JAPON','DIRECCION_USA',
 'COTIZANDO_ENVIO','ENVIADO','ADUANA','EN_MEXICO','LISTO_PARA_ENTREGAR',
 'ENTREGADO_COMPLETADO','STOCK','MULTA','CANCELADO')),
 tipo_entrega text check (tipo_entrega in ('ENVIO','PERSONAL','URGENTE_KA','URGENTE_MX','ENVIO_CON_ALMACEN')),
 fecha_limite date,
 nota_publica text,
 creado_en timestamptz not null default now()
);
create index detalle_pedido_idx on public.pedido_detalles(pedido_id);
create index detalle_caja_idx on public.pedido_detalles(caja_id);

create table public.cargos (
 id uuid primary key default gen_random_uuid(),
 pedido_detalle_id uuid not null references public.pedido_detalles(id),
 tipo text not null check (tipo in ('EMS','ADUANA','CRUCE','COMISION','MULTA','ENVIO_LOCAL','OTRO')),
 monto numeric(12,2) not null check (monto >= 0),
 descripcion text,
 creado_en timestamptz not null default now()
);
create index cargos_detalle_idx on public.cargos(pedido_detalle_id);

create table public.pagos (
 id uuid primary key default gen_random_uuid(),
 comprador_id uuid not null references public.compradores(id),
 monto numeric(12,2) not null check (monto > 0),
 fecha timestamptz not null default now(),
 metodo text,
 referencia text,
 nota_interna text,
 creado_en timestamptz not null default now()
);
create index pagos_comprador_idx on public.pagos(comprador_id);

-- Un pago puede aplicarse a uno o muchos detalles/cargos; el remanente queda como saldo a favor.
create table public.aplicaciones_pago (
 id uuid primary key default gen_random_uuid(),
 pago_id uuid not null references public.pagos(id),
 pedido_detalle_id uuid references public.pedido_detalles(id),
 cargo_id uuid references public.cargos(id),
 monto numeric(12,2) not null check (monto > 0),
 creado_en timestamptz not null default now(),
 constraint solo_un_destino check (num_nonnulls(pedido_detalle_id,cargo_id)=1)
);
create index aplicaciones_pago_idx on public.aplicaciones_pago(pago_id);

-- NOTA CRITICA: validación transaccional de sobreaplicaciones y pertenencia del cliente
-- se implementará en servicio/RPC protegido antes de habilitar escritura operativa.
-- Hasta entonces, los clientes son estrictamente SOLO LECTURA.

alter table public.compradores enable row level security;
alter table public.perfiles enable row level security;
alter table public.articulos enable row level security;
alter table public.cajas enable row level security;
alter table public.pedidos enable row level security;
alter table public.pedido_detalles enable row level security;
alter table public.cargos enable row level security;
alter table public.pagos enable row level security;
alter table public.aplicaciones_pago enable row level security;

-- Solo los admins autenticados pueden operar en tablas. No habilitar API con service key en navegador.
create policy admin_compradores on public.compradores for all to authenticated using (private.es_admin()) with check (private.es_admin());
create policy admin_perfiles on public.perfiles for all to authenticated using (private.es_admin()) with check (private.es_admin());
create policy admin_articulos on public.articulos for all to authenticated using (private.es_admin()) with check (private.es_admin());
create policy admin_cajas on public.cajas for all to authenticated using (private.es_admin()) with check (private.es_admin());
create policy admin_pedidos on public.pedidos for all to authenticated using (private.es_admin()) with check (private.es_admin());
create policy admin_detalles on public.pedido_detalles for all to authenticated using (private.es_admin()) with check (private.es_admin());
create policy admin_cargos on public.cargos for all to authenticated using (private.es_admin()) with check (private.es_admin());
create policy admin_pagos on public.pagos for all to authenticated using (private.es_admin()) with check (private.es_admin());
create policy admin_aplicaciones on public.aplicaciones_pago for all to authenticated using (private.es_admin()) with check (private.es_admin());

-- Cliente: acceso a lo propio, con políticas por tabla, sin actualizaciones.
create policy cliente_perfil on public.perfiles for select to authenticated using (auth_user_id=(select auth.uid()));
create policy cliente_comprador on public.compradores for select to authenticated using (auth_user_id=(select auth.uid()));
create policy cliente_pedidos on public.pedidos for select to authenticated using (
 exists (select 1 from public.compradores c where c.id=comprador_id and c.auth_user_id=(select auth.uid())));
create policy cliente_detalles on public.pedido_detalles for select to authenticated using (
 exists(select 1 from public.pedidos p join public.compradores c on c.id=p.comprador_id
 where p.id=pedido_id and c.auth_user_id=(select auth.uid())));
create policy cliente_articulos on public.articulos for select to authenticated using (
 exists(select 1 from public.pedido_detalles d join public.pedidos p on p.id=d.pedido_id
 join public.compradores c on c.id=p.comprador_id where d.articulo_id=articulos.id and c.auth_user_id=(select auth.uid())));
create policy cliente_cajas on public.cajas for select to authenticated using (
 exists(select 1 from public.pedido_detalles d join public.pedidos p on p.id=d.pedido_id
 join public.compradores c on c.id=p.comprador_id where d.caja_id=cajas.id and c.auth_user_id=(select auth.uid())));
create policy cliente_cargos on public.cargos for select to authenticated using (
 exists(select 1 from public.pedido_detalles d join public.pedidos p on p.id=d.pedido_id
 join public.compradores c on c.id=p.comprador_id where d.id=cargos.pedido_detalle_id and c.auth_user_id=(select auth.uid())));
create policy cliente_pagos on public.pagos for select to authenticated using (
 exists(select 1 from public.compradores c where c.id=pagos.comprador_id and c.auth_user_id=(select auth.uid())));
create policy cliente_aplicaciones on public.aplicaciones_pago for select to authenticated using (
 exists(select 1 from public.pagos pg join public.compradores c on c.id=pg.comprador_id
 where pg.id=aplicaciones_pago.pago_id and c.auth_user_id=(select auth.uid())));

-- Privilegios de API. RLS limita filas y operaciones.
grant select, insert, update, delete on public.compradores,public.perfiles,public.articulos,public.cajas,public.pedidos,public.pedido_detalles,public.cargos,public.pagos,public.aplicaciones_pago to authenticated;
-- No se conceden permisos anon a estas tablas.
