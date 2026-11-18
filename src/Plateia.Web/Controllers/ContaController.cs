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
        var ok = u is { TemSenha: true } && hasher.VerifyHashedPassword(u, u.SenhaHash, senha ?? "") != PasswordVerificationResult.Failed;
        if (!ok)
        {
            await Task.Delay(400); // dificulta tentativa e erro
            ViewBag.Erro = "E-mail ou senha incorretos.";
            ViewBag.Email = email;
            return View(model: returnUrl);
        }

        await EntrarComo(u!);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Sessao.Inicio(u!));
    }

    /// <summary>Cadastro de produtor. Quem já tem conta de público ativa o modo produtor na mesma conta.</summary>
    [AllowAnonymous, HttpGet("/criar-conta")]
    public IActionResult CriarConta() => User.Identity?.IsAuthenticated != true ? View()
        : User.IsInRole(nameof(Perfil.Participante)) ? Redirect("/virar-produtor") : Redirect("/painel");

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

    /// <summary>Começa o login com Google, Apple ou Microsoft (POST com antiforgery, para ninguém disparar o login por outro site).</summary>
    [AllowAnonymous, HttpPost("/entrar/externo"), EnableRateLimiting("publico")]
    public async Task<IActionResult> EntrarExterno(string provedor, string? returnUrl, string? tipo, [FromServices] IAuthenticationSchemeProvider esquemas)
    {
        if (!LoginExternoConfig.Provedores.Contains(provedor))
            return ErroLogin("Esta forma de entrar não está disponível.");
        if (await esquemas.GetSchemeAsync(provedor) is null)
            return ErroLogin($"Entrar com {LoginExternoConfig.Nome(provedor)} ainda não foi ativado neste site. Por enquanto, use e-mail e senha.");
        var volta = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        var props = new AuthenticationProperties { RedirectUri = "/entrar/externo/retorno" + (volta is null ? "" : "?returnUrl=" + Uri.EscapeDataString(volta)) };
        props.Items["provedor"] = provedor;
        // Conta nova criada a partir de "Criar conta de produtor" nasce produtora; do "Cadastre-se", conta de público.
        props.Items["tipo"] = tipo == "produtor" ? "produtor" : "publico";
        return Challenge(props, provedor);
    }

    /// <summary>
    /// Volta do provedor. Ordem: (1) conta já ligada a este login → entra; (2) já está logado → liga o login à conta atual;
    /// (3) e-mail confiável (Google/Apple) de uma conta existente → liga e entra; (4) e-mail novo → cria a conta de produtor (em análise).
    /// </summary>
    [AllowAnonymous, HttpGet("/entrar/externo/retorno")]
    public async Task<IActionResult> RetornoExterno(string? returnUrl)
    {
        var r = await HttpContext.AuthenticateAsync(LoginExternoConfig.EsquemaExterno);
        await HttpContext.SignOutAsync(LoginExternoConfig.EsquemaExterno);
        if (!r.Succeeded || r.Principal is null || r.Properties?.Items.TryGetValue("provedor", out var provedor) != true || provedor is null)
            return ErroLogin("Não foi possível entrar agora. Tente de novo.");

        var p = r.Principal;
        var chave = p.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(chave)) return ErroLogin("Não foi possível entrar agora. Tente de novo.");
        var email = (p.FindFirstValue(ClaimTypes.Email) ?? "").Trim().ToLowerInvariant();
        var nome = Application.Utilidades.Limpo(p.FindFirstValue(ClaimTypes.Name), 120)
                   ?? Application.Utilidades.Limpo($"{p.FindFirstValue(ClaimTypes.GivenName)} {p.FindFirstValue(ClaimTypes.Surname)}", 120);
        var confiavel = LoginExternoConfig.EmailConfiavel(provedor);

        // (1) Login já conhecido.
        var login = await db.LoginsExternos.Include(x => x.Usuario).FirstOrDefaultAsync(x => x.Provedor == provedor && x.Chave == chave);
        if (login?.Usuario is { } dono)
        {
            if (!dono.Ativo) return ErroLogin("Esta conta está desativada. Fale com a equipe do Plateia.");
            if (confiavel && !dono.EmailConfirmado && dono.Email == email) { dono.EmailConfirmado = true; await db.SaveChangesAsync(); }
            await EntrarComo(dono);
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Sessao.Inicio(dono));
        }

        // (2) Já está logado: "Conectar Google" na Minha conta.
        if (User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var meuId))
        {
            if (await db.LoginsExternos.AnyAsync(x => x.UsuarioId == meuId && x.Provedor == provedor))
                return VoltarCom($"Sua conta já está conectada a outra conta {provedor}. Desconecte antes de trocar.", "/minha-conta");
            db.LoginsExternos.Add(new LoginExterno { UsuarioId = meuId, Provedor = provedor, Chave = chave, Email = Curto(email) });
            await db.SaveChangesAsync();
            return VoltarCom($"Pronto! Agora você também pode entrar com {provedor}.", "/minha-conta");
        }

        if (!Application.Utilidades.EmailValido(email))
            return ErroLogin($"A conta {provedor} não informou um e-mail. Autorize o compartilhamento do e-mail ou crie a conta com e-mail e senha.");

        // (3) Já existe conta com este e-mail.
        var existente = await db.Usuarios.FirstOrDefaultAsync(x => x.Email == email);
        if (existente is not null)
        {
            if (!LoginExternoConfig.EmailConfiavel(provedor))
                return ErroLogin($"Já existe uma conta com {email}. Entre com e-mail e senha e conecte a conta {provedor} em \"Minha conta\".");
            if (!existente.Ativo) return ErroLogin("Esta conta está desativada. Fale com a equipe do Plateia.");
            db.LoginsExternos.Add(new LoginExterno { UsuarioId = existente.Id, Provedor = provedor, Chave = chave, Email = Curto(email) });
            existente.EmailConfirmado = true;
            await db.SaveChangesAsync();
            await EntrarComo(existente);
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Sessao.Inicio(existente));
        }

        // (4) Conta nova: de produtor ("em análise", como no cadastro com senha) ou de público.
        var produtor = r.Properties.Items.TryGetValue("tipo", out var tipo) && tipo == "produtor";
        var u = new Usuario
        {
            Nome = nome ?? email.Split('@')[0], Email = email, SenhaHash = "", EmailConfirmado = confiavel,
            Perfil = produtor ? Perfil.Organizador : Perfil.Participante, Aprovado = !produtor
        };
        u.Slug = await SlugProdutorAsync(db, u.NomeExibicao);
        u.Logins.Add(new LoginExterno { Provedor = provedor, Chave = chave, Email = Curto(email) });
        db.Usuarios.Add(u);
        await db.SaveChangesAsync();
        await EntrarComo(u);
        if (!produtor)
        {
            TempData["msg"] = $"Conta criada com {LoginExternoConfig.Nome(provedor)}! Seus ingressos ficam em \"Meus ingressos\".";
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/meus-ingressos");
        }
        TempData["msg"] = $"Conta criada com {LoginExternoConfig.Nome(provedor)}! Monte seu primeiro evento. A publicação é liberada assim que a equipe aprovar seu cadastro. Complete seu perfil em \"Minha conta\".";
        return Redirect("/painel");

        static string? Curto(string e) => e.Length == 0 ? null : e.Length > 180 ? e[..180] : e;
    }

    IActionResult ErroLogin(string mensagem)
    {
        ViewBag.Erro = mensagem;
        return View("Entrar", model: (string?)null);
    }

    [Authorize, HttpPost("/minha-conta/conexoes/{id:int}/remover")]
    public async Task<IActionResult> RemoverConexao(int id)
    {
        var u = await db.Usuarios.Include(x => x.Logins).FirstAsync(x => x.Id == Eu.Id);
        var l = u.Logins.FirstOrDefault(x => x.Id == id);
        if (l is null) return NotFound();
        if (!u.TemSenha && u.Logins.Count == 1)
            return VoltarCom("Crie uma senha antes de desconectar: senão você fica sem forma de entrar.");
        db.LoginsExternos.Remove(l);
        await db.SaveChangesAsync();
        return VoltarCom($"Conta {l.Provedor} desconectada.");
    }

    public static async Task<string> SlugProdutorAsync(AppDbContext db, string nome, int? ignorarId = null)
    {
        var b = Application.Utilidades.Slug(nome);
        var s = b;
        for (var n = 2; await db.Usuarios.AnyAsync(x => x.Slug == s && x.Id != ignorarId); n++) s = $"{b}-{n}";
        return s;
    }

    Task EntrarComo(Usuario u) => Sessao.EntrarAsync(HttpContext, u);

    [Authorize, HttpPost("/sair")]
    public async Task<IActionResult> Sair()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }

    [Authorize, HttpGet("/minha-conta")]
    public async Task<IActionResult> MinhaConta() => View(await db.Usuarios.Include(x => x.Logins).FirstAsync(x => x.Id == Eu.Id));

    [Authorize, HttpPost("/minha-conta")]
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
        if (u.Perfil != Perfil.Participante) u.Slug ??= await SlugProdutorAsync(db, u.NomeExibicao, u.Id);
        await db.SaveChangesAsync();
        return VoltarCom("Dados salvos. O novo nome aparece no próximo acesso.");
    }

    [Authorize, HttpPost("/minha-conta/senha")]
    public async Task<IActionResult> TrocarSenha(string atual, string nova, string confirmacao)
    {
        var u = await db.Usuarios.FirstAsync(x => x.Id == Eu.Id);
        // Conta criada com Google/Apple/Microsoft ainda não tem senha: cria a primeira sem pedir a atual.
        if (u.TemSenha && hasher.VerifyHashedPassword(u, u.SenhaHash, atual ?? "") == PasswordVerificationResult.Failed)
            return VoltarCom("A senha atual não confere.");
        if ((nova ?? "").Length < 8) return VoltarCom("A nova senha precisa ter pelo menos 8 caracteres.");
        if (nova != confirmacao) return VoltarCom("A confirmação não confere com a nova senha.");
        u.SenhaHash = hasher.HashPassword(u, nova!);
        await db.SaveChangesAsync();
        return VoltarCom("Senha alterada.");
    }

    /// <summary>Conta de público tentando abrir a área de produtor: oferece ativar o modo produtor.</summary>
    [AllowAnonymous, HttpGet("/sem-acesso")]
    public IActionResult SemAcesso() => User.IsInRole(nameof(Perfil.Participante))
        ? Redirect("/virar-produtor")
        : View("Mensagem", "Seu perfil não tem acesso a esta página.");

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
