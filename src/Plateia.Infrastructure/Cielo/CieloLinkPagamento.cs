using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Plateia.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Plateia.Infrastructure.Cielo;

public class CieloOptions
{
    /// <summary>Endereço da API (produção). Só muda em testes.</summary>
    public string BaseUrl { get; set; } = "https://cieloecommerce.cielo.com.br";
    /// <summary>Credenciais do Link de Pagamento (Backoffice Cielo → Configurações → Dados cadastrais → Credenciais de API).</summary>
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    /// <summary>Texto na fatura do cartão (até 13 caracteres, sem acento).</summary>
    public string SoftDescriptor { get; set; } = "";
    public int MaxParcelas { get; set; } = 1;
    /// <summary>Chave secreta exigida no endereço de notificação (?chave=...). Opcional, mas recomendada.</summary>
    public string ChaveNotificacao { get; set; } = "";

    // API E-commerce Cielo 3.0 (Pix na própria página). Credenciais: Backoffice Cielo → Configurações → Dados cadastrais.
    public string MerchantId { get; set; } = "";
    public string MerchantKey { get; set; } = "";
    /// <summary>true = ambiente de testes da Cielo (sandbox), com credenciais de sandbox.</summary>
    public bool Sandbox { get; set; }
}

/// <summary>
/// Link de Pagamento da Cielo (Checkout Cielo). A pessoa paga em uma página da própria Cielo
/// (crédito, débito ou Pix, conforme a conta); nenhum dado de cartão passa pelo Plateia.
/// Documentação: docs.cielo.com.br/link
/// </summary>
public class CieloLinkPagamento(HttpClient http, IOptions<CieloOptions> opcoes, ILogger<CieloLinkPagamento> log) : IPagamentoOnline
{
    readonly CieloOptions o = opcoes.Value;
    static readonly SemaphoreSlim Trava = new(1, 1);
    static (string Token, DateTime Expira)? _token;

    public bool Configurado => o.ClientId.Length > 0 && o.ClientSecret.Length > 0;

    async Task<string> TokenAsync(CancellationToken ct)
    {
        if (_token is { } t && t.Expira > DateTime.UtcNow) return t.Token;
        await Trava.WaitAsync(ct);
        try
        {
            if (_token is { } t2 && t2.Expira > DateTime.UtcNow) return t2.Token;
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{o.BaseUrl}/api/public/v2/token")
            {
                Content = new FormUrlEncodedContent([new("grant_type", "client_credentials")])
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.ClientId}:{o.ClientSecret}")));
            using var r = await http.SendAsync(req, ct);
            if (!r.IsSuccessStatusCode)
            {
                log.LogError("Cielo recusou o token: {Status} {Corpo}", (int)r.StatusCode, await r.Content.ReadAsStringAsync(ct));
                throw new RegraNegocioException("Não foi possível conectar à Cielo. Confira as credenciais (ClientId/ClientSecret).");
            }
            var j = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
            var token = j.GetProperty("access_token").GetString()!;
            var segundos = j.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 1200;
            _token = (token, DateTime.UtcNow.AddSeconds(Math.Max(60, segundos - 60)));
            return token;
        }
        finally { Trava.Release(); }
    }

    async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string caminho, object? corpo, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(metodo, $"{o.BaseUrl}{caminho}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
        // StringContent manda Content-Length (sem "chunked"), que é o que gateways de pagamento esperam.
        if (corpo is not null) req.Content = new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json");
        return await http.SendAsync(req, ct);
    }

    public async Task<LinkPagamento> CriarLinkAsync(string nome, string descricao, decimal valor, string referencia, DateTime expiraEm, CancellationToken ct = default)
    {
        var corpo = new Dictionary<string, object?>
        {
            ["type"] = "Digital",
            ["name"] = Cortar(nome, 128),
            ["description"] = Cortar(descricao, 256),
            ["showDescription"] = true,
            ["price"] = (long)Math.Round(valor * 100m),
            ["quantity"] = 1,
            ["sku"] = Cortar(referencia, 32),
            ["expirationDate"] = expiraEm.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["maxNumberOfInstallments"] = Math.Clamp(o.MaxParcelas, 1, 12),
            ["shipping"] = new { type = "WithoutShipping", name = "Ingresso digital", price = 0 },
        };
        if (o.SoftDescriptor.Length > 0) corpo["softDescriptor"] = Cortar(o.SoftDescriptor, 13);

        using var r = await EnviarAsync(HttpMethod.Post, "/api/public/v1/products/", corpo, ct);
        var texto = await r.Content.ReadAsStringAsync(ct);
        if (!r.IsSuccessStatusCode)
        {
            log.LogError("Cielo recusou o link de pagamento: {Status} {Corpo}", (int)r.StatusCode, texto);
            throw new RegraNegocioException("A Cielo não gerou o link de pagamento agora. Tente de novo em instantes.");
        }
        var j = JsonDocument.Parse(texto).RootElement;
        return new LinkPagamento(Texto(j, "id")!, Texto(j, "shortUrl")!);
    }

