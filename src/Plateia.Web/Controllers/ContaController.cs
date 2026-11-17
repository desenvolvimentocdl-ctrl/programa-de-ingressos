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

        await EntrarComo(u!);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/painel");
    }

    [AllowAnonymous, HttpGet("/criar-conta")]
    public IActionResult CriarConta() => User.Identity?.IsAuthenticated == true ? Redirect("/painel") : View();

    /// <summary>Cadastro de produtor (palestrante, empresa, coletivo…). A conta nasce "em análise": cria eventos, mas só publica após aprovação.</summary>
    [AllowAnonymous, HttpPost("/criar-conta"), EnableRateLimiting("publico")]
    public async Task<IActionResult> CriarConta(string nome, string? marca, string email, string? telefone, string? cidade, string senha, string confirmacao, bool aceite)
    {
        ViewBag.Form = new { nome, marca, email, telefone, cidade };
        string? erro = null;
        var e = (email ?? "").Trim().ToLowerInvariant();
        if (Application.Utilidades.Limpo(nome, 120) is null) erro = "Informe seu nome.";
        else if (!Application.Utilidades.EmailValido(e)) erro = "Informe um e-mail válido.";
        else if ((senha ?? "").Length < 8) erro = "A senha precisa ter pelo menos 8 caracteres.";
        else if (senha != confirmacao) erro = "A confirmação não confere com a senha.";
        else if (!aceite) erro = "Para continuar, aceite os termos de uso.";
        else if (await db.Usuarios.AnyAsync(u => u.Email == e)) erro = "Já existe uma conta com este e-mail. Use \"Entrar\".";
        if (erro is not null) { ViewBag.Erro = erro; return View(); }

        var u = new Usuario
        {
            Nome = Application.Utilidades.Limpo(nome, 120)!, NomePublico = Application.Utilidades.Limpo(marca, 120), Email = e,
            Telefone = Application.Utilidades.SoDigitos(telefone), Cidade = Application.Utilidades.Limpo(cidade, 80),
            Perfil = Perfil.Organizador, Aprovado = false
        };
        u.Slug = await SlugProdutorAsync(db, u.NomeExibicao);
        u.SenhaHash = hasher.HashPassword(u, senha!);
        db.Usuarios.Add(u);
        await db.SaveChangesAsync();
        await EntrarComo(u);
        TempData["msg"] = "Conta criada! Monte seu primeiro evento. A publicação é liberada assim que a equipe aprovar seu cadastro.";
        return Redirect("/painel");
    }

    public static async Task<string> SlugProdutorAsync(AppDbContext db, string nome, int? ignorarId = null)
    {
        var b = Application.Utilidades.Slug(nome);
        var s = b;
        for (var n = 2; await db.Usuarios.AnyAsync(x => x.Slug == s && x.Id != ignorarId); n++) s = $"{b}-{n}";
        return s;
    }

    async Task EntrarComo(Usuario u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Nome),
            new(ClaimTypes.Email, u.Email),
            new(ClaimTypes.Role, u.Perfil.ToString())
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
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
    public async Task<IActionResult> SalvarConta(string nome, string? telefone, string? marca, string? bio, string? instagram, string? site, string? cidade)
    {
        var u = await db.Usuarios.FirstAsync(x => x.Id == Eu.Id);
        u.Nome = Application.Utilidades.Limpo(nome, 120) ?? u.Nome;
        u.Telefone = Application.Utilidades.SoDigitos(telefone);
        u.NomePublico = Application.Utilidades.Limpo(marca, 120);
        u.Bio = Application.Utilidades.Limpo(bio, 1000);
        u.Instagram = Application.Utilidades.Limpo(instagram, 60)?.TrimStart('@');
        var s = Application.Utilidades.Limpo(site, 200);
        u.Site = s is null ? null : s.StartsWith("http") ? s : "https://" + s;
        u.Cidade = Application.Utilidades.Limpo(cidade, 80);
        u.Slug ??= await SlugProdutorAsync(db, u.NomeExibicao, u.Id);
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
