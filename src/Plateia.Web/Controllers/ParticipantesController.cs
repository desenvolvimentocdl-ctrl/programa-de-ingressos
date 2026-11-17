using System.Text;
using Plateia.Application;
using Plateia.Domain;
using Plateia.Infrastructure;
using Plateia.Web.Infra;
using Plateia.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Web.Controllers;

public class ParticipantesController(AppDbContext db, EventoService eventos, InscricaoService inscricoes) : BaseController
{
    public static readonly string[] Filtros = ["todos", "pagos", "pendentes", "cortesia", "presentes", "cancelados"];

    [HttpGet("/eventos/{id:int}/participantes")]
    public async Task<IActionResult> Lista(int id, string? filtro, string? q)
    {
        var e = await eventos.ObterAsync(id, Eu);
        if (e is null) return NotFound();
        filtro = Filtros.Contains(filtro) ? filtro! : "todos";

        var consulta = db.Inscricoes.Include(i => i.TipoIngresso).Where(i => i.EventoId == id);
        consulta = filtro switch
        {
            "pagos" => consulta.Where(i => i.Status == StatusInscricao.Confirmada && i.Pagamento == StatusPagamento.Pago),
            "pendentes" => consulta.Where(i => i.Status == StatusInscricao.Confirmada && i.Pagamento == StatusPagamento.Pendente),
            "cortesia" => consulta.Where(i => i.Status == StatusInscricao.Confirmada && i.Pagamento == StatusPagamento.Cortesia),
            "presentes" => consulta.Where(i => i.Status == StatusInscricao.Confirmada && i.CheckinEm != null),
            "cancelados" => consulta.Where(i => i.Status == StatusInscricao.Cancelada),
            _ => consulta.Where(i => i.Status == StatusInscricao.Confirmada)
        };
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            var cod = Utilidades.NormalizarCodigo(t);
            consulta = consulta.Where(i => i.Nome.Contains(t) || i.Email.Contains(t) || (i.Telefone != null && i.Telefone.Contains(t)) || i.Codigo == cod);
        }

