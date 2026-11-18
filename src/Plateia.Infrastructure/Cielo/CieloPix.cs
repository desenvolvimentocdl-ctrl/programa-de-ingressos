using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Plateia.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Plateia.Infrastructure.Cielo;

/// <summary>
/// Pix pela API E-commerce Cielo 3.0: gera a cobrança (QR Code + copia e cola) e consulta a situação.
/// Documentação: docs.cielo.com.br/ecommerce-cielo/docs/pix
/// </summary>
public class CieloPix(HttpClient http, IOptions<CieloOptions> opcoes, ILogger<CieloPix> log) : IPagamentoPix
{
    readonly CieloOptions o = opcoes.Value;

    public bool Configurado => o.MerchantId.Length > 0 && o.MerchantKey.Length > 0;

    string Api => o.Sandbox ? "https://apisandbox.cieloecommerce.cielo.com.br" : "https://api.cieloecommerce.cielo.com.br";
    string ApiConsulta => o.Sandbox ? "https://apiquerysandbox.cieloecommerce.cielo.com.br" : "https://apiquery.cieloecommerce.cielo.com.br";

    HttpRequestMessage Requisicao(HttpMethod metodo, string url, object? corpo = null)
    {
        var req = new HttpRequestMessage(metodo, url);
        req.Headers.Add("MerchantId", o.MerchantId);
        req.Headers.Add("MerchantKey", o.MerchantKey);
        req.Headers.Add("RequestId", Guid.NewGuid().ToString());
        if (corpo is not null) req.Content = new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json");
        return req;
    }

    public async Task<PixGerado> CriarAsync(string referencia, decimal valor, string nomePagador, string cpf, CancellationToken ct = default)
    {
        var corpo = new
        {
            MerchantOrderId = referencia.Length > 50 ? referencia[..50] : referencia,
            Customer = new { Name = nomePagador.Length > 255 ? nomePagador[..255] : nomePagador, Identity = cpf, IdentityType = "CPF" },
            Payment = new { Type = "Pix", Amount = (long)Math.Round(valor * 100m) }
        };
        using var req = Requisicao(HttpMethod.Post, $"{Api}/1/sales/", corpo);
        using var r = await http.SendAsync(req, ct);
        var texto = await r.Content.ReadAsStringAsync(ct);
        if (!r.IsSuccessStatusCode)
        {
            log.LogError("Cielo recusou o Pix: {Status} {Corpo}", (int)r.StatusCode, texto);
            throw new RegraNegocioException("A Cielo não gerou o Pix agora. Confira o CPF e tente de novo em instantes.");
        }
        var pg = JsonDocument.Parse(texto).RootElement.GetProperty("Payment");
        var id = Texto(pg, "PaymentId");
        var copiaCola = Texto(pg, "QrCodeString");
        if (id is null || copiaCola is null)
        {
            log.LogError("Resposta do Pix sem QR Code: {Corpo}", texto);
            throw new RegraNegocioException(Texto(pg, "ReturnMessage") is { } m ? $"A Cielo não gerou o Pix: {m}" : "A Cielo não gerou o Pix agora. Tente de novo em instantes.");
        }
        return new PixGerado(id, copiaCola);
    }

    public async Task<PagamentoOnline?> ConsultarAsync(string paymentId, CancellationToken ct = default)
    {
        using var req = Requisicao(HttpMethod.Get, $"{ApiConsulta}/1/sales/{Uri.EscapeDataString(paymentId)}");
        using var r = await http.SendAsync(req, ct);
        if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!r.IsSuccessStatusCode) { log.LogWarning("Consulta do Pix {Id} na Cielo falhou: {Status}", paymentId, (int)r.StatusCode); return null; }
        var j = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!j.TryGetProperty("Payment", out var pg)) return null;
        var status = pg.TryGetProperty("Status", out var s) && s.TryGetInt32(out var n) ? n : -1;
        decimal? valor = pg.TryGetProperty("Amount", out var a) && a.TryGetInt64(out var c) ? c / 100m : null;
        return new PagamentoOnline(Situacao(status), paymentId, valor, "Cielo · Pix");
    }

    /// <summary>Status da API 3.0: 2 = pago; 10/11 = cancelado/estornado; 3/13 = negado/abortado; 12 = pendente.</summary>
    public static SituacaoOnline Situacao(int status) => status switch
    {
        2 => SituacaoOnline.Pago,
        10 or 11 => SituacaoOnline.Estornado,
        3 or 13 => SituacaoOnline.Negado,
        _ => SituacaoOnline.Aguardando
    };

    static string? Texto(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
