using System.Security.Claims;
using System.Text;
using Plateia.Application;
using Plateia.Domain;
using Plateia.Infrastructure;
using Plateia.Web.Infra;
using Plateia.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Web.Controllers;

/// <summary>Páginas abertas ao público: agenda, página do evento, inscrição, pagamento e ingresso.</summary>
[AllowAnonymous]
public class PublicoController(AppDbContext db, InscricaoService inscricoes, PagamentoService pagamentos, DivulgacaoService divulgacao,
    CatalogoService catalogo, ILogger<PublicoController> log) : BaseController
{
    [HttpGet("/")]
    public async Task<IActionResult> Inicio()
    {
        var (semanaDe, semanaAte) = Periodo("semana")!.Value;
        return View(new PaginaInicio
        {
            Destaques = await catalogo.DestaquesAsync(),
            EstaSemana = await catalogo.BuscarAsync(new FiltroBusca(DeUtc: semanaDe, AteUtc: semanaAte), 8),
            Proximos = await catalogo.BuscarAsync(new FiltroBusca(), 12),
            Online = await catalogo.BuscarAsync(new FiltroBusca(SoOnline: true), 4),
            Categorias = await catalogo.CategoriasAsync(),
            Cidades = await catalogo.CidadesAsync(),
            Total = await catalogo.TotalAsync()
        });
    }

    [HttpGet("/eventos")]
    public async Task<IActionResult> Busca(string? q, string? categoria, string? cidade, string? quando, string? online, string? gratis)
    {
        var cat = Texto.CategoriaDoSlug(categoria);
        var periodo = Periodo(quando);
        var soOnline = online == "1";
        var soGratis = gratis == "1";
        return View(new PaginaBusca
        {
            Itens = await catalogo.BuscarAsync(new FiltroBusca(q, cat, cidade, periodo?.De, periodo?.Ate, soOnline, soGratis)),
            Texto = q, Categoria = cat, Cidade = cidade, Quando = periodo is null ? null : quando, Online = soOnline, Gratis = soGratis,
            Categorias = await catalogo.CategoriasAsync(), Cidades = await catalogo.CidadesAsync()
        });
    }

    [HttpGet("/agenda")]
    public IActionResult Agenda() => RedirectPermanent("/eventos");

    [HttpGet("/p/{slug}")]
    public async Task<IActionResult> Produtor(string slug)
    {
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Slug == slug && x.Ativo && x.Aprovado && x.Perfil != Perfil.Participante);
        if (u is null) return NotFound();
        return View(new PaginaProdutor
        {
            Produtor = u,
            Eventos = await catalogo.DoProdutorAsync(u.Id),
            Realizados = await db.Eventos.CountAsync(e => e.OrganizadorId == u.Id && e.Status != StatusEvento.Rascunho && e.InicioEm < DateTime.UtcNow)
        });
    }

    /// <summary>Converte "hoje", "fim-de-semana", "semana" e "mes" em intervalo UTC (no horário de Brasília).</summary>
    static (DateTime De, DateTime Ate)? Periodo(string? quando)
    {
        var hoje = Hora.HojeLocal;
        (DateTime, DateTime)? local = quando switch
        {
            "hoje" => (hoje, hoje.AddDays(1)),
            "amanha" => (hoje.AddDays(1), hoje.AddDays(2)),
            "fim-de-semana" => FimDeSemana(hoje),
            "semana" => (hoje, hoje.AddDays(7)),
            "mes" => (hoje, hoje.AddDays(30)),
            _ => null
        };
        return local is { } l ? (Hora.ParaUtc(l.Item1), Hora.ParaUtc(l.Item2)) : null;

        static (DateTime, DateTime) FimDeSemana(DateTime d)
        {
            var sabado = d.AddDays(((int)DayOfWeek.Saturday - (int)d.DayOfWeek + 7) % 7);
            if (d.DayOfWeek == DayOfWeek.Sunday) sabado = d.AddDays(-1);
            return (sabado < d ? d : sabado, sabado.AddDays(2));
        }
    }

    [HttpGet("/e/{slug}")]
    public async Task<IActionResult> Evento(string slug, string? origem, [FromQuery(Name = "utm_source")] string? utm)
    {
        var pagina = await Montar(slug);
        if (pagina is null) return NotFound();
        pagina.Canal = await RegistrarCanal(pagina, origem ?? utm);
        pagina.Relacionados = await catalogo.RelacionadosAsync(pagina.Evento);
        return View(pagina);
    }

    [HttpGet("/e/{slug}/cupom")]
    public async Task<IActionResult> Cupom(string slug, string? codigo)
    {
        var ev = await db.Eventos.FirstOrDefaultAsync(e => e.Slug == slug);
        var c = ev is null ? null : await divulgacao.CupomValidoAsync(ev.Id, codigo);
        return Json(c is null
            ? new { valido = false, texto = "Cupom inválido ou esgotado.", tipo = "", valor = 0m }
            : new { valido = true, texto = c.Tipo == TipoDesconto.Percentual ? $"{c.Valor:0.##}% de desconto aplicado" : $"{Hora.Dinheiro(c.Valor)} de desconto aplicado",
                    tipo = c.Tipo.ToString(), valor = c.Valor });
    }

    /// <summary>
    /// Checkout único: quem compra informa os próprios dados e o nome (e, se quiser, e-mail e WhatsApp) de cada participante.
    /// Um ingresso só vai direto para o ingresso; dois ou mais vão para a página do pedido.
    /// </summary>
    [HttpPost("/e/{slug}"), EnableRateLimiting("publico")]
    public async Task<IActionResult> Inscrever(string slug, string nome, string email, string? telefone, List<ParticipanteForm>? p, string? cupom, string? canal)
    {
        var pagina = await Montar(slug);
        if (pagina is null) return NotFound();
        var participantes = (p ?? []).Where(x => x.TipoId > 0).ToList();
        try
        {
            var itens = participantes.Select(x => x.Meu
                ? new ParticipanteCheckout(x.TipoId, nome, email, telefone, x.Meia)
                : new ParticipanteCheckout(x.TipoId, x.Nome, x.Email, x.Telefone, x.Meia)).ToList();
            var pedido = await inscricoes.CheckoutAsync(pagina.Evento.Id, nome, email, telefone, itens, cupom, canal);
            return pedido.Inscricoes.Count == 1
                ? Redirect($"/ingresso/{pedido.Inscricoes[0].Codigo}?novo=1")
                : Redirect($"/pedido/{pedido.Codigo}?novo=1");
        }
        catch (RegraNegocioException ex)
        {
            ViewBag.Erro = ex.Message;
            ViewBag.Form = new FormCheckout(nome, email, telefone, cupom, participantes);
            var pg = await Montar(slug);
            pg!.Canal = canal;
            return View("Evento", pg);
        }
    }

    [HttpGet("/pedido/{codigo}")]
    public async Task<IActionResult> Pedido(string codigo)
    {
        var p = await CarregarPedido(codigo);
        if (p is null) return NotFound();
        if (p.APagar > 0 && p.CieloLinkId is not null)
        {
            try { await pagamentos.SincronizarAsync(p); }
            catch (Exception ex) { log.LogWarning(ex, "Falha ao consultar a Cielo para o pedido {Codigo}", p.Codigo); }
        }
        ViewBag.Pix = pagamentos.Pix;
        ViewBag.Cartao = pagamentos.Cartao;
        return View(p);
    }

    /// <summary>Leva para a página de pagamento da Cielo com o total do pedido (o link é criado na primeira vez).</summary>
    [HttpPost("/pedido/{codigo}/pagar"), EnableRateLimiting("publico")]
    public async Task<IActionResult> PagarPedido(string codigo)
    {
        var p = await CarregarPedido(codigo);
        if (p is null) return NotFound();
        try { return Redirect(await pagamentos.UrlPagamentoAsync(p)); }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Cielo indisponível ao gerar link do pedido {Codigo}", p.Codigo);
            throw new RegraNegocioException("A Cielo não respondeu agora. Tente de novo em instantes.");
        }
    }

    /// <summary>Gera o Pix do pedido (QR Code + copia e cola) na própria página.</summary>
    [HttpPost("/pedido/{codigo}/pix"), EnableRateLimiting("publico")]
    public async Task<IActionResult> GerarPix(string codigo, string? cpf)
    {
        var p = await CarregarPedido(codigo);
        if (p is null) return NotFound();
        try { await pagamentos.GerarPixAsync(p, cpf); }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Cielo indisponível ao gerar Pix do pedido {Codigo}", p.Codigo);
            throw new RegraNegocioException("A Cielo não respondeu agora. Tente de novo em instantes.");
        }
        return Redirect($"/pedido/{p.Codigo}#pagamento");
    }

    /// <summary>A página do pedido pergunta a cada poucos segundos se o Pix já caiu (a consulta à Cielo é limitada a uma a cada 5 s).</summary>
    [HttpGet("/pedido/{codigo}/situacao")]
    public async Task<IActionResult> SituacaoPedido(string codigo)
    {
        var p = await CarregarPedido(codigo);
        if (p is null) return NotFound();
        if (p.APagar > 0)
        {
            try { await pagamentos.SincronizarAsync(p); }
            catch (Exception ex) { log.LogWarning(ex, "Falha ao consultar a Cielo para o pedido {Codigo}", p.Codigo); }
        }
        return Json(new { pago = p.APagar <= 0 });
    }

    Task<Pedido?> CarregarPedido(string codigo)
    {
        var c = Utilidades.NormalizarCodigo(codigo);
        return db.Pedidos.Include(x => x.Evento).Include(x => x.Inscricoes).ThenInclude(i => i.TipoIngresso).ThenInclude(t => t!.Espaco)
            .FirstOrDefaultAsync(x => x.Codigo == c);
    }

    [HttpGet("/ingresso/{codigo}")]
    public async Task<IActionResult> Ingresso(string codigo)
    {
        var i = await Carregar(codigo);
        if (i is null) return NotFound();
        // Quem volta da página da Cielo vê a situação atualizada (consulta limitada a uma vez por minuto).
        if (i.Ativa && i.Pagamento == StatusPagamento.Pendente && (i.CieloLinkId is not null || i.PedidoId is not null))
        {
            try { await pagamentos.SincronizarAsync(i); }
            catch (Exception ex) { log.LogWarning(ex, "Falha ao consultar a Cielo para o ingresso {Codigo}", i.Codigo); }
        }
        ViewBag.Link = $"{Request.Scheme}://{Request.Host}/ingresso/{i.Codigo}";
        ViewBag.Online = i.PedidoId is null ? pagamentos.Cartao : pagamentos.Online;
        // Ingresso comprado junto com outros: o pagamento é do pedido inteiro.
        if (i.PedidoId is { } pid)
            ViewBag.Pedido = await db.Pedidos.Include(x => x.Inscricoes).FirstOrDefaultAsync(x => x.Id == pid);
        return View(i);
    }

    /// <summary>Leva para a página de pagamento da Cielo (o link é criado na primeira vez).</summary>
    [HttpPost("/ingresso/{codigo}/pagar"), EnableRateLimiting("publico")]
    public async Task<IActionResult> Pagar(string codigo)
    {
        var i = await Carregar(codigo);
        if (i is null) return NotFound();
        try { return Redirect(await pagamentos.UrlPagamentoAsync(i)); }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Cielo indisponível ao gerar link do ingresso {Codigo}", i.Codigo);
            throw new RegraNegocioException("A Cielo não respondeu agora. Tente de novo em instantes.");
        }
    }

    [HttpGet("/ingresso/{codigo}/agenda.ics")]
    public async Task<IActionResult> Calendario(string codigo)
    {
        var i = await Carregar(codigo);
        if (i is not { Ativa: true, Evento: { } e }) return NotFound();
        static string Esc(string? s) => (s ?? "").Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");
        static string Ut(DateTime d) => d.ToString("yyyyMMdd'T'HHmmss'Z'");
        var ics = new StringBuilder()
            .Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Plateia//PT-BR\r\nBEGIN:VEVENT\r\n")
            .Append($"UID:{i.Codigo}@plateia\r\nDTSTAMP:{Ut(DateTime.UtcNow)}\r\nDTSTART:{Ut(e.InicioEm)}\r\nDTEND:{Ut(e.FimEm ?? e.InicioEm.AddHours(2))}\r\n")
            .Append($"SUMMARY:{Esc(e.Titulo)}\r\nLOCATION:{Esc(string.Join(" - ", new[] { e.Local, e.Endereco }.Where(x => !string.IsNullOrEmpty(x))))}\r\n")
            .Append($"DESCRIPTION:{Esc($"Ingresso {i.Codigo}: {Request.Scheme}://{Request.Host}/ingresso/{i.Codigo}")}\r\n")
            .Append("END:VEVENT\r\nEND:VCALENDAR\r\n");
        return File(Encoding.UTF8.GetBytes(ics.ToString()), "text/calendar; charset=utf-8", $"{e.Slug}.ics");
    }

    Task<Inscricao?> Carregar(string codigo)
    {
        var c = Utilidades.NormalizarCodigo(codigo);
        return db.Inscricoes.Include(x => x.Evento).Include(x => x.TipoIngresso).ThenInclude(t => t!.Espaco).FirstOrDefaultAsync(x => x.Codigo == c);
    }

    /// <summary>
    /// Guarda de qual canal a pessoa veio (link ?origem=instagram) por 30 dias e conta a visita
    /// uma vez a cada 6 horas por navegador. Prévias de rascunho não contam.
    /// </summary>
    async Task<string?> RegistrarCanal(PaginaEvento p, string? origem)
    {
        var chaveCanal = $"pl_c{p.Evento.Id}";
        var canal = DivulgacaoService.NormalizarCanal(origem) ?? DivulgacaoService.NormalizarCanal(Request.Cookies[chaveCanal]);
        if (DivulgacaoService.NormalizarCanal(origem) is { } novo)
            Response.Cookies.Append(chaveCanal, novo, new CookieOptions { MaxAge = TimeSpan.FromDays(30), HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true });

        var chaveVisita = $"pl_v{p.Evento.Id}";
        if (!p.Previa && Request.Cookies[chaveVisita] is null)
        {
            Response.Cookies.Append(chaveVisita, "1", new CookieOptions { MaxAge = TimeSpan.FromHours(6), HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true });
            try { await divulgacao.RegistrarVisitaAsync(p.Evento.Id, canal, Hora.HojeLocal); }
            catch (Exception ex) { log.LogWarning(ex, "Falha ao contar visita"); }
        }
        return canal;
    }

    async Task<PaginaEvento?> Montar(string slug)
    {
        var e = await db.Eventos.Include(x => x.Tipos).Include(x => x.Espacos).Include(x => x.Organizador).FirstOrDefaultAsync(x => x.Slug == slug);
        if (e is null) return null;

        // Rascunho só aparece (como prévia) para o próprio organizador ou para o administrador.
        var previa = false;
        if (!e.Visivel)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var admin = User.IsInRole(nameof(Perfil.Administrador));
            if (uid is null || (!admin && uid != e.OrganizadorId.ToString())) return null;
            previa = true;
        }

        var resumo = await ResumoEvento.CarregarAsync(db, e);
        return new PaginaEvento
        {
            Evento = e, Tipos = resumo.PorTipo.Where(t => t.Ativo).ToList(), Livres = resumo.Livres, Previa = previa, Espacos = resumo.PorEspaco,
            PagamentoOnline = pagamentos.Online, TemCupons = await db.Cupons.AnyAsync(c => c.EventoId == e.Id && c.Ativo)
        };
    }
}
