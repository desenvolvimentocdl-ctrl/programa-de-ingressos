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

/// <summary>AguardaLote = virada de lote: só abre quando o lote anterior esgotar ou encerrar.</summary>
public enum SituacaoVenda { AVenda, Esgotado, Encerrado, EmBreve, AguardaLote }

public record ResumoTipo(int Id, string Nome, decimal Preco, int? Quantidade, int Vendidos, bool Ativo, DateTime? VendasAte = null, string? Descricao = null, int? EspacoId = null)
{
    public DateTime? VendasDe { get; init; }
    public bool AbreAposAnterior { get; init; }
    public bool PermiteMeia { get; init; }
    public SituacaoVenda Situacao { get; init; }
    /// <summary>Nome do lote que precisa acabar antes deste abrir (quando <see cref="Situacao"/> é AguardaLote).</summary>
    public string? LoteAnterior { get; init; }

    public bool Esgotado => Situacao == SituacaoVenda.Esgotado;
    public bool Encerrado => Situacao == SituacaoVenda.Encerrado;
    public bool AVenda => Situacao == SituacaoVenda.AVenda;
    public bool Meia => PermiteMeia && Preco > 0;
    public decimal PrecoMeia => Math.Round(Preco / 2m, 2);
    public int? Restantes => Quantidade is { } q ? Math.Max(0, q - Vendidos) : null;

    /// <summary>
    /// Situação de venda de cada tipo, na ordem do evento. Um tipo com "abre após o anterior" fica aguardando
    /// enquanto o tipo ativo de cima ainda estiver à venda, em breve ou também aguardando (virada em cadeia: 1º → 2º → 3º lote).
    /// </summary>
    public static List<ResumoTipo> Montar(IEnumerable<TipoIngresso> tipos, Func<int, int> vendidos, DateTime agoraUtc)
    {
        var lista = new List<ResumoTipo>();
        ResumoTipo? anterior = null;
        foreach (var t in tipos.OrderBy(t => t.Ordem))
        {
            var v = vendidos(t.Id);
            var situacao =
                !t.Ativo || t.VendaEncerrada(agoraUtc) ? SituacaoVenda.Encerrado
                : t.Quantidade is { } q && v >= q ? SituacaoVenda.Esgotado
                : t.VendaNaoComecou(agoraUtc) ? SituacaoVenda.EmBreve
                : t.AbreAposAnterior && anterior is { Situacao: SituacaoVenda.AVenda or SituacaoVenda.EmBreve or SituacaoVenda.AguardaLote } ? SituacaoVenda.AguardaLote
                : SituacaoVenda.AVenda;
            var r = new ResumoTipo(t.Id, t.Nome, t.Preco, t.Quantidade, v, t.Ativo, t.VendasAte, t.Descricao, t.EspacoId)
            {
                VendasDe = t.VendasDe, AbreAposAnterior = t.AbreAposAnterior, PermiteMeia = t.PermiteMeia, Situacao = situacao,
                LoteAnterior = situacao == SituacaoVenda.AguardaLote ? anterior!.Nome : null
            };
            lista.Add(r);
            if (t.Ativo) anterior = r;
        }
        return lista;
    }
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

    public List<ResumoTipo> PorTipo => ResumoTipo.Montar(Evento.Tipos, id => Ativas.Count(l => l.TipoId == id), DateTime.UtcNow)
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
