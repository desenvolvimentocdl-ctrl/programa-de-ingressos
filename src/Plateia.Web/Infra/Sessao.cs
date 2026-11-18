using System.Security.Claims;
using Plateia.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Plateia.Web.Infra;

/// <summary>Abre a sessão (cookie) e decide a página inicial de cada perfil.</summary>
public static class Sessao
{
    public static Task EntrarAsync(HttpContext http, Usuario u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Nome),
            new(ClaimTypes.Email, u.Email),
            new(ClaimTypes.Role, u.Perfil.ToString())
        };
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    }

    /// <summary>Público vai para "Meus ingressos"; produtor e administrador, para o painel.</summary>
    public static string Inicio(Usuario u) => u.Perfil == Perfil.Participante ? "/meus-ingressos" : "/painel";

    public static bool Produtor(ClaimsPrincipal u) =>
        u.IsInRole(nameof(Perfil.Organizador)) || u.IsInRole(nameof(Perfil.Administrador));
}
