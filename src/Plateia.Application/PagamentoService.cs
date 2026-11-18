using Plateia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Application;

/// <summary>
/// Pagamento online das inscrições. Regra de ouro: aviso de pagamento nunca é aceito "de boca";
/// a situação é sempre confirmada consultando o link na Cielo.
/// Ingressos comprados num pedido (checkout) são pagos juntos, com um único link pelo total do pedido;
/// inscrições avulsas (antigas ou manuais) continuam com um link próprio.
/// </summary>
public class PagamentoService(IAppDbContext db, IPagamentoOnline gateway, IPagamentoPix pix, IPagamentoCartao cartao)
{
    /// <summary>Algum pagamento online disponível (cartão pela Cielo ou Pix na página).</summary>
    public bool Online => gateway.Configurado || pix.Configurado;
    /// <summary>Cartão (e o que mais a conta tiver) na página segura da Cielo.</summary>
    public bool Cartao => gateway.Configurado;
    /// <summary>Pix com QR Code na própria página do pedido.</summary>
    public bool Pix => pix.Configurado;
    /// <summary>Cartão digitado na página de checkout do Plateia (Silent Order Post da Cielo).</summary>
    public bool CartaoNaPagina => cartao.Configurado;
    public int MaxParcelas => cartao.MaxParcelas;
    public bool Sandbox => cartao.Sandbox;
    public string ScriptCartao => cartao.ScriptSop;
    public Task<string?> TokenCartaoAsync() => cartao.TokenAcessoAsync();

    static readonly string[] Bandeiras = ["Visa", "Master", "Elo", "Amex", "Hipercard", "Diners", "Discover", "JCB", "Aura"];

