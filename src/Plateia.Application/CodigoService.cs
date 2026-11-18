using System.Security.Cryptography;
using System.Text;
using Plateia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Application;

/// <summary>Envio de e-mail (SMTP). Sem servidor configurado, o e-mail é gravado numa pasta de teste.</summary>
public interface IEnvioEmail
{
    bool Configurado { get; }
    Task EnviarAsync(string para, string assunto, string texto, CancellationToken ct = default);
}

/// <summary>
/// Códigos de 6 dígitos por e-mail (esqueci a senha, confirmar e-mail).
/// Vale 15 minutos, aceita no máximo 5 tentativas, um novo pedido só depois de 1 minuto e o código anterior deixa de valer.
/// Só o hash do código fica no banco.
/// </summary>
public class CodigoService(IAppDbContext db, IEnvioEmail email)
{
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(15);
    const int MaxTentativas = 5;

    public async Task EnviarAsync(string destino, FinalidadeCodigo finalidade, string? nome = null)
    {
        var e = destino.Trim().ToLowerInvariant();
        var agora = DateTime.UtcNow;
        var anteriores = await db.CodigosEmail.Where(c => c.Email == e && c.Finalidade == finalidade && c.UsadoEm == null).ToListAsync();
        if (anteriores.Any(c => agora - c.CriadoEm < TimeSpan.FromMinutes(1)))
            throw new RegraNegocioException("Acabamos de enviar um código. Aguarde 1 minuto antes de pedir outro.");
        foreach (var c in anteriores) c.UsadoEm = agora; // só o último código vale

        var codigo = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000");
        db.CodigosEmail.Add(new CodigoEmail { Email = e, Finalidade = finalidade, Hash = Hash(e, finalidade, codigo), ExpiraEm = agora + Validade });
        await db.SaveChangesAsync();

        var ola = string.IsNullOrWhiteSpace(nome) ? "Olá!" : $"Olá, {nome.Split(' ')[0]}!";
        var (assunto, motivo) = finalidade == FinalidadeCodigo.RedefinirSenha
            ? ($"{codigo} é o seu código para redefinir a senha · Plateia", "Recebemos um pedido para redefinir a senha da sua conta no Plateia.")
            : ($"{codigo} é o seu código de confirmação · Plateia", "Use o código abaixo para confirmar o seu e-mail no Plateia.");
        var texto = $"""
            {ola}

            {motivo}

            Seu código: {codigo}

            Ele vale por {(int)Validade.TotalMinutes} minutos. Se não foi você que pediu, ignore este e-mail: nada muda na sua conta.

            Plateia
            """;
        await email.EnviarAsync(e, assunto, texto);
    }

    /// <summary>Confere o código. Acertou: o código é consumido e não vale de novo.</summary>
    public async Task<bool> ValidarAsync(string destino, FinalidadeCodigo finalidade, string? codigo)
    {
        var e = destino.Trim().ToLowerInvariant();
        var digitos = new string((codigo ?? "").Where(char.IsDigit).ToArray());
        var c = await db.CodigosEmail.Where(x => x.Email == e && x.Finalidade == finalidade && x.UsadoEm == null)
            .OrderByDescending(x => x.CriadoEm).FirstOrDefaultAsync();
        if (c is null || c.ExpiraEm < DateTime.UtcNow) return false;

        c.Tentativas++;
        var certo = digitos.Length == 6 && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(c.Hash), Encoding.ASCII.GetBytes(Hash(e, finalidade, digitos)));
        if (certo || c.Tentativas >= MaxTentativas) c.UsadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return certo;
    }

    static string Hash(string email, FinalidadeCodigo f, string codigo) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{email}|{(int)f}|{codigo}")));
}
