using Plateia.Application;
using Plateia.Domain;
using Plateia.Infrastructure;
using Plateia.Web.Infra;
using Plateia.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Web.Controllers;

public class EventosController(AppDbContext db, EventoService eventos, PagamentoService pagamentos) : BaseController
{
    public static readonly (string Chave, string Nome)[] Visoes = [("proximos", "Próximos"), ("rascunhos", "Rascunhos"), ("passados", "Passados"), ("todos", "Todos")];

    [HttpGet("/")]
    public async Task<IActionResult> Index(string? ver)
    {
        ver = Visoes.Any(v => v.Chave == ver) ? ver! : "proximos";
        var agora = DateTime.UtcNow.AddHours(-6);
        var consulta = eventos.Visiveis(Eu);
        ViewBag.Contagem = new Dictionary<string, int>
        {
            ["proximos"] = await consulta.CountAsync(e => e.Status == StatusEvento.Publicado && (e.FimEm ?? e.InicioEm) >= agora),
            ["rascunhos"] = await consulta.CountAsync(e => e.Status == StatusEvento.Rascunho),
            ["passados"] = await consulta.CountAsync(e => e.Status != StatusEvento.Rascunho && (e.Status != StatusEvento.Publicado || (e.FimEm ?? e.InicioEm) < agora)),
            ["todos"] = await consulta.CountAsync()
        };
        ViewBag.Ver = ver;
        consulta = ver switch
        {
            "proximos" => consulta.Where(e => e.Status == StatusEvento.Publicado && (e.FimEm ?? e.InicioEm) >= agora),
            "rascunhos" => consulta.Where(e => e.Status == StatusEvento.Rascunho),
            "passados" => consulta.Where(e => e.Status != StatusEvento.Rascunho && (e.Status != StatusEvento.Publicado || (e.FimEm ?? e.InicioEm) < agora)),
            _ => consulta
        };
        var lista = await consulta
            .OrderBy(e => e.Status == StatusEvento.Encerrado || e.Status == StatusEvento.Cancelado)
            .ThenBy(e => e.InicioEm)
            .Select(e => new CartaoEvento(e.Id, e.Titulo, e.Slug, e.Local, e.InicioEm, e.Capacidade, e.Cor, e.Status,
                e.Organizador!.Nome,
                e.Inscricoes.Count(i => i.Status == StatusInscricao.Confirmada),
                e.Inscricoes.Count(i => i.Status == StatusInscricao.Confirmada && i.Pagamento == StatusPagamento.Pago),
                e.Inscricoes.Count(i => i.Status == StatusInscricao.Confirmada && i.Pagamento == StatusPagamento.Pendente),
                e.Inscricoes.Count(i => i.Status == StatusInscricao.Confirmada && i.CheckinEm != null)))
            .ToListAsync();
        return View(lista);
    }

    [HttpGet("/eventos/novo")]
    public async Task<IActionResult> Novo()
    {
        var hoje = Hora.HojeLocal;
        var e = new Evento
        {
            InicioEm = Hora.ParaUtc(hoje.AddDays(14).AddHours(19)), Capacidade = 100, Status = StatusEvento.Rascunho,
            Cor = Texto.Cores[Random.Shared.Next(Texto.Cores.Length - 1)],
            Tipos = [new TipoIngresso { Nome = "Inteira", Preco = 0 }]
        };
        await CarregarOrganizadores();
        return View("Formulario", e);
    }

    [HttpGet("/eventos/{id:int}/editar")]
    public async Task<IActionResult> Editar(int id)
    {
        var e = await eventos.ObterAsync(id, Eu);
        if (e is null) return NotFound();
        e.Tipos = e.Tipos.Where(t => t.Ativo).OrderBy(t => t.Ordem).ToList();
        await CarregarOrganizadores();
        return View("Formulario", e);
    }

    [HttpPost("/eventos/novo")]
    public async Task<IActionResult> Criar() => await Salvar(null);

