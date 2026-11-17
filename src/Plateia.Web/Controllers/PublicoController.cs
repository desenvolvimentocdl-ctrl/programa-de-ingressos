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

/// <summary>Páginas abertas ao público: página do evento, inscrição e ingresso.</summary>
[AllowAnonymous]
public class PublicoController(AppDbContext db, InscricaoService inscricoes) : BaseController
{
    [HttpGet("/e/{slug}")]
    public async Task<IActionResult> Evento(string slug)
    {
        var pagina = await Montar(slug);
        return pagina is null ? NotFound() : View(pagina);
    }

    [HttpPost("/e/{slug}"), EnableRateLimiting("publico")]
    public async Task<IActionResult> Inscrever(string slug, int tipo, string nome, string email, string? telefone)
    {
        var pagina = await Montar(slug);
        if (pagina is null) return NotFound();
        try
        {
            var ins = await inscricoes.InscreverAsync(pagina.Evento.Id, tipo, nome, email, telefone, OrigemInscricao.Site);
            return Redirect($"/ingresso/{ins.Codigo}?novo=1");
        }
        catch (RegraNegocioException ex)
        {
            ViewBag.Erro = ex.Message;
            ViewBag.Form = new { tipo, nome, email, telefone };
            return View("Evento", await Montar(slug));
        }
    }

    [HttpGet("/ingresso/{codigo}")]
    public async Task<IActionResult> Ingresso(string codigo)
    {
        var c = Utilidades.NormalizarCodigo(codigo);
        var i = await db.Inscricoes.Include(x => x.Evento).Include(x => x.TipoIngresso).FirstOrDefaultAsync(x => x.Codigo == c);
        if (i is null) return NotFound();
        ViewBag.Link = $"{Request.Scheme}://{Request.Host}/ingresso/{i.Codigo}";
        return View(i);
    }

    [HttpGet("/ingresso/{codigo}/agenda.ics")]
    public async Task<IActionResult> Agenda(string codigo)
    {
        var c = Utilidades.NormalizarCodigo(codigo);
        var i = await db.Inscricoes.Include(x => x.Evento).FirstOrDefaultAsync(x => x.Codigo == c && x.Status == StatusInscricao.Confirmada);
        if (i?.Evento is not { } e) return NotFound();
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

    async Task<PaginaEvento?> Montar(string slug)
    {
        var e = await db.Eventos.Include(x => x.Tipos).Include(x => x.Organizador).FirstOrDefaultAsync(x => x.Slug == slug);
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
        return new PaginaEvento { Evento = e, Tipos = resumo.PorTipo.Where(t => t.Ativo).ToList(), Livres = resumo.Livres, Previa = previa };
    }
}