    /// <summary>
    /// Cobra o que falta do pedido no cartão de crédito. Chega só o token que a Cielo devolveu ao navegador:
    /// número, validade e código de segurança do cartão nunca passam pelo Plateia.
    /// </summary>
    public async Task PagarComCartaoAsync(Pedido p, string? paymentToken, string? bandeira, int parcelas)
    {
        if (!cartao.Configurado) throw new RegraNegocioException("O pagamento com cartão ainda não está disponível.");
        var valor = p.APagar;
        if (valor <= 0) throw new RegraNegocioException("Este pedido já está pago.");
        var ev = p.Evento ?? await db.Eventos.FirstAsync(e => e.Id == p.EventoId);
        ValidarEvento(ev);
        if (string.IsNullOrWhiteSpace(paymentToken) || paymentToken.Length > 100)
            throw new RegraNegocioException("Não recebemos os dados do cartão. Preencha de novo e tente outra vez.");
        var marca = Bandeiras.FirstOrDefault(b => string.Equals(b, bandeira, StringComparison.OrdinalIgnoreCase))
                    ?? throw new RegraNegocioException("Não reconhecemos a bandeira do cartão. Confira o número.");
        parcelas = Math.Clamp(parcelas, 1, cartao.MaxParcelas);

        var r = await cartao.PagarAsync($"{p.Codigo}-C{DateTime.UtcNow:HHmmss}", valor, parcelas, p.Nome, paymentToken, marca);
        if (!r.Aprovado) throw new RegraNegocioException(r.Mensagem);
        var forma = parcelas > 1 ? $"Cielo · crédito {parcelas}x" : "Cielo · crédito";
        await AplicarAsync(p, new PagamentoOnline(SituacaoOnline.Pago, r.PaymentId, valor, forma));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Gera (ou reaproveita) o Pix do pedido pelo valor que falta. O mesmo Pix vale enquanto o valor não mudar
    /// (até 24 h); se mudar, um novo é gerado e o anterior continua sendo consultado, para nenhum pagamento se perder.
    /// </summary>
    public async Task<Pedido> GerarPixAsync(Pedido p, string? cpf)
    {
        if (!pix.Configurado) throw new RegraNegocioException("O pagamento por Pix ainda não está disponível.");
        var valor = p.APagar;
        if (valor <= 0) throw new RegraNegocioException("Este pedido não tem nada a pagar.");
        var ev = p.Evento ?? await db.Eventos.FirstAsync(e => e.Id == p.EventoId);
        ValidarEvento(ev);
        if (p.PixPaymentId is not null && p.PixValor == valor && p.PixGeradoEm > DateTime.UtcNow.AddHours(-24)) return p;

        var doc = Utilidades.SoDigitos(cpf) ?? "";
        if (!Utilidades.CpfValido(doc)) throw new RegraNegocioException("Informe um CPF válido de quem vai pagar (a Cielo exige no Pix).");
        var gerado = await pix.CriarAsync($"{p.Codigo}-{DateTime.UtcNow:HHmmss}", valor, p.Nome, doc);
        if (p.PixPaymentId is { } antigo)
            p.PixAnteriores = string.Join(",", (p.PixAnteriores ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Append(antigo).TakeLast(8));
        p.PixPaymentId = gerado.PaymentId;
        p.PixCopiaCola = gerado.CopiaCola;
        p.PixValor = valor;
        p.PixGeradoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return p;
    }

    /// <summary>Endereço da página de pagamento da Cielo para esta inscrição (cria o link na primeira vez).</summary>
    public async Task<string> UrlPagamentoAsync(Inscricao i)
    {
        if (i.PedidoId is { } pid) return await UrlPagamentoAsync(await CarregarPedidoAsync(pid));

        if (!gateway.Configurado) throw new RegraNegocioException("Pagamento online ainda não está disponível. Siga as instruções do ingresso.");
        if (!i.Ativa) throw new RegraNegocioException("Esta inscrição está cancelada.");
        if (i.Pagamento != StatusPagamento.Pendente || i.Valor <= 0) throw new RegraNegocioException("Este ingresso já está pago.");
        if (i.CieloLinkUrl is { Length: > 0 } url) return url;

        var ev = i.Evento ?? await db.Eventos.FirstAsync(e => e.Id == i.EventoId);
        var tipo = i.TipoIngresso ?? await db.TiposIngresso.FirstAsync(t => t.Id == i.TipoIngressoId);
        ValidarEvento(ev);

        var link = await gateway.CriarLinkAsync($"{ev.Titulo} · {tipo.Nome}", $"Ingresso {i.Codigo} · {i.Nome}", i.Valor, i.Codigo, Expiracao(ev));
        i.CieloLinkId = link.Id;
        i.CieloLinkUrl = link.Url;
        await db.SaveChangesAsync();
        return link.Url;
    }

    /// <summary>Endereço da página de pagamento da Cielo para o pedido inteiro (um pagamento pelo total que falta).</summary>
    public async Task<string> UrlPagamentoAsync(Pedido p)
    {
        if (!gateway.Configurado) throw new RegraNegocioException("Pagamento online ainda não está disponível. Siga as instruções do ingresso.");
        var valor = p.APagar;
        if (valor <= 0) throw new RegraNegocioException("Este pedido não tem nada a pagar.");
        // Reaproveita o link enquanto o valor for o mesmo; se algum ingresso foi cancelado, gera outro com o valor novo.
        if (p.CieloLinkUrl is { Length: > 0 } url && p.CieloLinkValor == valor) return url;

        var ev = p.Evento ?? await db.Eventos.FirstAsync(e => e.Id == p.EventoId);
        ValidarEvento(ev);
        var pendentes = p.Inscricoes.Where(i => i.Ativa && i.Pagamento == StatusPagamento.Pendente).ToList();
        var nome = pendentes.Count == 1 ? $"{ev.Titulo} · 1 ingresso" : $"{ev.Titulo} · {pendentes.Count} ingressos";
        var descricao = $"Pedido {p.Codigo} · " + string.Join(", ", pendentes.Select(i => i.Nome));
        var link = await gateway.CriarLinkAsync(nome, descricao, valor, p.Codigo, Expiracao(ev));
        p.CieloLinkId = link.Id;
        p.CieloLinkUrl = link.Url;
        p.CieloLinkValor = valor;
        await db.SaveChangesAsync();
        return link.Url;
    }

    static void ValidarEvento(Evento ev)
    {
        if (ev.Status is StatusEvento.Encerrado or StatusEvento.Cancelado || (ev.FimEm ?? ev.InicioEm) < DateTime.UtcNow)
            throw new RegraNegocioException("Este evento já aconteceu ou foi cancelado.");
    }

    /// <summary>O link vale até o dia do evento (no mínimo até amanhã).</summary>
    static DateTime Expiracao(Evento ev) =>
        ev.InicioEm.Date < DateTime.UtcNow.Date.AddDays(1) ? DateTime.UtcNow.Date.AddDays(1) : ev.InicioEm.Date;

    Task<Pedido> CarregarPedidoAsync(int id) =>
        db.Pedidos.Include(p => p.Evento).Include(p => p.Inscricoes).ThenInclude(i => i.TipoIngresso).FirstAsync(p => p.Id == id);

    /// <summary>Consulta a Cielo e atualiza a inscrição (ou o pedido dela). Sem <paramref name="forcar"/>, no máximo uma consulta por minuto.</summary>
    public async Task<bool> SincronizarAsync(Inscricao i, bool forcar = false)
    {
        if (i.PedidoId is { } pid) return await SincronizarAsync(await CarregarPedidoAsync(pid), forcar);
        if (!gateway.Configurado || i.CieloLinkId is null) return false;
        if (!forcar && i.CieloConsultadoEm is { } c && DateTime.UtcNow - c < TimeSpan.FromMinutes(1)) return false;
        i.CieloConsultadoEm = DateTime.UtcNow;
        var r = await gateway.ConsultarLinkAsync(i.CieloLinkId);
        var mudou = r is not null && await AplicarAsync(i, r);
        await db.SaveChangesAsync();
        return mudou;
    }

    /// <summary>Consulta o link de cartão (no máximo 1 vez por minuto) e os Pix do pedido (no máximo a cada 5 s, para a página que espera o Pix).</summary>
    public async Task<bool> SincronizarAsync(Pedido p, bool forcar = false)
    {
        var mudou = false;
        var agora = DateTime.UtcNow;
        if (gateway.Configurado && p.CieloLinkId is not null && (forcar || p.CieloConsultadoEm is not { } c || agora - c >= TimeSpan.FromMinutes(1)))
        {
            p.CieloConsultadoEm = agora;
            if (await gateway.ConsultarLinkAsync(p.CieloLinkId) is { } r) mudou |= await AplicarAsync(p, r);
        }
        if (pix.Configurado && p.PixPaymentId is not null && p.APagar > 0 && (forcar || p.PixConsultadoEm is not { } x || agora - x >= TimeSpan.FromSeconds(5)))
        {
            p.PixConsultadoEm = agora;
            foreach (var id in (p.PixAnteriores ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Prepend(p.PixPaymentId))
            {
                if (p.APagar <= 0) break;
                if (await pix.ConsultarAsync(id) is { Situacao: SituacaoOnline.Pago } r) mudou |= await AplicarAsync(p, r);
            }
        }
        await db.SaveChangesAsync();
        return mudou;
    }

    /// <summary>Aviso da Cielo (API 3.0) de que um Pix mudou de situação: confirma consultando a Cielo.</summary>
    public async Task<bool> ProcessarAvisoPixAsync(string? paymentId)
    {
        if (string.IsNullOrWhiteSpace(paymentId) || paymentId.Length > 64) return false;
        var p = await db.Pedidos.Include(x => x.Inscricoes)
            .FirstOrDefaultAsync(x => x.PixPaymentId == paymentId || (x.PixAnteriores != null && x.PixAnteriores.Contains(paymentId)));
        if (p is null) return false;
        await SincronizarAsync(p, forcar: true);
        return true;
    }

    /// <summary>
    /// Aviso da Cielo (notificação). Localiza o pedido ou a inscrição pelo link ou pelo número do pedido na Cielo
    /// e confirma a situação consultando o próprio link. Retorna falso se não reconheceu o aviso.
    /// </summary>
    public async Task<bool> ProcessarAvisoAsync(string? linkId, string? pedidoCielo, string? forma)
    {
        Pedido? p = null;
        if (!string.IsNullOrWhiteSpace(linkId)) p = await db.Pedidos.Include(x => x.Inscricoes).FirstOrDefaultAsync(x => x.CieloLinkId == linkId);
        if (p is null && !string.IsNullOrWhiteSpace(pedidoCielo)) p = await db.Pedidos.Include(x => x.Inscricoes).FirstOrDefaultAsync(x => x.CieloPedido == pedidoCielo);
        if (p?.CieloLinkId is not null)
        {
            p.CieloConsultadoEm = DateTime.UtcNow;
            var rp = await ConsultarComFormaAsync(p.CieloLinkId, forma);
            if (rp is null) return false;
            await AplicarAsync(p, rp);
            await db.SaveChangesAsync();
            return true;
        }

        Inscricao? i = null;
        if (!string.IsNullOrWhiteSpace(linkId)) i = await db.Inscricoes.FirstOrDefaultAsync(x => x.CieloLinkId == linkId);
        if (i is null && !string.IsNullOrWhiteSpace(pedidoCielo)) i = await db.Inscricoes.FirstOrDefaultAsync(x => x.CieloPedido == pedidoCielo);
        if (i?.CieloLinkId is null) return false;

        i.CieloConsultadoEm = DateTime.UtcNow;
        var r = await ConsultarComFormaAsync(i.CieloLinkId, forma);
        if (r is null) return false;
        await AplicarAsync(i, r);
        await db.SaveChangesAsync();
        return true;
    }

    async Task<PagamentoOnline?> ConsultarComFormaAsync(string linkId, string? forma)
    {
        var r = await gateway.ConsultarLinkAsync(linkId);
        if (r is { Forma: null, Pedido: { } numero })
            r = r with { Forma = forma ?? (await gateway.ConsultarPedidoAsync(numero))?.Forma };
        return r;
    }

    /// <summary>Um mesmo pedido da Cielo nunca vale para dois ingressos ou dois pedidos do Plateia.</summary>
    async Task<bool> PedidoCieloJaUsadoAsync(string numero, int? inscricaoId, int? pedidoId)
    {
        // Ingressos do próprio pedido recebem o mesmo número: não contam como "outro".
        if (await db.Inscricoes.AnyAsync(x => x.CieloPedido == numero && x.Id != inscricaoId && (pedidoId == null || x.PedidoId != pedidoId)))
            return true;
        return await db.Pedidos.AnyAsync(x => x.CieloPedido == numero && x.Id != pedidoId);
    }

    async Task<bool> AplicarAsync(Inscricao i, PagamentoOnline p)
    {
        if (p.Pedido is { Length: > 0 } numero && i.CieloPedido != numero)
        {
            if (await PedidoCieloJaUsadoAsync(numero, i.Id, null)) return false;
            i.CieloPedido = numero;
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

    /// <summary>Pagamento do pedido inteiro: libera (ou estorna) todos os ingressos dele de uma vez.</summary>
    async Task<bool> AplicarAsync(Pedido ped, PagamentoOnline p)
    {
        if (p.Pedido is { Length: > 0 } numero && ped.CieloPedido != numero)
        {
            if (await PedidoCieloJaUsadoAsync(numero, null, ped.Id)) return false;
            ped.CieloPedido = numero;
        }

        switch (p.Situacao)
        {
            case SituacaoOnline.Pago:
                var pendentes = ped.Inscricoes.Where(i => i.Ativa && i.Pagamento == StatusPagamento.Pendente).ToList();
                if (pendentes.Count == 0)
                {
                    // Já estava pago por outro meio (ex.: Pix e cartão): avisa o organizador para estornar um deles.
                    var aviso = $"Pedido {ped.Codigo} pago duas vezes na Cielo ({p.Forma ?? "Cielo"}, {p.Pedido}). Avaliar estorno.";
                    var marcados = ped.Inscricoes.Where(i => i.Ativa && i.Pagamento == StatusPagamento.Pago && p.Pedido is not null
                        && i.CieloPedido != p.Pedido && !(i.Observacao ?? "").Contains(p.Pedido)).ToList();
                    foreach (var i in marcados.Take(1)) i.Observacao = Utilidades.Limpo($"{aviso} {i.Observacao}", 500);
                    return marcados.Count > 0;
                }
                var devido = pendentes.Sum(i => i.Valor);
                if (p.Valor is { } v && v + 0.009m < devido) return false; // pagou menos do que deve: não libera
                foreach (var i in pendentes)
                {
                    i.CieloPedido = ped.CieloPedido;
                    InscricaoService.DefinirPagamento(i, StatusPagamento.Pago, p.Forma ?? "Cielo");
                }
                // Pagou a mais (o link foi criado antes de algum cancelamento): avisa o organizador no ingresso cancelado.
                if (p.Valor is { } pago && pago > devido + 0.009m)
                    foreach (var c in ped.Inscricoes.Where(i => !i.Ativa && i.Pagamento == StatusPagamento.Pendente))
                        c.Observacao = Utilidades.Limpo($"Pedido {ped.Codigo} pago na Cielo com o valor deste ingresso cancelado. Avaliar estorno. {c.Observacao}", 500);
                return true;

            case SituacaoOnline.Estornado:
                var pagos = ped.Inscricoes.Where(i => i.Pagamento == StatusPagamento.Pago && (i.FormaPagamento ?? "").StartsWith("Cielo")).ToList();
                foreach (var i in pagos)
                {
                    i.Pagamento = StatusPagamento.Pendente;
                    i.PagoEm = null;
                    i.Observacao = Utilidades.Limpo($"Pagamento do pedido {ped.Codigo} estornado/cancelado na Cielo (pedido Cielo {ped.CieloPedido}). {i.Observacao}", 500);
                }
                return pagos.Count > 0;
        }
        return false;
    }
}
