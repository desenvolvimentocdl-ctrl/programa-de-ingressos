using Plateia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Application;

public enum ResultadoCheckin { Liberado, JaEntrou, PagamentoPendente, Cancelada, NaoEncontrado }

public record Checkin(ResultadoCheckin Resultado, Inscricao? Inscricao);

public class InscricaoService(IAppDbContext db)
{
    /// <summary>
    /// Cria a inscrição travando a linha do evento durante a contagem, para dois pedidos simultâneos
    /// não ocuparem o mesmo último lugar.
    /// </summary>
    public async Task<Inscricao> InscreverAsync(int eventoId, int tipoId, string nome, string email, string? telefone,
        OrigemInscricao origem, StatusPagamento? pagamento = null, string? observacao = null)
    {
        var n = Utilidades.Limpo(nome, 160) ?? throw new RegraNegocioException("Informe o nome.");
        var e = (email ?? "").Trim().ToLowerInvariant();
        if (!Utilidades.EmailValido(e) || e.Length > 180) throw new RegraNegocioException("Informe um e-mail válido.");
        var fone = Utilidades.SoDigitos(telefone);
        if (fone is { Length: > 15 }) fone = fone[..15];

        var estrategia = db.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM Eventos WHERE Id = {eventoId} FOR UPDATE");

            var ev = await db.Eventos.Include(x => x.Tipos).FirstOrDefaultAsync(x => x.Id == eventoId)
                     ?? throw new RegraNegocioException("Evento não encontrado.");
            var tipo = ev.Tipos.FirstOrDefault(t => t.Id == tipoId && t.Ativo)
                       ?? throw new RegraNegocioException("Escolha um tipo de ingresso.");

            if (origem == OrigemInscricao.Site)
            {
                if (ev.Status != StatusEvento.Publicado || !ev.InscricoesAbertas)
                    throw new RegraNegocioException("As inscrições deste evento estão fechadas.");
                if (await db.Inscricoes.AnyAsync(i => i.EventoId == eventoId && i.Email == e && i.Status == StatusInscricao.Confirmada))
                    throw new RegraNegocioException("Este e-mail já tem inscrição neste evento. Se perdeu o ingresso, fale com o organizador.");
            }

            var ativos = db.Inscricoes.Where(i => i.EventoId == eventoId && i.Status == StatusInscricao.Confirmada);
            if (await ativos.CountAsync() >= ev.Capacidade)
                throw new RegraNegocioException("Lotado: não há mais lugares neste evento.");
            if (tipo.Quantidade is { } q && await ativos.CountAsync(i => i.TipoIngressoId == tipo.Id) >= q)
                throw new RegraNegocioException($"Os ingressos \"{tipo.Nome}\" esgotaram.");

            var codigo = Utilidades.NovoCodigo();
            while (await db.Inscricoes.AnyAsync(i => i.Codigo == codigo)) codigo = Utilidades.NovoCodigo();

            var ins = new Inscricao
            {
                EventoId = eventoId, TipoIngressoId = tipo.Id, Nome = n, Email = e, Telefone = fone, Codigo = codigo,
                Origem = origem, Valor = tipo.Preco, Observacao = Utilidades.Limpo(observacao, 500),
                Pagamento = tipo.Gratuito ? StatusPagamento.Cortesia : pagamento ?? StatusPagamento.Pendente
            };
            if (ins.Pagamento != StatusPagamento.Pendente) ins.PagoEm = DateTime.UtcNow;
            db.Inscricoes.Add(ins);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            ins.TipoIngresso = tipo;
            ins.Evento = ev;
            return ins;
        });
    }

    public static void DefinirPagamento(Inscricao i, StatusPagamento status, string? forma = null)
    {
        if (!i.Ativa) throw new RegraNegocioException("Esta inscrição está cancelada.");
        i.Pagamento = status;
        i.FormaPagamento = status == StatusPagamento.Pendente ? null : Utilidades.Limpo(forma, 40) ?? i.FormaPagamento;
        i.PagoEm = status == StatusPagamento.Pendente ? null : i.PagoEm ?? DateTime.UtcNow;
    }

    public static void Cancelar(Inscricao i)
    {
        if (!i.Ativa) return;
        i.Status = StatusInscricao.Cancelada;
        i.CanceladaEm = DateTime.UtcNow;
        i.CheckinEm = null;
    }

    public static void MarcarPresenca(Inscricao i, bool presente)
    {
        if (!i.Ativa) throw new RegraNegocioException("Esta inscrição está cancelada.");
        i.CheckinEm = presente ? i.CheckinEm ?? DateTime.UtcNow : null;
    }

    /// <summary>
    /// Check-in pela portaria. Com pagamento pendente só libera se <paramref name="liberarPendente"/>
    /// (a pessoa pagou na entrada: o pagamento fica registrado como "Na entrada").
    /// </summary>
    public async Task<Checkin> CheckinAsync(int eventoId, string? codigoLido, bool liberarPendente)
    {
        var codigo = Utilidades.NormalizarCodigo(codigoLido);
        if (codigo.Length == 0) return new(ResultadoCheckin.NaoEncontrado, null);
        var i = await db.Inscricoes.Include(x => x.TipoIngresso)
            .FirstOrDefaultAsync(x => x.EventoId == eventoId && x.Codigo == codigo);
        if (i is null) return new(ResultadoCheckin.NaoEncontrado, null);
        if (!i.Ativa) return new(ResultadoCheckin.Cancelada, i);
        if (i.CheckinEm is not null) return new(ResultadoCheckin.JaEntrou, i);
        if (i.Pagamento == StatusPagamento.Pendente)
        {
            if (!liberarPendente) return new(ResultadoCheckin.PagamentoPendente, i);
            DefinirPagamento(i, StatusPagamento.Pago, "Na entrada");
        }
        i.CheckinEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(ResultadoCheckin.Liberado, i);
    }
}
