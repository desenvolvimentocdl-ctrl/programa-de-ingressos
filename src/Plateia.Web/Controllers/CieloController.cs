using System.Security.Cryptography;
using System.Text;
using Plateia.Application;
using Plateia.Infrastructure.Cielo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Plateia.Web.Controllers;

/// <summary>
/// Endereços que a Cielo chama quando um pagamento é concluído ou muda de situação.
/// Configure no Backoffice da Cielo (formato POST):
///   URL de Notificação:        https://SEU-DOMINIO/cielo/notificacao?chave=CHAVE
///   URL de Mudança de Status:  https://SEU-DOMINIO/cielo/status?chave=CHAVE
/// O conteúdo do aviso não é aceito como verdade: a situação é sempre confirmada consultando a Cielo.
/// </summary>
[AllowAnonymous, IgnoreAntiforgeryToken]
public class CieloController(PagamentoService pagamentos, IOptions<CieloOptions> opcoes, ILogger<CieloController> log) : Controller
{
    [HttpPost("/cielo/notificacao"), HttpPost("/cielo/status")]
    public async Task<IActionResult> Aviso(string? chave)
    {
        var esperada = opcoes.Value.ChaveNotificacao;
        if (esperada.Length > 0 && !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(chave ?? ""), Encoding.UTF8.GetBytes(esperada)))
            return Unauthorized();
        if (!Request.HasFormContentType) return Ok(); // formato JSON não é usado: configure "POST" no Backoffice

        var f = Request.Form;
        string? Campo(string nome) => f[nome].FirstOrDefault() is { Length: > 0 } v ? v.Trim() : null;
        var linkId = Campo("product_id");
        var pedido = Campo("checkout_cielo_order_number");
        var forma = CieloLinkPagamento.NomeForma(Campo("payment_method_type"));
        try
        {
            var reconhecido = await pagamentos.ProcessarAvisoAsync(linkId, pedido, forma);
            if (!reconhecido) log.LogWarning("Aviso da Cielo não reconhecido (link {Link}, pedido {Pedido})", linkId, pedido);
            // 200 mesmo quando não reconhece, para a Cielo não reenviar avisos de outros produtos da conta.
            return Ok();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao processar aviso da Cielo (link {Link}, pedido {Pedido})", linkId, pedido);
            return StatusCode(500); // a Cielo tenta de novo
        }
    }
}
