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
