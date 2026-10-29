using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace MillyKpopGo.Pedidos;

public static class PedidosEndpoints
{
    private static readonly JsonSerializerOptions JsonOpt = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    public static IEndpointRouteBuilder MapPedidosEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/admin/pedidos").RequireAuthorization(new AuthorizeAttribute { Roles = "ADMIN" });
        // GET migrado a componentes Razor en entrega 008.4
        // GET migrado a componentes Razor en entrega 008.4
        g.MapPost("/guardar", Guardar).DisableAntiforgery(); // validación explícita
        // GET migrado a componentes Razor en entrega 008.4
        return app;
    }

    private static async Task<IResult> Nuevo(HttpContext ctx, IHttpClientFactory factory, IConfiguration config, IAntiforgery anti)
    {
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) return Results.Redirect("/logout");
        using var http = Client(factory, config, jwt);
        var compradores = await Fetch<List<Comprador>>(http, "compradores?select=id,nombre,alias,activo&order=nombre.asc&limit=1000", ctx.RequestAborted);
        var articulos = await Fetch<List<Articulo>>(http, "articulos?select=id,nombre,sku,variante,precio_referencia,activo&order=nombre.asc&limit=1000", ctx.RequestAborted);
        if (compradores is null || articulos is null) return Error("No se pudieron consultar compradores o artículos. Revisa tu sesión y permisos.", 502);
        compradores = compradores.Where(x => x.Activo).ToList();
        articulos = articulos.Where(x => x.Activo).ToList();
        if (compradores.Count == 0 || articulos.Count == 0)
            return Html(Page("Nuevo pedido", "<section class='go-box'><p>Para crear un pedido necesitas al menos un comprador y un artículo activos.</p><p><a href='/admin/compradores'>Compradores</a> · <a href='/admin/articulos'>Artículos</a></p></section>"));
        var csrf = anti.GetAndStoreTokens(ctx).RequestToken ?? "";
        var h = new StringBuilder();
        h.Append("<section class='go-panel'><h1>Nuevo pedido</h1><p>Selecciona un comprador y agrega hasta 30 renglones del catálogo. Los precios se guardan en el pedido y no modifican el catálogo.</p>");
        h.Append("<form action='/admin/pedidos/guardar' method='post' id='form-pedido' class='go-box go-order-form'>");
        h.Append("<input type='hidden' name='__RequestVerificationToken' value='").Append(E(csrf)).Append("'>");
        h.Append("<label>Comprador *<select name='comprador_id' required><option value=''>Seleccionar comprador</option>");
        foreach(var c in compradores) h.Append("<option value='").Append(c.Id).Append("'>").Append(E(c.Nombre)).Append(string.IsNullOrWhiteSpace(c.Alias)?"":" · "+E(c.Alias)).Append("</option>");
        h.Append("</select></label><label>Nota interna (opcional)<textarea name='nota_interna' maxlength='1000' rows='2'></textarea></label>");
        h.Append("<h2>Artículos del pedido</h2><div id='renglones'></div><button type='button' class='go-btn-secondary' id='agregar'>+ Agregar artículo</button>");
        h.Append("<p><strong>Total estimado: <span id='total'>$0.00</span></strong></p><button type='submit'>Guardar pedido</button> <a href='/admin/pedidos'>Cancelar</a></form></section>");
        // IDs y precios provienen del catálogo de Supabase. JSON se inyecta como texto seguro
        // evitando que los nombres de productos se conviertan en HTML o JavaScript ejecutable.
        var options = articulos.Select(a => new { id = a.Id.ToString("D"), nombre = a.Nombre ?? "", variante = a.Variante ?? "", sku = a.Sku ?? "", precio = a.PrecioReferencia });
        var json = JsonSerializer.Serialize(options, JsonOpt).Replace("<", "\\u003c").Replace(">", "\\u003e").Replace("&", "\\u0026");
        h.Append("<script type='application/json' id='catalogo-json'>").Append(json).Append("</script>");
        h.Append("""
        <script>
        (() => {
          const articulos = JSON.parse(document.querySelector('#catalogo-json').textContent);
          const lista = document.querySelector('#renglones');
          const total = document.querySelector('#total');
          const form = document.querySelector('#form-pedido');
          const actualizar = () => {
            let suma = 0;
            for (const row of lista.children) {
              const cantidad = Number(row.querySelector('.cantidad').value);
              const precio = Number(row.querySelector('.precio').value);
              if (Number.isFinite(cantidad) && Number.isFinite(precio)) suma += cantidad * precio;
            }
            total.textContent = suma.toLocaleString('es-MX',{style:'currency',currency:'MXN'});
          };
          document.querySelector('#agregar').addEventListener('click', () => {
            if(lista.children.length >= 30) return;
            const usados = new Set(Array.from(lista.children).map(x=>Number(x.dataset.indice)));
            let i = 0; while(usados.has(i)) i++;
            const row = document.createElement('div');
            row.className = 'go-order-row'; row.dataset.indice = String(i);
            const artLabel = document.createElement('label'); artLabel.textContent = 'Artículo';
            const sel = document.createElement('select'); sel.name = `articulo_${i}`; sel.required = true;
            const placeholder = new Option('Seleccionar artículo',''); sel.add(placeholder);
            for (const a of articulos) sel.add(new Option(`${a.nombre}${a.variante?' · '+a.variante:''}${a.sku?' ['+a.sku+']':''}`,a.id));
            artLabel.append(sel);
            const qLabel = document.createElement('label'); qLabel.textContent='Cantidad';
            const q = document.createElement('input'); q.type='number'; q.name=`cantidad_${i}`; q.min='1'; q.max='999'; q.step='1'; q.required=true; q.value='1'; q.className='cantidad'; qLabel.append(q);
            const pLabel = document.createElement('label'); pLabel.textContent='Precio unitario (MXN)';
            const p = document.createElement('input'); p.type='number'; p.name=`precio_${i}`; p.min='0'; p.max='9999999999.99'; p.step='0.01'; p.required=true; p.className='precio'; pLabel.append(p);
            const borrar = document.createElement('button'); borrar.type='button'; borrar.textContent='Quitar'; borrar.className='go-btn-secondary'; borrar.addEventListener('click',()=>{row.remove();actualizar();});
            row.append(artLabel,qLabel,pLabel,borrar); lista.append(row);
            sel.addEventListener('change',()=>{ const a=articulos.find(x=>x.id===sel.value); p.value=a?.precio?.toFixed(2) ?? '0.00'; actualizar(); });
            q.addEventListener('input',actualizar); p.addEventListener('input',actualizar);
            sel.selectedIndex=1; sel.dispatchEvent(new Event('change'));
          });
          form.addEventListener('submit',ev=>{if(lista.children.length===0){ev.preventDefault();alert('Agrega al menos un artículo.');}});
          document.querySelector('#agregar').click();
        })();
        </script>
        """);
        return Html(Page("Nuevo pedido", h.ToString()));
    }

    private static async Task<IResult> Guardar(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, IAntiforgery anti)
    {
        try { await anti.ValidateRequestAsync(ctx); }
        catch (AntiforgeryValidationException) { return Results.BadRequest("Solicitud inválida (CSRF)."); }
        var f = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        if (!Guid.TryParse(f["comprador_id"].ToString(), out var compradorId)) return Error("Selecciona un comprador.", 400);
        var nota = f["nota_interna"].ToString().Trim();
        if (nota.Length > 1000) return Error("Nota demasiado larga.", 400);
        var items = new List<object>();
        for (int i = 0; i < 30; i++)
        {
            var articuloText = f[$"articulo_{i}"].ToString();
            if (string.IsNullOrEmpty(articuloText)) continue;
            if (!Guid.TryParse(articuloText, out var id)) return Error("Identificador de artículo inválido.", 400);
            if (!int.TryParse(f[$"cantidad_{i}"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cantidad) || cantidad is < 1 or > 999)
                return Error("La cantidad debe estar entre 1 y 999.", 400);
            if (!decimal.TryParse(f[$"precio_{i}"].ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var precio) || precio < 0 || precio > 9999999999.99m || decimal.Round(precio,2) != precio)
                return Error("Precio inválido: usa hasta dos decimales.", 400);
            items.Add(new { articulo_id=id, cantidad, precio_unitario=precio });
        }
        if (items.Count == 0) return Error("Agrega al menos un artículo.", 400);
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) return Results.Redirect("/logout");
        using var http = Client(factory, cfg, jwt);
        using var response = await http.PostAsJsonAsync("rpc/crear_pedido_go", new { p_comprador_id=compradorId, p_nota_interna=nota, p_items=items }, JsonOpt, ctx.RequestAborted);
        if (!response.IsSuccessStatusCode)
            return Error("No se pudo guardar el pedido. Comprueba que ejecutaste 005_crear_pedido.sql y que comprador y artículos estén activos. HTTP " + (int)response.StatusCode, 400);
        var raw = await response.Content.ReadAsStringAsync(ctx.RequestAborted);
        try
        {
            var id = JsonSerializer.Deserialize<Guid>(raw);
            return Results.Redirect("/admin/pedidos/" + id.ToString("D"));
        }
        catch(JsonException) { return Error("Supabase respondió sin un identificador válido.", 502); }
    }

    private static async Task<IResult> Lista(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, string? q = null)
    {
        var jwt = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) return Results.Redirect("/logout");
        using var http = Client(factory,cfg,jwt);
        var buyers = await Fetch<List<Comprador>>(http, "compradores?select=id,nombre,alias,activo&limit=1000",ctx.RequestAborted);
        var orders = await Fetch<List<Pedido>>(http,"pedidos?select=id,comprador_id,fecha,nota_interna&order=fecha.desc&limit=500",ctx.RequestAborted);
        if (buyers is null || orders is null) return Error("No fue posible obtener los pedidos.",502);
        var names = buyers.ToDictionary(x=>x.Id, x=>x.Nombre ?? "Comprador");
        var filtro = (q??"").Trim();
        if (filtro.Length > 0)
            orders=orders.Where(o=>names.GetValueOrDefault(o.CompradorId,"").Contains(filtro,StringComparison.OrdinalIgnoreCase) || o.Id.ToString("D").Contains(filtro,StringComparison.OrdinalIgnoreCase)).ToList();
        var h = new StringBuilder("<section class='go-panel'><h1>Pedidos</h1><p>Encargos de los compradores registrados.</p><p><a href='/admin/pedidos/nuevo' class='go-btn-secondary'>+ Nuevo pedido</a></p>");
        h.Append("<form method='get' action='/admin/pedidos' class='go-search'><input name='q' placeholder='Nombre del comprador o ID del pedido' value='").Append(E(filtro)).Append("'><button>Buscar</button><a class='go-btn-secondary' href='/admin/pedidos'>Limpiar</a></form>");
        h.Append("<div class='go-box'><h2>Pedidos registrados (mostrando ").Append(orders.Count).Append(")</h2>");
        if (orders.Count == 0) h.Append("<p>No hay pedidos para mostrar.</p>");
        else
        {
            h.Append("<div class='go-table-wrap'><table class='go-table'><thead><tr><th>Comprador</th><th>Fecha</th><th>Pedido</th><th></th></tr></thead><tbody>");
            foreach(var o in orders)
                h.Append("<tr><td>").Append(E(names.GetValueOrDefault(o.CompradorId,"Comprador desconocido"))).Append("</td><td>").Append(o.Fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm")).Append("</td><td><small>").Append(o.Id.ToString("D")[..8]).Append("</small></td><td><a href='/admin/pedidos/").Append(o.Id.ToString("D")).Append("'>Ver detalle</a></td></tr>");
            h.Append("</tbody></table></div>");
        }
        h.Append("</div></section>");
        return Html(Page("Pedidos",h.ToString()));
    }

    private static async Task<IResult> Detalle(Guid id, HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg)
    {
        var jwt=await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(jwt)) return Results.Redirect("/logout");
        using var http=Client(factory,cfg,jwt);
        var order=await Fetch<List<Pedido>>(http,$"pedidos?select=id,comprador_id,fecha,nota_interna&id=eq.{id:D}&limit=1",ctx.RequestAborted);
        if (order is null) return Error("No se pudo consultar el pedido.",502);
        if(order.Count==0) return Results.NotFound();
        var d=await Fetch<List<DetallePedido>>(http,$"pedido_detalles?select=id,articulo_id,cantidad,precio_unitario,estado&pedido_id=eq.{id:D}&limit=100",ctx.RequestAborted);
        var arts=await Fetch<List<Articulo>>(http,"articulos?select=id,nombre,sku,variante,precio_referencia,activo&limit=1000",ctx.RequestAborted);
        var buyers=await Fetch<List<Comprador>>(http,$"compradores?select=id,nombre,alias,activo&id=eq.{order[0].CompradorId:D}&limit=1",ctx.RequestAborted);
        if(d is null || arts is null || buyers is null) return Error("No se pudieron consultar los detalles del pedido.",502);
        var map=arts.ToDictionary(a=>a.Id,a=>a);
        var h=new StringBuilder("<section class='go-panel'><h1>Detalle del pedido</h1>");
        h.Append("<p><strong>Comprador:</strong> ").Append(E(buyers.FirstOrDefault()?.Nombre)).Append("</p><p><strong>Fecha:</strong> ").Append(order[0].Fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm")).Append("</p>");
        if(!string.IsNullOrWhiteSpace(order[0].NotaInterna)) h.Append("<p><strong>Nota interna:</strong> ").Append(E(order[0].NotaInterna)).Append("</p>");
        h.Append("<div class='go-box go-table-wrap'><table class='go-table'><thead><tr><th>Artículo</th><th>Cantidad</th><th>Precio unitario</th><th>Subtotal</th><th>Estado</th></tr></thead><tbody>");
        decimal total=0;
        foreach(var x in d)
        {
            var sub=x.Cantidad*x.PrecioUnitario; total+=sub;
            h.Append("<tr><td>").Append(E(map.GetValueOrDefault(x.ArticuloId)?.Nombre ?? "Artículo no encontrado")).Append("</td><td>").Append(x.Cantidad)
             .Append("</td><td>").Append(Money(x.PrecioUnitario)).Append("</td><td>").Append(Money(sub)).Append("</td><td>").Append(E(x.Estado)).Append("</td></tr>");
        }
        h.Append("</tbody></table><p><strong>Total de artículos: ").Append(Money(total)).Append("</strong></p></div><p><a href='/admin/pedidos'>Volver a pedidos</a></p></section>");
        return Html(Page("Detalle del pedido",h.ToString()));
    }

    private static async Task<T?> Fetch<T>(HttpClient http, string url, CancellationToken ct)
    {
        try { using var response=await http.GetAsync(url,ct); if(!response.IsSuccessStatusCode) return default; return await response.Content.ReadFromJsonAsync<T>(JsonOpt,ct); }
        catch (Exception ex) when(ex is HttpRequestException or JsonException or TaskCanceledException) { return default; }
    }
    private static HttpClient Client(IHttpClientFactory factory,IConfiguration cfg,string jwt)
    {
        var root=cfg["Supabase:Url"]?.TrimEnd('/') ?? throw new InvalidOperationException("Falta Supabase:Url");
        var key=cfg["Supabase:PublishableKey"] ?? throw new InvalidOperationException("Falta Supabase:PublishableKey");
        var http=factory.CreateClient();
        http.BaseAddress=new Uri(root+"/rest/v1/");
        http.DefaultRequestHeaders.Add("apikey",key);
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",jwt);
        http.Timeout=TimeSpan.FromSeconds(25);
        return http;
    }
    private static IResult Error(string message,int code=400)=>Html(Page("Error en pedidos","<div class='go-warning'>"+E(message)+"</div><p><a href='/admin/pedidos'>Volver a pedidos</a></p>"),code);
    private static IResult Html(string text,int code=200)=>Results.Content(text,"text/html; charset=utf-8",statusCode:code);
    private static string E(string? v)=>WebUtility.HtmlEncode(v??"");
    private static string Money(decimal value)=>"$"+value.ToString("N2",Mx);
    private static string Page(string title,string body)=>$$"""
    <!doctype html><html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
    <title>{{E(title)}} · Milly K-Pop GO</title><link rel="stylesheet" href="/go.css"><link rel="stylesheet" href="/compradores.css">
    <link rel="stylesheet" href="/pedidos.css"></head>
    <body><main class="go-shell"><header class="go-header"><div><small>MILLY K-POP GO · ADMIN</small><h1>{{E(title)}}</h1></div><nav><a href="/admin">Inicio</a> · <a href="/admin/compradores">Compradores</a> · <a href="/admin/articulos">Artículos</a> · <a href="/logout">Salir</a></nav></header>{{body}}</main></body></html>
    """;
    private sealed class Comprador { [JsonPropertyName("id")] public Guid Id {get;set;} [JsonPropertyName("nombre")] public string? Nombre {get;set;} [JsonPropertyName("alias")] public string? Alias {get;set;} [JsonPropertyName("activo")] public bool Activo {get;set;} }
    private sealed class Articulo { [JsonPropertyName("id")] public Guid Id {get;set;} [JsonPropertyName("nombre")] public string? Nombre {get;set;} [JsonPropertyName("sku")] public string? Sku {get;set;} [JsonPropertyName("variante")] public string? Variante {get;set;} [JsonPropertyName("precio_referencia")] public decimal? PrecioReferencia {get;set;} [JsonPropertyName("activo")] public bool Activo {get;set;} }
    private sealed class Pedido { [JsonPropertyName("id")] public Guid Id {get;set;} [JsonPropertyName("comprador_id")] public Guid CompradorId {get;set;} [JsonPropertyName("fecha")] public DateTimeOffset Fecha {get;set;} [JsonPropertyName("nota_interna")] public string? NotaInterna {get;set;} }
    private sealed class DetallePedido { [JsonPropertyName("id")] public Guid Id {get;set;} [JsonPropertyName("articulo_id")] public Guid ArticuloId {get;set;} [JsonPropertyName("cantidad")] public int Cantidad {get;set;} [JsonPropertyName("precio_unitario")] public decimal PrecioUnitario {get;set;} [JsonPropertyName("estado")] public string? Estado {get;set;} }
}
