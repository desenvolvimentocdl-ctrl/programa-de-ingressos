namespace Plateia.Application;

/// <summary>Situação de um pagamento online, já traduzida para o que importa ao Plateia.</summary>
public enum SituacaoOnline { Aguardando, Pago, Negado, Estornado }

public record LinkPagamento(string Id, string Url);

public record PagamentoOnline(SituacaoOnline Situacao, string? Pedido, decimal? Valor, string? Forma);

/// <summary>Gateway de pagamento online (implementado com o Link de Pagamento da Cielo).</summary>
public interface IPagamentoOnline
{
    /// <summary>Falso quando as credenciais não foram configuradas: o sistema volta às instruções manuais.</summary>
    bool Configurado { get; }
    Task<LinkPagamento> CriarLinkAsync(string nome, string descricao, decimal valor, string referencia, DateTime expiraEm, CancellationToken ct = default);
    /// <summary>Melhor situação entre os pedidos feitos com o link (pago vence aguardando).</summary>
    Task<PagamentoOnline?> ConsultarLinkAsync(string linkId, CancellationToken ct = default);
    Task<PagamentoOnline?> ConsultarPedidoAsync(string pedido, CancellationToken ct = default);
}

public record PixGerado(string PaymentId, string CopiaCola);

/// <summary>Pix gerado na própria página do pedido (QR Code + copia e cola), pela API E-commerce da Cielo.</summary>
public interface IPagamentoPix
{
    /// <summary>Falso sem MerchantId/MerchantKey: o pedido mostra só o pagamento por cartão (ou as instruções manuais).</summary>
    bool Configurado { get; }
    Task<PixGerado> CriarAsync(string referencia, decimal valor, string nomePagador, string cpf, CancellationToken ct = default);
    Task<PagamentoOnline?> ConsultarAsync(string paymentId, CancellationToken ct = default);
}

public record ResultadoCartao(bool Aprovado, string? PaymentId, string Mensagem);

/// <summary>Cartão digitado na página de checkout do Plateia (o cartão vai direto do navegador para a Cielo; aqui só chega um token).</summary>
public interface IPagamentoCartao
{
    bool Configurado { get; }
    bool Sandbox { get; }
    int MaxParcelas { get; }
    /// <summary>Endereço do script da Cielo que a página carrega para enviar o cartão.</summary>
    string ScriptSop { get; }
    Task<string?> TokenAcessoAsync(CancellationToken ct = default);
    Task<ResultadoCartao> PagarAsync(string referencia, decimal valor, int parcelas, string nomeComprador, string paymentToken, string bandeira, CancellationToken ct = default);
}
