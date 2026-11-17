using Plateia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Application;

public record LinhaResumo(int Id, string Nome, int TipoId, StatusInscricao Status, StatusPagamento Pagamento, decimal Valor,
    DateTime CriadoEm, DateTime? PagoEm, DateTime? CheckinEm, DateTime? CanceladaEm);

public enum TipoAcontecimento { Inscreveu, Pagou, Entrou, Cancelou }

public record Acontecimento(DateTime Quando, TipoAcontecimento Tipo, string Nome);

public record ResumoEspaco(int Id, string Nome, string? Descricao, int Capacidade, int Ocupados)
{
    public int Livres => Math.Max(0, Capacidade - Ocupados);
}

public record ResumoTipo(int Id, string Nome, decimal Preco, int? Quantidade, int Vendidos, bool Ativo, DateTime? VendasAte = null, string? Descricao = null, int? EspacoId = null)
{
    public bool Esgotado => Quantidade is { } q && Vendidos >= q;
    public bool Encerrado => VendasAte is { } v && DateTime.UtcNow > v;
}

/// <summary>Números do painel de um evento. Eventos têm no máximo alguns milhares de inscrições, então tudo é calculado em memória.</summary>
public class ResumoEvento
{
    public required Evento Evento { get; init; }
    public required List<LinhaResumo> Linhas { get; init; }

    IEnumerable<LinhaResumo> Ativas => Linhas.Where(l => l.Status == StatusInscricao.Confirmada);

    public int Confirmados => Ativas.Count();
    public int Pagos => Ativas.Count(l => l.Pagamento == StatusPagamento.Pago);
    public int Pendentes => Ativas.Count(l => l.Pagamento == StatusPagamento.Pendente);
    public int Cortesias => Ativas.Count(l => l.Pagamento == StatusPagamento.Cortesia);
    public int Presentes => Ativas.Count(l => l.CheckinEm is not null);
    public int Cancelados => Linhas.Count(l => l.Status == StatusInscricao.Cancelada);
    public int Livres => Math.Max(0, Evento.Capacidade - Confirmados);
    public decimal Arrecadado => Ativas.Where(l => l.Pagamento == StatusPagamento.Pago).Sum(l => l.Valor);
    public decimal AReceber => Ativas.Where(l => l.Pagamento == StatusPagamento.Pendente).Sum(l => l.Valor);
    public double Ocupacao => Evento.Capacidade == 0 ? 0 : Math.Min(1, (double)Confirmados / Evento.Capacidade);

    public List<ResumoTipo> PorTipo => Evento.Tipos.OrderBy(t => t.Ordem)
        .Select(t => new ResumoTipo(t.Id, t.Nome, t.Preco, t.Quantidade, Ativas.Count(l => l.TipoId == t.Id), t.Ativo, t.VendasAte, t.Descricao, t.EspacoId))
        .Where(t => t.Ativo || t.Vendidos > 0).ToList();

    public List<ResumoEspaco> PorEspaco => Evento.Espacos.OrderBy(x => x.Ordem)
        .Select(x => new ResumoEspaco(x.Id, x.Nome, x.Descricao, x.Capacidade,
            Ativas.Count(l => Evento.Tipos.FirstOrDefault(t => t.Id == l.TipoId)?.EspacoId == x.Id))).ToList();

    /// <summary>Inscrições por dia (datas locais), dos últimos <paramref name="dias"/> dias.</summary>
    public List<(DateTime Dia, int Total)> PorDia(int dias, Func<DateTime, DateTime> paraLocal, DateTime hojeLocal)
    {
        var contagem = Linhas.GroupBy(l => paraLocal(l.CriadoEm).Date).ToDictionary(g => g.Key, g => g.Count());
        return Enumerable.Range(0, dias).Select(i => hojeLocal.AddDays(i - dias + 1))
            .Select(d => (d, contagem.GetValueOrDefault(d))).ToList();
    }

    public List<Acontecimento> Recentes(int quantos) => Linhas
        .SelectMany(l => new[]
        {
            new Acontecimento(l.CriadoEm, TipoAcontecimento.Inscreveu, l.Nome),
            l.Pagamento == StatusPagamento.Pago && l.PagoEm is { } p ? new Acontecimento(p, TipoAcontecimento.Pagou, l.Nome) : null,
            l.CheckinEm is { } c ? new Acontecimento(c, TipoAcontecimento.Entrou, l.Nome) : null,
            l.CanceladaEm is { } x ? new Acontecimento(x, TipoAcontecimento.Cancelou, l.Nome) : null
        })
        .OfType<Acontecimento>()
        .OrderByDescending(a => a.Quando).Take(quantos).ToList();

    public static async Task<ResumoEvento> CarregarAsync(IAppDbContext db, Evento evento) => new()
    {
        Evento = evento,
        Linhas = await db.Inscricoes.Where(i => i.EventoId == evento.Id)
            .Select(i => new LinhaResumo(i.Id, i.Nome, i.TipoIngressoId, i.Status, i.Pagamento, i.Valor, i.CriadoEm, i.PagoEm, i.CheckinEm, i.CanceladaEm))
            .ToListAsync()
    };
}
