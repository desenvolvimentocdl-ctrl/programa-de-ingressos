using System.Net;
using System.Net.Mail;
using System.Text;
using Plateia.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Plateia.Infrastructure.Email;

public class EmailOptions
{
    /// <summary>Servidor SMTP (ex.: smtp.gmail.com, smtp.office365.com). Vazio = não envia, grava na pasta de teste.</summary>
    public string Host { get; set; } = "";
    public int Porta { get; set; } = 587;
    public string Usuario { get; set; } = "";
    public string Senha { get; set; } = "";
    /// <summary>Endereço que aparece como remetente (vazio = o mesmo do usuário).</summary>
    public string Remetente { get; set; } = "";
    public string NomeRemetente { get; set; } = "Plateia";
    public bool Ssl { get; set; } = true;
    /// <summary>Sem SMTP, os e-mails viram arquivos .txt nesta pasta (para testar o "esqueci minha senha" sem servidor de e-mail).</summary>
    public string PastaTeste { get; set; } = "";
}

public class SmtpEnvioEmail(IOptions<EmailOptions> opcoes, ILogger<SmtpEnvioEmail> log) : IEnvioEmail
{
    readonly EmailOptions o = opcoes.Value;

    public bool Configurado => o.Host.Length > 0;

    public async Task EnviarAsync(string para, string assunto, string texto, CancellationToken ct = default)
    {
        if (!Configurado)
        {
            if (o.PastaTeste.Length == 0)
            {
                log.LogWarning("E-mail para {Para} não enviado: configure Email__Host (SMTP) ou Email__PastaTeste.", para);
                throw new RegraNegocioException("O envio de e-mails ainda não foi configurado neste site. Fale com a equipe do Plateia.");
            }
            Directory.CreateDirectory(o.PastaTeste);
            var arquivo = Path.Combine(o.PastaTeste, $"{DateTime.Now:yyyyMMdd-HHmmss}-{new string(para.Where(char.IsLetterOrDigit).ToArray())}.txt");
            await File.WriteAllTextAsync(arquivo, $"Para: {para}\nAssunto: {assunto}\n\n{texto}", Encoding.UTF8, ct);
            log.LogInformation("SMTP não configurado: e-mail para {Para} gravado em {Arquivo}", para, arquivo);
            return;
        }

        using var msg = new MailMessage
        {
            From = new MailAddress(o.Remetente.Length > 0 ? o.Remetente : o.Usuario, o.NomeRemetente),
            Subject = assunto, Body = texto, IsBodyHtml = false,
            BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8
        };
        msg.To.Add(para);
        using var smtp = new SmtpClient(o.Host, o.Porta) { EnableSsl = o.Ssl, DeliveryMethod = SmtpDeliveryMethod.Network };
        if (o.Usuario.Length > 0) smtp.Credentials = new NetworkCredential(o.Usuario, o.Senha);
        try { await smtp.SendMailAsync(msg, ct); }
        catch (SmtpException ex)
        {
            log.LogError(ex, "Falha ao enviar e-mail para {Para}", para);
            throw new RegraNegocioException("Não conseguimos enviar o e-mail agora. Tente de novo em instantes.");
        }
    }
}
