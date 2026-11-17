using Plateia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Application;

/// <summary>
/// Pagamento online das inscrições. Regra de ouro: aviso de pagamento nunca é aceito "de boca";
/// a situação é sempre confirmada consultando o link na Cielo (o link é exclusivo de cada inscrição).
/// </summary>
public class PagamentoService(IAppDbContext db, IPagamentoOnline gateway)
{
    public bool Online => gateway.Configurado;

    /// <summary>Endereço da página de pagamento da Cielo para esta inscrição (cria o link na primeira vez).</summary>
    public async Task<string> UrlPagamentoAsync(Inscricao i)
    {
        if (!gateway.Configurado) throw new RegraNegocioException("Pagamento online ainda não está disponível. Siga as instruções do ingresso.");
        if (!i.Ativa) throw new RegraNegocioException("Esta inscrição está cancelada.");
        if (i.Pagamento != StatusPagamento.Pendente || i.Valor <= 0) throw new RegraNegocioException("Este ingresso já está pago.");
        if (i.CieloLinkUrl is { Length: > 0 } url) return url;

        var ev = i.Evento ?? await db.Eventos.FirstAsync(e => e.Id == i.EventoId);
        var tipo = i.TipoIngresso ?? await db.TiposIngresso.FirstAsync(t => t.Id == i.TipoIngressoId);
        if (ev.Status is StatusEvento.Encerrado or StatusEvento.Cancelado || (ev.FimEm ?? ev.InicioEm) < DateTime.UtcNow)
            throw new RegraNegocioException("Este evento já aconteceu ou foi cancelado.");

        // O link vale até o dia do evento (no mínimo até amanhã).
        var expira = ev.InicioEm.Date < DateTime.UtcNow.Date.AddDays(1) ? DateTime.UtcNow.Date.AddDays(1) : ev.InicioEm.Date;
        var link = await gateway.CriarLinkAsync($"{ev.Titulo} · {tipo.Nome}", $"Ingresso {i.Codigo} · {i.Nome}", i.Valor, i.Codigo, expira);
        i.CieloLinkId = link.Id;
        i.CieloLinkUrl = link.Url;
        await db.SaveChangesAsync();
        return link.Url;
    }

    /// <summary>Consulta a Cielo e atualiza a inscrição. Sem <paramref name="forcar"/>, no máximo uma consulta por minuto.</summary>
    public async Task<bool> SincronizarAsync(Inscricao i, bool forcar = false)
    {
        if (!gateway.Configurado || i.CieloLinkId is null) return false;
        if (!forcar && i.CieloConsultadoEm is { } c && DateTime.UtcNow - c < TimeSpan.FromMinutes(1)) return false;
        i.CieloConsultadoEm = DateTime.UtcNow;
        var r = await gateway.ConsultarLinkAsync(i.CieloLinkId);
        var mudou = r is not null && await AplicarAsync(i, r);
        await db.SaveChangesAsync();
        return mudou;
    }

    /// <summary>
    /// Aviso da Cielo (notificação). Localiza a inscrição pelo link ou pelo número do pedido e confirma
    /// a situação consultando o próprio link. Retorna falso se não reconheceu o aviso.
    /// </summary>
    public async Task<bool> ProcessarAvisoAsync(string? linkId, string? pedido, string? forma)
    {
        Inscricao? i = null;
        if (!string.IsNullOrWhiteSpace(linkId)) i = await db.Inscricoes.FirstOrDefaultAsync(x => x.CieloLinkId == linkId);
        if (i is null && !string.IsNullOrWhiteSpace(pedido)) i = await db.Inscricoes.FirstOrDefaultAsync(x => x.CieloPedido == pedido);
        if (i?.CieloLinkId is null) return false;

        i.CieloConsultadoEm = DateTime.UtcNow;
        var r = await gateway.ConsultarLinkAsync(i.CieloLinkId);
        if (r is null) return false;
        if (r.Forma is null && r.Pedido is { } p)
            r = r with { Forma = forma ?? (await gateway.ConsultarPedidoAsync(p))?.Forma };
        await AplicarAsync(i, r);
        await db.SaveChangesAsync();
        return true;
    }

    async Task<bool> AplicarAsync(Inscricao i, PagamentoOnline p)
    {
        if (p.Pedido is { Length: > 0 } pedido && i.CieloPedido != pedido)
        {
            // Um mesmo pedido nunca vale para dois ingressos.
            if (await db.Inscricoes.AnyAsync(x => x.CieloPedido == pedido && x.Id != i.Id)) return false;
            i.CieloPedido = pedido;
        }

        switch (p.Situacao)
        {
            case SituacaoOnline.Pago when i.Pagamento == StatusPagamento.Pendente:
                if (p.Valor is { } v && v + 0.009m < i.Valor) return false; // pagou menos do que deve: não libera
                if (!i.Ativa)
                {
                    i.Observacao = Utilidades.Limpo($"Pagamento recebido pela Cielo depois do cancelamento (pedido {i.CieloPedido}). Avaliar estorno. {i.Observacao}", 500);
                    return true;
                }
                InscricaoService.DefinirPagamento(i, StatusPagamento.Pago, p.Forma ?? "Cielo");
                return true;

            case SituacaoOnline.Estornado when i.Pagamento == StatusPagamento.Pago && (i.FormaPagamento ?? "").StartsWith("Cielo"):
                i.Pagamento = StatusPagamento.Pendente;
                i.PagoEm = null;
                i.Observacao = Utilidades.Limpo($"Pagamento estornado/cancelado na Cielo (pedido {i.CieloPedido}). {i.Observacao}", 500);
                return true;
        }
        return false;
    }
}
