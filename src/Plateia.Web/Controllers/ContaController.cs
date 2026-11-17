using System.Security.Claims;
using Plateia.Domain;
using Plateia.Infrastructure;
using Plateia.Web.Infra;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Web.Controllers;

public class ContaController(AppDbContext db, IPasswordHasher<Usuario> hasher) : BaseController
{
    [AllowAnonymous, HttpGet("/entrar")]
    public IActionResult Entrar(string? returnUrl) => View(model: returnUrl);

    [AllowAnonymous, HttpPost("/entrar"), EnableRateLimiting("publico")]
    public async Task<IActionResult> Entrar(string email, string senha, string? returnUrl)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Email == e && x.Ativo);
        var ok = u is not null && hasher.VerifyHashedPassword(u, u.SenhaHash, senha ?? "") != PasswordVerificationResult.Failed;
        if (!ok)
        {
            await Task.Delay(400); // dificulta tentativa e erro
            ViewBag.Erro = "E-mail ou senha incorretos.";
            ViewBag.Email = email;
            return View(model: returnUrl);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u!.Id.ToString()),
            new(ClaimTypes.Name, u.Nome),
            new(ClaimTypes.Email, u.Email),
            new(ClaimTypes.Role, u.Perfil.ToString())
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }

    [HttpPost("/sair")]
    public async Task<IActionResult> Sair()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/entrar");
    }

    [HttpGet("/minha-conta")]
    public async Task<IActionResult> MinhaConta() => View(await db.Usuarios.FirstAsync(x => x.Id == Eu.Id));

    [HttpPost("/minha-conta")]
    public async Task<IActionResult> SalvarConta(string nome, string? telefone)
    {
        var u = await db.Usuarios.FirstAsync(x => x.Id == Eu.Id);
        u.Nome = Application.Utilidades.Limpo(nome, 120) ?? u.Nome;
        u.Telefone = Application.Utilidades.SoDigitos(telefone);
        await db.SaveChangesAsync();
        return VoltarCom("Dados salvos. O novo nome aparece no próximo acesso.");
    }

    [HttpPost("/minha-conta/senha")]
    public async Task<IActionResult> TrocarSenha(string atual, string nova, string confirmacao)
    {
        var u = await db.Usuarios.FirstAsync(x => x.Id == Eu.Id);
        if (hasher.VerifyHashedPassword(u, u.SenhaHash, atual ?? "") == PasswordVerificationResult.Failed)
            return VoltarCom("A senha atual não confere.");
        if ((nova ?? "").Length < 8) return VoltarCom("A nova senha precisa ter pelo menos 8 caracteres.");
        if (nova != confirmacao) return VoltarCom("A confirmação não confere com a nova senha.");
        u.SenhaHash = hasher.HashPassword(u, nova!);
        await db.SaveChangesAsync();
        return VoltarCom("Senha alterada.");
    }

    [AllowAnonymous, HttpGet("/sem-acesso")]
    public IActionResult SemAcesso() => View("Mensagem", "Seu perfil não tem acesso a esta página.");

    [AllowAnonymous, HttpGet("/erro")]
    public IActionResult Erro() => View("Mensagem", "Algo deu errado. Tente de novo em instantes.");

    [AllowAnonymous, IgnoreAntiforgeryToken, Route("/status/{codigo:int}")]
    public IActionResult Status(int codigo) => View("Mensagem", codigo switch
    {
        404 => "Não encontramos esta página. Confira o link.",
        429 => "Muitas tentativas seguidas. Aguarde um minuto e tente de novo.",
        _ => "Algo deu errado. Tente de novo em instantes."
    });
}