    [HttpPost("/eventos/{id:int}/editar")]
    public async Task<IActionResult> Atualizar(int id) => await Salvar(id);

    async Task<IActionResult> Salvar(int? id)
    {
        var f = Request.Form;
        DateTime? Data(string k) => DateTime.TryParse(f[k], out var d) ? Hora.ParaUtc(d) : null;
        decimal Dec(string? s) => decimal.TryParse((s ?? "").Replace("R$", "").Trim(), System.Globalization.NumberStyles.Number, Hora.PtBr, out var v) ? v : 0;

        var ids = f["tipo_id"]; var nomes = f["tipo_nome"]; var descs = f["tipo_desc"];
        var precos = f["tipo_preco"]; var qtds = f["tipo_qtd"]; var remover = f["tipo_remover"]; var ates = f["tipo_ate"];
        var tipos = new List<DadosTipo>();
        for (var i = 0; i < nomes.Count; i++)
            tipos.Add(new DadosTipo(
                int.TryParse(ids.ElementAtOrDefault(i), out var tid) ? tid : null,
                nomes[i] ?? "", descs.ElementAtOrDefault(i), Dec(precos.ElementAtOrDefault(i)),
                int.TryParse(qtds.ElementAtOrDefault(i), out var q) ? q : null,
                remover.ElementAtOrDefault(i) == "1",
                DateTime.TryParse(ates.ElementAtOrDefault(i), out var ate) ? Hora.ParaUtc(ate) : null));

        var inicio = Data("inicio") ?? throw new RegraNegocioException("Informe a data e a hora de início.");
        var dados = new DadosEvento(
            f["titulo"]!, f["subtitulo"], f["descricao"], f["local"]!, f["endereco"], inicio, Data("fim"),
            int.TryParse(f["capacidade"], out var cap) ? cap : 0, f["cor"]!,
            Enum.TryParse<StatusEvento>(f["status"], out var st) ? st : StatusEvento.Rascunho,
            f["inscricoes_abertas"] == "true", f["instrucoes"],
            int.TryParse(f["organizador"], out var org) ? org : null, tipos);

        var e = await eventos.SalvarAsync(id, dados, Eu);
        TempData["msg"] = id is null ? "Evento criado." : "Alterações salvas.";
        return Redirect($"/eventos/{e.Id}");
    }

    [HttpGet("/eventos/{id:int}")]
    public async Task<IActionResult> Painel(int id)
    {
        var e = await eventos.ObterAsync(id, Eu);
        if (e is null) return NotFound();
        ViewBag.Online = pagamentos.Online;
        return View(await ResumoEvento.CarregarAsync(db, e));
    }

    /// <summary>Feed "ao vivo" do painel (atualizado a cada 30 s enquanto a aba está visível).</summary>
    [HttpGet("/eventos/{id:int}/ao-vivo")]
    public async Task<IActionResult> AoVivo(int id)
    {
        var e = await eventos.ObterAsync(id, Eu);
        if (e is null) return NotFound();
        return PartialView("_AoVivo", (await ResumoEvento.CarregarAsync(db, e)).Recentes(8));
    }

    [HttpPost("/eventos/{id:int}/duplicar")]
    public async Task<IActionResult> Duplicar(int id)
    {
        var e = await eventos.DuplicarAsync(id, Eu);
        TempData["msg"] = "Cópia criada como rascunho. Ajuste a data e publique.";
        return Redirect($"/eventos/{e.Id}/editar");
    }

    [HttpPost("/eventos/{id:int}/excluir")]
    public async Task<IActionResult> Excluir(int id)
    {
        await eventos.ExcluirAsync(id, Eu);
        TempData["msg"] = "Evento excluído.";
        return Redirect("/");
    }

    async Task CarregarOrganizadores()
    {
        ViewBag.Organizadores = Eu.Admin
            ? await db.Usuarios.Where(u => u.Ativo).OrderBy(u => u.Nome).Select(u => new OpcaoOrganizador(u.Id, u.Nome)).ToListAsync()
            : null;
    }
}
