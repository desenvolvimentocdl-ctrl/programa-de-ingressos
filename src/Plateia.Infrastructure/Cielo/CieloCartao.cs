using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Plateia.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Plateia.Infrastructure.Cielo;

/// <summary>
/// Cartão de crédito digitado na página de checkout do Plateia, sem os dados do cartão passarem pelo servidor:
/// o navegador envia o cartão direto para a Cielo (Silent Order Post) e recebe um PaymentToken;
/// o servidor só cobra com esse token pela API E-commerce Cielo 3.0.
/// Documentação: docs.cielo.com.br/ecommerce-cielo/docs/silent-order-post
/// O Silent Order Post precisa estar habilitado na conta (peça ao suporte da Cielo).
/// </summary>
public class CieloCartao(HttpClient http, IOptions<CieloOptions> opcoes, ILogger<CieloCartao> log) : IPagamentoCartao
{
    readonly CieloOptions o = opcoes.Value;

    public bool Configurado => o.MerchantId.Length > 0 && o.MerchantKey.Length > 0;
    public bool Sandbox => o.Sandbox;
    public int MaxParcelas => Math.Clamp(o.MaxParcelas, 1, 12);

    string Api => o.Sandbox ? "https://apisandbox.cieloecommerce.cielo.com.br" : "https://api.cieloecommerce.cielo.com.br";
    string Sop => o.Sandbox ? "https://transactionsandbox.pagador.com.br" : "https://transaction.pagador.com.br";
    public string ScriptSop => $"{Sop}/post/scripts/silentorderpost-1.0.min.js";

    /// <summary>Token de acesso de curta duração que a página usa para enviar o cartão direto à Cielo.</summary>
    public async Task<string?> TokenAcessoAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Sop}/post/api/public/v1/accesstoken?merchantid={Uri.EscapeDataString(o.MerchantId)}");
        req.Headers.Add("MerchantId", o.MerchantId);
        req.Content = new StringContent("", Encoding.UTF8, "application/json");
        try
        {
            using var r = await http.SendAsync(req, ct);
            var texto = await r.Content.ReadAsStringAsync(ct);
            if (!r.IsSuccessStatusCode)
            {
                log.LogError("Cielo recusou o token do Silent Order Post: {Status} {Corpo}", (int)r.StatusCode, texto);
                return null;
            }
            var j = JsonDocument.Parse(texto).RootElement;
            return j.TryGetProperty("AccessToken", out var t) ? t.GetString() : null;
        }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Cielo indisponível ao gerar token do Silent Order Post");
            return null;
        }
    }

    public async Task<ResultadoCartao> PagarAsync(string referencia, decimal valor, int parcelas, string nomeComprador, string paymentToken, string bandeira, CancellationToken ct = default)
    {
        var corpo = new
        {
            MerchantOrderId = referencia.Length > 50 ? referencia[..50] : referencia,
            Customer = new { Name = nomeComprador.Length > 255 ? nomeComprador[..255] : nomeComprador },
            Payment = new
            {
                Type = "CreditCard",
                Amount = (long)Math.Round(valor * 100m),
                Installments = Math.Clamp(parcelas, 1, MaxParcelas),
                Capture = true,
                SoftDescriptor = o.SoftDescriptor.Length > 0 ? (o.SoftDescriptor.Length > 13 ? o.SoftDescriptor[..13] : o.SoftDescriptor) : null,
                CreditCard = new { PaymentToken = paymentToken, Brand = bandeira }
            }
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Api}/1/sales/");
        req.Headers.Add("MerchantId", o.MerchantId);
        req.Headers.Add("MerchantKey", o.MerchantKey);
        req.Headers.Add("RequestId", Guid.NewGuid().ToString());
        req.Content = new StringContent(JsonSerializer.Serialize(corpo, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }),
            Encoding.UTF8, "application/json");
        using var r = await http.SendAsync(req, ct);
        var texto = await r.Content.ReadAsStringAsync(ct);
        if (!r.IsSuccessStatusCode)
        {
            log.LogError("Cielo recusou a venda no cartão: {Status} {Corpo}", (int)r.StatusCode, texto);
            return new ResultadoCartao(false, null, "Não foi possível processar o cartão agora. Confira os dados ou tente outro cartão.");
        }
        var pg = JsonDocument.Parse(texto).RootElement.GetProperty("Payment");
        var status = pg.TryGetProperty("Status", out var s) && s.TryGetInt32(out var n) ? n : -1;
        var id = pg.TryGetProperty("PaymentId", out var p) ? p.GetString() : null;
        var msg = pg.TryGetProperty("ReturnMessage", out var m) ? m.GetString() : null;
        // 2 = pago (capturado). Com Capture=true, "autorizado" (1) não deveria ficar parado; tratamos como aprovado só o 2.
        return status == 2
            ? new ResultadoCartao(true, id, "Pagamento aprovado.")
            : new ResultadoCartao(false, id, MensagemRecusa(pg, msg));
    }

    /// <summary>Mensagem amigável para a recusa (sem expor detalhes técnicos).</summary>
    static string MensagemRecusa(JsonElement pg, string? msg)
    {
        var codigo = pg.TryGetProperty("ReturnCode", out var c) ? c.GetString() : null;
        return codigo switch
        {
            "05" or "57" or "78" => "O banco não autorizou a compra. Fale com o seu banco ou use outro cartão.",
            "51" => "Saldo ou limite insuficiente. Tente outro cartão ou pague com Pix.",
            "54" => "Cartão vencido. Confira a validade ou use outro cartão.",
            "14" or "N7" or "82" => "Dados do cartão inválidos. Confira o número, a validade e o código de segurança.",
            _ => string.IsNullOrWhiteSpace(msg) ? "Pagamento não aprovado. Tente outro cartão ou pague com Pix." : $"Pagamento não aprovado: {msg}."
        };
    }
}