        return View(new ListaParticipantes
        {
            Resumo = await ResumoEvento.CarregarAsync(db, e),
            Itens = await consulta.OrderBy(i => i.Nome).Take(2000).ToListAsync(),
            Filtro = filtro, Busca = q
        });
    }

    [HttpPost("/eventos/{id:int}/participantes")]
    public async Task<IActionResult> Adicionar(int id, int tipo, string nome, string email, string? telefone, StatusPagamento pagamento, string? observacao)
    {
        if (await eventos.ObterAsync(id, Eu) is null) return NotFound();
        var ins = await inscricoes.InscreverAsync(id, tipo, nome, email, telefone, OrigemInscricao.Manual, pagamento, observacao);
        return VoltarCom($"{ins.Nome} foi adicionado(a). Código do ingresso: {ins.Codigo}");
    }

    [HttpPost("/eventos/{id:int}/participantes/{pid:int}/pagamento")]
    public async Task<IActionResult> Pagamento(int id, int pid, StatusPagamento status, string? forma)
    {
        var i = await Carregar(id, pid);
        if (i is null) return NotFound();
        InscricaoService.DefinirPagamento(i, status, forma);
        await db.SaveChangesAsync();
        return QuerJson ? Json(new { ok = true }) : VoltarCom($"Pagamento de {i.Nome}: {Texto.NomePagamento(status)}.");
    }

    [HttpPost("/eventos/{id:int}/participantes/{pid:int}/presenca")]
    public async Task<IActionResult> Presenca(int id, int pid, bool presente)
    {
        var i = await Carregar(id, pid);
        if (i is null) return NotFound();
        InscricaoService.MarcarPresenca(i, presente);
        await db.SaveChangesAsync();
        return QuerJson ? Json(new { ok = true, hora = i.CheckinEm is { } c ? Hora.Hhmm(c) : null }) : VoltarCom(presente ? $"{i.Nome} marcado(a) como presente." : "Presença desfeita.");
    }

    [HttpPost("/eventos/{id:int}/participantes/{pid:int}/cancelar")]
    public async Task<IActionResult> Cancelar(int id, int pid)
    {
        var i = await Carregar(id, pid);
        if (i is null) return NotFound();
        InscricaoService.Cancelar(i);
        await db.SaveChangesAsync();
        return VoltarCom($"Inscrição de {i.Nome} cancelada. O lugar voltou a ficar livre.");
    }

    [HttpPost("/eventos/{id:int}/participantes/lote")]
    public async Task<IActionResult> Lote(int id, string acao, int[] ids)
    {
        if (await eventos.ObterAsync(id, Eu) is null) return NotFound();
        var itens = await db.Inscricoes.Where(i => i.EventoId == id && ids.Contains(i.Id) && i.Status == StatusInscricao.Confirmada).ToListAsync();
        foreach (var i in itens)
        {
            switch (acao)
            {
                case "pago": if (i.Pagamento == StatusPagamento.Pendente) InscricaoService.DefinirPagamento(i, StatusPagamento.Pago); break;
                case "pendente": InscricaoService.DefinirPagamento(i, StatusPagamento.Pendente); break;
                case "presente": InscricaoService.MarcarPresenca(i, true); break;
                case "cancelar": InscricaoService.Cancelar(i); break;
                default: throw new RegraNegocioException("Ação desconhecida.");
            }
        }
        await db.SaveChangesAsync();
        return VoltarCom($"{itens.Count} participante(s) atualizado(s).");
    }

    [HttpGet("/eventos/{id:int}/exportar")]
    public async Task<IActionResult> Exportar(int id)
    {
        var e = await eventos.ObterAsync(id, Eu);
        if (e is null) return NotFound();
        var itens = await db.Inscricoes.Include(i => i.TipoIngresso).Where(i => i.EventoId == id).OrderBy(i => i.Nome).ToListAsync();

        static string C(string? v)
        {
            v ??= "";
            if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v; // evita fórmulas ao abrir no Excel
            return v.Contains(';') || v.Contains('"') || v.Contains('\n') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }
        var sb = new StringBuilder("Nome;E-mail;Telefone;Ingresso;Valor;Pagamento;Forma;Pago em;Check-in;Situação;Código;Inscrito em\n");
        foreach (var i in itens)
            sb.AppendJoin(';', C(i.Nome), C(i.Email), C(i.Telefone), C(i.TipoIngresso?.Nome), i.Valor.ToString("0.00", Hora.PtBr),
                Texto.NomePagamento(i.Pagamento), C(i.FormaPagamento),
                i.PagoEm is { } p ? Hora.Local(p).ToString("dd/MM/yyyy HH:mm") : "",
                i.CheckinEm is { } c ? Hora.Local(c).ToString("dd/MM/yyyy HH:mm") : "",
                i.Ativa ? "Confirmada" : "Cancelada", i.Codigo, Hora.Local(i.CriadoEm).ToString("dd/MM/yyyy HH:mm")).Append('\n');

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"participantes-{e.Slug}.csv");
    }

    [HttpGet("/eventos/{id:int}/checkin")]
    public async Task<IActionResult> Portaria(int id)
    {
        var e = await eventos.ObterAsync(id, Eu);
        if (e is null) return NotFound();
        ViewBag.Recentes = await db.Inscricoes.Include(i => i.TipoIngresso)
            .Where(i => i.EventoId == id && i.CheckinEm != null && i.Status == StatusInscricao.Confirmada)
            .OrderByDescending(i => i.CheckinEm).Take(12).ToListAsync();
        return View(await ResumoEvento.CarregarAsync(db, e));
    }

    [HttpPost("/eventos/{id:int}/checkin")]
    public async Task<IActionResult> Checkin(int id, string codigo, bool liberar)
    {
        var e = await eventos.ObterAsync(id, Eu);
        if (e is null) return NotFound();
        var r = await inscricoes.CheckinAsync(id, codigo, liberar);
        var i = r.Inscricao;
        var presentes = await db.Inscricoes.CountAsync(x => x.EventoId == id && x.Status == StatusInscricao.Confirmada && x.CheckinEm != null);
        return Json(new
        {
            resultado = r.Resultado.ToString(),
            presentes,
            inscricao = i is null ? null : new
            {
                i.Id, i.Nome, i.Codigo, tipo = i.TipoIngresso?.Nome, pagamento = Texto.NomePagamento(i.Pagamento),
                valor = Hora.Dinheiro(i.Valor), hora = i.CheckinEm is { } c ? Hora.Hhmm(c) : null, iniciais = Texto.Iniciais(i.Nome)
            }
        });
    }

    async Task<Inscricao?> Carregar(int id, int pid) =>
        await eventos.ObterAsync(id, Eu) is null ? null : await db.Inscricoes.FirstOrDefaultAsync(i => i.Id == pid && i.EventoId == id);
}
