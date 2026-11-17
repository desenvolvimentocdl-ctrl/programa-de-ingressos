using Plateia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Application;

public record LinhaFunil(string Canal, int Visitas, int Inscricoes, int Pagos, decimal Receita)
{
    public double Conversao => Visitas == 0 ? 0 : Math.Min(1, (double)Inscricoes / Visitas);
}

public record UsoCupom(Cupom Cupom, int Usos, decimal DescontoTotal);

/// <summary>Cupons de desconto e medição da divulgação (visitas e inscrições por canal).</summary>
public class DivulgacaoService(IAppDbContext db)
{
    public const string Direto = "direto";

    /// <summary>Canais sugeridos na tela de divulgação, com o nome que aparece para o organizador.</summary>
    public static readonly (string Chave, string Nome)[] Canais =
    [
        ("instagram", "Instagram"), ("whatsapp", "WhatsApp"), ("facebook", "Facebook"), ("linkedin", "LinkedIn"),
        ("email", "E-mail"), ("cartaz", "Cartaz / QR impresso"), ("site", "Site"), ("parceiros", "Parceiros")
    ];

    public static string NomeCanal(string? chave) =>
        chave is null or Direto ? "Direto / sem canal" : Canais.FirstOrDefault(c => c.Chave == chave).Nome ?? chave;

    public static string NormalizarCupom(string? c) =>
        new string((c ?? "").Trim().ToUpperInvariant().Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_').Take(30).ToArray());

    /// <summary>"Instagram Stories" → "instagram-stories". Vazio vira nulo (inscrição sem canal).</summary>
    public static string? NormalizarCanal(string? c)
    {
        if (string.IsNullOrWhiteSpace(c)) return null;
        var s = Utilidades.Slug(c);
        if (s.Length > 40) s = s[..40];
        return s == "evento" && !c.Contains("evento", StringComparison.OrdinalIgnoreCase) ? null : s;
    }

    /// <summary>Conta uma visita à página pública (uma linha por evento, canal e dia).</summary>
    public Task RegistrarVisitaAsync(int eventoId, string? canal, DateTime diaLocal)
    {
        var c = NormalizarCanal(canal) ?? Direto;
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO Visitas (EventoId, Canal, Dia, Visitas) VALUES ({eventoId}, {c}, {diaLocal.Date}, 1) ON DUPLICATE KEY UPDATE Visitas = Visitas + 1");
    }

    public async Task<List<LinhaFunil>> FunilAsync(int eventoId)
    {
        var visitas = await db.Visitas.Where(v => v.EventoId == eventoId)
            .GroupBy(v => v.Canal).Select(g => new { Canal = g.Key, N = g.Sum(v => v.Visitas) }).ToListAsync();
        var ins = await db.Inscricoes.Where(i => i.EventoId == eventoId && i.Status == StatusInscricao.Confirmada && i.Origem == OrigemInscricao.Site)
            .Select(i => new { i.Canal, i.Pagamento, i.Valor }).ToListAsync();

        var canais = visitas.Select(v => v.Canal).Concat(ins.Select(i => i.Canal ?? Direto)).Distinct();
        return canais.Select(c =>
        {
            var doCanal = ins.Where(i => (i.Canal ?? Direto) == c).ToList();
            return new LinhaFunil(c, visitas.FirstOrDefault(v => v.Canal == c)?.N ?? 0, doCanal.Count,
                doCanal.Count(i => i.Pagamento == StatusPagamento.Pago), doCanal.Where(i => i.Pagamento == StatusPagamento.Pago).Sum(i => i.Valor));
        }).OrderByDescending(l => l.Inscricoes).ThenByDescending(l => l.Visitas).ToList();
    }

    public async Task<List<UsoCupom>> CuponsAsync(int eventoId)
    {
        var cupons = await db.Cupons.Where(c => c.EventoId == eventoId).OrderByDescending(c => c.Ativo).ThenBy(c => c.Codigo).ToListAsync();
        var usos = await db.Inscricoes.Where(i => i.EventoId == eventoId && i.CupomId != null && i.Status == StatusInscricao.Confirmada)
            .GroupBy(i => i.CupomId!.Value).Select(g => new { Id = g.Key, N = g.Count(), D = g.Sum(i => i.Desconto) }).ToListAsync();
        return cupons.Select(c => new UsoCupom(c, usos.FirstOrDefault(u => u.Id == c.Id)?.N ?? 0, usos.FirstOrDefault(u => u.Id == c.Id)?.D ?? 0)).ToList();
    }

    public async Task<Cupom> CriarCupomAsync(int eventoId, string codigo, TipoDesconto tipo, decimal valor, int? limite, DateTime? validoAteUtc)
    {
        var c = NormalizarCupom(codigo);
        if (c.Length < 3) throw new RegraNegocioException("O código do cupom precisa ter pelo menos 3 letras ou números.");
        if (valor <= 0) throw new RegraNegocioException("Informe o valor do desconto.");
        if (tipo == TipoDesconto.Percentual && valor > 100) throw new RegraNegocioException("O desconto percentual vai até 100%.");
        if (limite is < 1) throw new RegraNegocioException("O limite de usos precisa ser maior que zero (ou deixe em branco).");
        if (await db.Cupons.AnyAsync(x => x.EventoId == eventoId && x.Codigo == c)) throw new RegraNegocioException($"Já existe o cupom {c} neste evento.");
        var cupom = new Cupom { EventoId = eventoId, Codigo = c, Tipo = tipo, Valor = Math.Round(valor, 2), Limite = limite, ValidoAte = validoAteUtc };
        db.Cupons.Add(cupom);
        await db.SaveChangesAsync();
        return cupom;
    }

    /// <summary>Confere um cupom para mostrar o preço com desconto na página (a validação de verdade é na inscrição).</summary>
    public async Task<Cupom?> CupomValidoAsync(int eventoId, string? codigo)
    {
        var c = NormalizarCupom(codigo);
        if (c.Length == 0) return null;
        var cupom = await db.Cupons.FirstOrDefaultAsync(x => x.EventoId == eventoId && x.Codigo == c && x.Ativo);
        if (cupom is null || cupom.ValidoAte is { } ate && DateTime.UtcNow > ate) return null;
        if (cupom.Limite is { } lim && await db.Inscricoes.CountAsync(i => i.CupomId == cupom.Id && i.Status == StatusInscricao.Confirmada) >= lim) return null;
        return cupom;
    }
}
