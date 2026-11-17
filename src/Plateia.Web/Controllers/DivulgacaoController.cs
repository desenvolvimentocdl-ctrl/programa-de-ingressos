using Plateia.Application;
using Plateia.Domain;
using Plateia.Infrastructure;
using Plateia.Web.Infra;
using Plateia.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Web.Controllers;

/// <summary>Kit de divulgação do evento: links por canal, resultado de cada canal, cupons, QR code e artes.</summary>
public class DivulgacaoController(AppDbContext db, EventoService eventos, DivulgacaoService divulgacao) : BaseController
{
    [HttpGet("/eventos/{id:int}/divulgacao")]
    public async Task<IActionResult> Index(int id)
    {
        var e = await eventos.ObterAsync(id, Eu);
        if (e is null) return NotFound();
        return View(new PaginaDivulgacao
        {
            Evento = e,
            LinkBase = $"{Request.Scheme}://{Request.Host}/e/{e.Slug}",
            Funil = await divulgacao.FunilAsync(id),
            Cupons = await divulgacao.CuponsAsync(id),
            Confirmados = await db.Inscricoes.CountAsync(i => i.EventoId == id && i.Status == StatusInscricao.Confirmada),
            MenorPreco = Texto.APartirDe(e.Tipos)
        });
    }

    /// <summary>QR code da página do evento, para cartazes e material impresso (conta como canal "cartaz").</summary>
    [HttpGet("/eventos/{id:int}/qr.svg")]
    public async Task<IActionResult> QrCode(int id, string? canal)
    {
        var e = await eventos.ObterAsync(id, Eu);
        if (e is null) return NotFound();
        var c = DivulgacaoService.NormalizarCanal(canal) ?? "cartaz";
        var svg = Qr.Svg($"{Request.Scheme}://{Request.Host}/e/{e.Slug}?origem={c}");
        return File(System.Text.Encoding.UTF8.GetBytes(svg), "image/svg+xml", $"qr-{e.Slug}.svg");
    }

    [HttpPost("/eventos/{id:int}/cupons")]
    public async Task<IActionResult> CriarCupom(int id, string codigo, TipoDesconto tipo, string valor, int? limite, DateTime? validoAte)
    {
        if (await eventos.ObterAsync(id, Eu) is null) return NotFound();
        decimal.TryParse((valor ?? "").Replace("R$", "").Replace("%", "").Trim(), System.Globalization.NumberStyles.Number, Hora.PtBr, out var v);
        var c = await divulgacao.CriarCupomAsync(id, codigo, tipo, v, limite, validoAte is { } d ? Hora.ParaUtc(d) : null);
        return VoltarCom($"Cupom {c.Codigo} criado.");
    }

    [HttpPost("/eventos/{id:int}/cupons/{cid:int}/ativo")]
    public async Task<IActionResult> AtivarCupom(int id, int cid, bool ativo)
    {
        if (await eventos.ObterAsync(id, Eu) is null) return NotFound();
        var c = await db.Cupons.FirstOrDefaultAsync(x => x.Id == cid && x.EventoId == id);
        if (c is null) return NotFound();
        c.Ativo = ativo;
        await db.SaveChangesAsync();
        return VoltarCom(ativo ? $"Cupom {c.Codigo} reativado." : $"Cupom {c.Codigo} desativado. Quem já usou continua com o desconto.");
    }
}
