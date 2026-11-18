using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Plateia.Application;

public static partial class Utilidades
{
    // Sem 0/O, 1/I/L: fácil de ler e de digitar na portaria.
    const string Alfabeto = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>Código do ingresso: 8 caracteres aleatórios (31^8 ≈ 850 bilhões de combinações).</summary>
    public static string NovoCodigo()
    {
        Span<char> c = stackalloc char[8];
        for (var i = 0; i < c.Length; i++) c[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
        return new string(c);
    }

    /// <summary>Normaliza o que foi digitado ou lido no QR (aceita o link inteiro do ingresso).</summary>
    public static string NormalizarCodigo(string? entrada)
    {
        var t = (entrada ?? "").Trim();
        var barra = t.TrimEnd('/').LastIndexOf('/');
        if (barra >= 0) t = t[(barra + 1)..];
        var q = t.IndexOfAny(['?', '#']);
        if (q >= 0) t = t[..q];
        return new string(t.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    public static string Slug(string texto)
    {
        var semAcento = new StringBuilder();
        foreach (var ch in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) semAcento.Append(ch);
        var s = NaoAlfanumerico().Replace(semAcento.ToString().ToLowerInvariant(), "-").Trim('-');
        if (s.Length > 60) s = s[..60].TrimEnd('-');
        return s.Length == 0 ? "evento" : s;
    }

    public static string? SoDigitos(string? t) => t is null ? null : new string(t.Where(char.IsDigit).ToArray()) is { Length: > 0 } d ? d : null;

    public static string? Limpo(string? t, int max) => string.IsNullOrWhiteSpace(t) ? null : (t.Trim().Length > max ? t.Trim()[..max] : t.Trim());

    /// <summary>CPF com 11 dígitos e dígitos verificadores corretos.</summary>
    public static bool CpfValido(string? cpf)
    {
        if (cpf is not { Length: 11 } || !cpf.All(char.IsDigit) || cpf.Distinct().Count() == 1) return false;
        int Dv(int n) { var s = 0; for (var i = 0; i < n; i++) s += (cpf[i] - '0') * (n + 1 - i); var r = s * 10 % 11; return r == 10 ? 0 : r; }
        return Dv(9) == cpf[9] - '0' && Dv(10) == cpf[10] - '0';
    }

    public static bool EmailValido(string? e) => e is not null && EmailRegex().IsMatch(e);

    public static bool CorValida(string? c) => c is not null && CorRegex().IsMatch(c);

    [GeneratedRegex("[^a-z0-9]+")] private static partial Regex NaoAlfanumerico();
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")] private static partial Regex EmailRegex();
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")] private static partial Regex CorRegex();
}