    public async Task<PagamentoOnline?> ConsultarLinkAsync(string linkId, CancellationToken ct = default)
    {
        using var r = await EnviarAsync(HttpMethod.Get, $"/api/public/v1/products/{Uri.EscapeDataString(linkId)}/payments", null, ct);
        if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!r.IsSuccessStatusCode) { log.LogWarning("Consulta do link {Id} na Cielo falhou: {Status}", linkId, (int)r.StatusCode); return null; }
        var j = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!j.TryGetProperty("orders", out var pedidos) || pedidos.ValueKind != JsonValueKind.Array) return null;

        PagamentoOnline? melhor = null;
        foreach (var p in pedidos.EnumerateArray())
        {
            var pg = p.TryGetProperty("payment", out var x) ? x : default;
            var atual = new PagamentoOnline(
                Situacao(pg.ValueKind == JsonValueKind.Object ? Texto(pg, "status") : null),
                Texto(p, "orderNumber"),
                pg.ValueKind == JsonValueKind.Object && pg.TryGetProperty("price", out var pr) && pr.TryGetInt64(out var c) ? c / 100m : null,
                null);
            if (melhor is null || Peso(atual.Situacao) > Peso(melhor.Situacao)) melhor = atual;
        }
        return melhor;

        static int Peso(SituacaoOnline s) => s switch { SituacaoOnline.Pago => 3, SituacaoOnline.Estornado => 2, SituacaoOnline.Aguardando => 1, _ => 0 };
    }

    public async Task<PagamentoOnline?> ConsultarPedidoAsync(string pedido, CancellationToken ct = default)
    {
        using var r = await EnviarAsync(HttpMethod.Get, $"/api/public/v2/orders/{Uri.EscapeDataString(pedido)}", null, ct);
        if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"Cielo respondeu {(int)r.StatusCode} na consulta do pedido.");
        var j = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        var pg = j.TryGetProperty("payment", out var x) ? x : default;
        decimal? valor = null;
        if (j.TryGetProperty("cart", out var cart) && cart.TryGetProperty("items", out var itens) && itens.ValueKind == JsonValueKind.Array)
            valor = itens.EnumerateArray().Sum(i => (i.TryGetProperty("unitPrice", out var u) && u.TryGetInt64(out var v) ? v : 0)
                                                  * (i.TryGetProperty("quantity", out var q) && q.TryGetInt32(out var n) ? n : 1)) / 100m;
        var tipo = pg.ValueKind == JsonValueKind.Object ? Texto(pg, "type") : null;
        return new PagamentoOnline(Situacao(pg.ValueKind == JsonValueKind.Object ? Texto(pg, "status") : null), pedido, valor is > 0 ? valor : null, NomeForma(tipo));
    }

    /// <summary>A Cielo usa números (notificação) ou textos (consulta) para a situação.</summary>
    public static SituacaoOnline Situacao(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "2" or "paid" or "pago" or "7" or "authorized" or "autorizado" => SituacaoOnline.Pago,
        "3" or "denied" or "negado" or "4" or "expired" or "expirado" or "6" or "notfinalized" or "não finalizado" => SituacaoOnline.Negado,
        "5" or "voided" or "canceled" or "cancelled" or "cancelado" or "8" or "chargeback" => SituacaoOnline.Estornado,
        _ => SituacaoOnline.Aguardando
    };

    /// <summary>payment_method_type da notificação, ou payment.type da consulta.</summary>
    public static string? NomeForma(string? t) => (t ?? "").Trim().ToLowerInvariant() switch
    {
        "1" or "creditcard" => "Cielo · crédito",
        "2" or "boleto" => "Cielo · boleto",
        "3" or "onlinedebit" => "Cielo · débito online",
        "4" or "debitcard" => "Cielo · débito",
        "5" or "qrcode" => "Cielo · QR code",
        "6" or "pix" => "Cielo · Pix",
        "" => null,
        _ => "Cielo"
    };

    static string? Texto(JsonElement e, string nome)
    {
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, nome, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
        return null;
    }

    static string Cortar(string s, int max)
    {
        s = s.Trim();
        return s.Length <= max ? s : s[..max];
    }
}
