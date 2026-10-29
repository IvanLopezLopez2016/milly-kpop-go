# Alcance confirmado — GO

## Fuente
Master List Milly K-Pop (PDF aportado por el usuario; 12 páginas).

## Reglas confirmadas
- ADMIN es quien crea e invita a clientes; no existe autorregistro.
- CLIENTE puede ver exclusivamente pedidos propios, estados, cargos, abonos y saldos.
- Artículos se registran una vez y se reutilizan en los pedidos.
- Un comprador puede tener muchos artículos y participar en varias cajas.
- Precio histórico por línea, independiente del catálogo.
- El estado logístico es por línea de pedido; una caja puede agrupar productos de varios compradores.
- Abonos y cargos de EMS/aduana separados; pagos pueden exceder un producto y convertirse en saldo a favor.
- Stock no es un comprador; debe modelarse como inventario.

## Terminología del PDF
Estados: EN ESPERA, PEDIDO, DIRECCION COREA, DIRECCION JAPON, DIRECCION USA, COTIZANDO ENVIO, ENVIADO, ADUANA, EN MEXICO, LISTO PARA ENTREGAR, ENTREGADO/COMPLETADO, STOCK, MULTA, CANCELADO.

Tipos de entrega: ENVIO, PERSONAL, URGENTE KA, URGENTE MX, ENVIO CON ALMACEN.

## Pendientes de negocio (no suponer)
- Método de prorrateo de EMS, aduana y cruces.
- Si el saldo a favor puede usarse entre pedidos automáticamente o requiere autorización manual.
- Método de devolución y tratamiento de pagos en exceso.
- Cuándo se bloquea una entrega por adeudo.
- Quién puede consultar notas internas: se reservan solo a ADMIN.

## Entrega 001
Incluye SQL inicial, RLS y generador de una demo Blazor. No implementa datos reales ni flujos de autenticación. El orden de trabajo siguiente es Auth -> CRUD -> pedidos -> logística -> pagos -> portal cliente -> importación y publicación.
