using Plateia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Application;

public enum ResultadoCheckin { Liberado, JaEntrou, PagamentoPendente, Cancelada, NaoEncontrado }

public record Checkin(ResultadoCheckin Resultado, Inscricao? Inscricao);

/// <summary>Um ingresso do checkout: o tipo escolhido e quem vai usar. E-mail vazio = usa o de quem compra.</summary>
public record ParticipanteCheckout(int TipoId, string? Nome, string? Email, string? Telefone, bool Meia = false);

public class InscricaoService(IAppDbContext db)
{
    /// <summary>Máximo de ingressos num mesmo pedido.</summary>
    public const int MaxPorPedido = 10;

    /// <summary>
    /// Checkout único: cria o pedido e um ingresso por participante, tudo na mesma transação
    /// (ou entram todos, ou nenhum). A linha do evento fica travada durante a contagem de lugares.
    /// </summary>
    public async Task<Pedido> CheckoutAsync(int eventoId, string nome, string email, string? telefone,
        IReadOnlyList<ParticipanteCheckout> itens, string? cupom = null, string? canal = null)
    {
        var nomeComprador = Utilidades.Limpo(nome, 160) ?? throw new RegraNegocioException("Informe o nome de quem está comprando.");
        var emailComprador = (email ?? "").Trim().ToLowerInvariant();
        if (!Utilidades.EmailValido(emailComprador) || emailComprador.Length > 180) throw new RegraNegocioException("Informe um e-mail válido para quem está comprando.");
        var foneComprador = Fone(telefone);
        if (itens.Count == 0) throw new RegraNegocioException("Escolha pelo menos um ingresso.");
        if (itens.Count > MaxPorPedido) throw new RegraNegocioException($"No máximo {MaxPorPedido} ingressos por pedido.");

        // Valida os participantes antes de abrir a transação.
        var participantes = new List<(int TipoId, string Nome, string Email, string? Fone, bool EmailProprio, bool Meia)>();
        for (var k = 0; k < itens.Count; k++)
        {
            var it = itens[k];
            var n = Utilidades.Limpo(it.Nome, 160) ?? throw new RegraNegocioException($"Informe o nome do participante do ingresso {k + 1}.");
            var e = (it.Email ?? "").Trim().ToLowerInvariant();
            var proprio = e.Length > 0 && e != emailComprador;
            if (e.Length == 0) e = emailComprador;
            else if (!Utilidades.EmailValido(e) || e.Length > 180) throw new RegraNegocioException($"O e-mail do ingresso {k + 1} ({n}) não é válido.");
            participantes.Add((it.TipoId, n, e, Fone(it.Telefone) ?? (proprio ? null : foneComprador), proprio, it.Meia));
        }
        var repetido = participantes.Where(p => p.EmailProprio).GroupBy(p => p.Email).FirstOrDefault(g => g.Count() > 1);
        if (repetido is not null) throw new RegraNegocioException($"O e-mail {repetido.Key} foi usado em mais de um ingresso. Cada participante precisa de um e-mail próprio (ou deixe em branco).");
        var codigoCupom = DivulgacaoService.NormalizarCupom(cupom);

        var estrategia = db.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM Eventos WHERE Id = {eventoId} FOR UPDATE");

            var ev = await db.Eventos.Include(x => x.Tipos).Include(x => x.Espacos).FirstOrDefaultAsync(x => x.Id == eventoId)
                     ?? throw new RegraNegocioException("Evento não encontrado.");
            if (ev.Status != StatusEvento.Publicado || !ev.InscricoesAbertas)
                throw new RegraNegocioException("As inscrições deste evento estão fechadas.");

            var agora = DateTime.UtcNow;
            // Situação de cada tipo agora (lotes que ainda não abriram, esgotados, encerrados).
            var vendidosPorTipo = await db.Inscricoes.Where(i => i.EventoId == eventoId && i.Status == StatusInscricao.Confirmada)
                .GroupBy(i => i.TipoIngressoId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
            var situacoes = ResumoTipo.Montar(ev.Tipos, id => vendidosPorTipo.GetValueOrDefault(id), agora).ToDictionary(r => r.Id);
            var tipos = new Dictionary<int, TipoIngresso>();
            foreach (var id in participantes.Select(p => p.TipoId).Distinct())
            {
                var t = ev.Tipos.FirstOrDefault(x => x.Id == id && x.Ativo) ?? throw new RegraNegocioException("Escolha um tipo de ingresso válido.");
                var r = situacoes[id];
                switch (r.Situacao)
                {
                    case SituacaoVenda.Encerrado: throw new RegraNegocioException($"As vendas de \"{t.Nome}\" já encerraram.");
                    case SituacaoVenda.Esgotado: throw new RegraNegocioException($"Os ingressos \"{t.Nome}\" esgotaram.");
                    case SituacaoVenda.EmBreve: throw new RegraNegocioException($"As vendas de \"{t.Nome}\" ainda não começaram.");
                    case SituacaoVenda.AguardaLote: throw new RegraNegocioException($"\"{t.Nome}\" abre quando \"{r.LoteAnterior}\" esgotar ou encerrar.");
                }
                tipos[id] = t;
            }
            if (participantes.FirstOrDefault(p => p.Meia && !(tipos[p.TipoId].PermiteMeia && tipos[p.TipoId].Preco > 0)) is { Nome: not null } semMeia)
                throw new RegraNegocioException($"\"{tipos[semMeia.TipoId].Nome}\" não tem meia-entrada.");

            // Quem tem e-mail próprio não pode ter outra inscrição ativa no evento.
            var emails = participantes.Where(p => p.EmailProprio).Select(p => p.Email).ToList();
            if (emails.Count > 0 && await db.Inscricoes.Where(i => i.EventoId == eventoId && i.Status == StatusInscricao.Confirmada && emails.Contains(i.Email))
                    .Select(i => i.Email).FirstOrDefaultAsync() is { } jaInscrito)
                throw new RegraNegocioException($"O e-mail {jaInscrito} já tem inscrição neste evento. Se perdeu o ingresso, fale com o organizador.");

            // Lugares: evento, tipo e espaço (contando os ingressos deste pedido).
            var ativos = db.Inscricoes.Where(i => i.EventoId == eventoId && i.Status == StatusInscricao.Confirmada);
            var livres = ev.Capacidade - await ativos.CountAsync();
            if (livres <= 0) throw new RegraNegocioException("Lotado: não há mais lugares neste evento.");
            if (participantes.Count > livres) throw new RegraNegocioException($"Restam só {livres} lugar(es) neste evento. Diminua a quantidade.");
            foreach (var g in participantes.GroupBy(p => p.TipoId))
            {
                var t = tipos[g.Key];
                if (t.Quantidade is { } q)
                {
                    var resta = q - await ativos.CountAsync(i => i.TipoIngressoId == t.Id);
                    if (resta <= 0) throw new RegraNegocioException($"Os ingressos \"{t.Nome}\" esgotaram.");
                    if (g.Count() > resta) throw new RegraNegocioException($"Restam só {resta} ingresso(s) \"{t.Nome}\".");
                }
            }
            foreach (var g in participantes.GroupBy(p => tipos[p.TipoId].EspacoId).Where(g => g.Key is not null))
            {
                var esp = ev.Espacos.FirstOrDefault(x => x.Id == g.Key);
                if (esp is null) continue;
                var resta = esp.Capacidade - await ativos.CountAsync(i => i.TipoIngresso!.EspacoId == esp.Id);
                if (resta <= 0) throw new RegraNegocioException($"Lotado: não há mais lugares em \"{esp.Nome}\".");
                if (g.Count() > resta) throw new RegraNegocioException($"Restam só {resta} lugar(es) em \"{esp.Nome}\".");
            }

            // Cupom: vale para cada ingresso pago do pedido (cada um conta um uso).
            Cupom? cp = null;
            if (codigoCupom.Length > 0)
            {
                cp = await db.Cupons.FirstOrDefaultAsync(c => c.EventoId == eventoId && c.Codigo == codigoCupom && c.Ativo);
                if (cp is null || cp.ValidoAte is { } ate && agora > ate)
                    throw new RegraNegocioException("Cupom inválido ou vencido.");
                if (cp.Limite is { } lim)
                {
                    var usos = participantes.Count(p => tipos[p.TipoId].Preco > 0);
                    var resta = lim - await db.Inscricoes.CountAsync(i => i.CupomId == cp.Id && i.Status == StatusInscricao.Confirmada);
                    if (resta <= 0) throw new RegraNegocioException("Este cupom já atingiu o limite de usos.");
                    if (usos > resta) throw new RegraNegocioException($"Este cupom só tem mais {resta} uso(s). Diminua a quantidade de ingressos pagos ou remova o cupom.");
                }
            }

            var pedido = new Pedido
            {
                EventoId = eventoId, Nome = nomeComprador, Email = emailComprador, Telefone = foneComprador,
                Codigo = await NovoCodigoAsync(c => db.Pedidos.AnyAsync(p => p.Codigo == c))
            };
            var canalNorm = DivulgacaoService.NormalizarCanal(canal);
            foreach (var p in participantes)
            {
                var t = tipos[p.TipoId];
                var preco = p.Meia ? t.PrecoMeia : t.Preco;
                var desconto = preco > 0 ? cp?.DescontoSobre(preco) ?? 0 : 0;
                var valor = preco - desconto;
                var ins = new Inscricao
                {
                    EventoId = eventoId, TipoIngressoId = t.Id, TipoIngresso = t, Nome = p.Nome, Email = p.Email, Telefone = p.Fone, Meia = p.Meia,
                    Codigo = await NovoCodigoAsync(c => db.Inscricoes.AnyAsync(i => i.Codigo == c)),
                    Origem = OrigemInscricao.Site, Valor = valor, Desconto = desconto, CupomId = desconto > 0 ? cp?.Id : null, Canal = canalNorm,
                    Pagamento = valor <= 0 ? StatusPagamento.Cortesia : StatusPagamento.Pendente
                };
                if (ins.Pagamento != StatusPagamento.Pendente) ins.PagoEm = agora;
                // Dois participantes do mesmo pedido não podem sortear o mesmo código.
                while (pedido.Inscricoes.Any(x => x.Codigo == ins.Codigo)) ins.Codigo = await NovoCodigoAsync(c => db.Inscricoes.AnyAsync(i => i.Codigo == c));
                pedido.Inscricoes.Add(ins);
            }
            pedido.Total = pedido.Inscricoes.Sum(i => i.Valor);
            pedido.Desconto = pedido.Inscricoes.Sum(i => i.Desconto);

            db.Pedidos.Add(pedido);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            pedido.Evento = ev;
            return pedido;
        });
    }

    static string? Fone(string? telefone)
    {
        var f = Utilidades.SoDigitos(telefone);
        return f is { Length: > 15 } ? f[..15] : f;
    }

    static async Task<string> NovoCodigoAsync(Func<string, Task<bool>> existe)
    {
        var c = Utilidades.NovoCodigo();
        while (await existe(c)) c = Utilidades.NovoCodigo();
        return c;
    }

    /// <summary>
    /// Cria a inscrição travando a linha do evento durante a contagem, para dois pedidos simultâneos
    /// não ocuparem o mesmo último lugar.
    /// </summary>
    public async Task<Inscricao> InscreverAsync(int eventoId, int tipoId, string nome, string email, string? telefone,
        OrigemInscricao origem, StatusPagamento? pagamento = null, string? observacao = null, string? cupom = null, string? canal = null)
    {
        var codigoCupom = DivulgacaoService.NormalizarCupom(cupom);
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
                if (tipo.VendaEncerrada(DateTime.UtcNow))
                    throw new RegraNegocioException($"As vendas de \"{tipo.Nome}\" já encerraram.");
                if (await db.Inscricoes.AnyAsync(i => i.EventoId == eventoId && i.Email == e && i.Status == StatusInscricao.Confirmada))
                    throw new RegraNegocioException("Este e-mail já tem inscrição neste evento. Se perdeu o ingresso, fale com o organizador.");
            }

            Cupom? cp = null;
            if (codigoCupom.Length > 0)
            {
                cp = await db.Cupons.FirstOrDefaultAsync(c => c.EventoId == eventoId && c.Codigo == codigoCupom && c.Ativo);
                if (cp is null || cp.ValidoAte is { } ate && DateTime.UtcNow > ate)
                    throw new RegraNegocioException("Cupom inválido ou vencido.");
                if (cp.Limite is { } lim && await db.Inscricoes.CountAsync(i => i.CupomId == cp.Id && i.Status == StatusInscricao.Confirmada) >= lim)
                    throw new RegraNegocioException("Este cupom já atingiu o limite de usos.");
            }

            var ativos = db.Inscricoes.Where(i => i.EventoId == eventoId && i.Status == StatusInscricao.Confirmada);
            if (await ativos.CountAsync() >= ev.Capacidade)
                throw new RegraNegocioException("Lotado: não há mais lugares neste evento.");
            if (tipo.Quantidade is { } q && await ativos.CountAsync(i => i.TipoIngressoId == tipo.Id) >= q)
                throw new RegraNegocioException($"Os ingressos \"{tipo.Nome}\" esgotaram.");
            if (tipo.EspacoId is { } espId && await db.Espacos.FirstOrDefaultAsync(x => x.Id == espId) is { } esp
                && await ativos.CountAsync(i => i.TipoIngresso!.EspacoId == espId) >= esp.Capacidade)
                throw new RegraNegocioException($"Lotado: não há mais lugares em \"{esp.Nome}\".");

            var codigo = Utilidades.NovoCodigo();
            while (await db.Inscricoes.AnyAsync(i => i.Codigo == codigo)) codigo = Utilidades.NovoCodigo();

            var desconto = cp?.DescontoSobre(tipo.Preco) ?? 0;
            var valor = tipo.Preco - desconto;
            var ins = new Inscricao
            {
                EventoId = eventoId, TipoIngressoId = tipo.Id, Nome = n, Email = e, Telefone = fone, Codigo = codigo,
                Origem = origem, Valor = valor, Desconto = desconto, CupomId = cp?.Id,
                Canal = DivulgacaoService.NormalizarCanal(canal), Observacao = Utilidades.Limpo(observacao, 500),
                Pagamento = valor <= 0 ? StatusPagamento.Cortesia : pagamento ?? StatusPagamento.Pendente
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
        var i = await db.Inscricoes.Include(x => x.TipoIngresso).ThenInclude(t => t!.Espaco)
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
