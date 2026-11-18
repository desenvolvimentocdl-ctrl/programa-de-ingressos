using Plateia.Application;
using Plateia.Domain;
using Plateia.Infrastructure;
using Plateia.Web.Infra;
using Plateia.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Web.Controllers;

/// <summary>
/// Conta do público: cadastro (e-mail e senha ou Google/Apple/Microsoft), "esqueci minha senha" com código por e-mail,
/// confirmação de e-mail, "Meus ingressos" e a passagem para conta de produtor.
/// </summary>
public class ContaPublicoController(AppDbContext db, IPasswordHasher<Usuario> hasher, CodigoService codigos, ILogger<ContaPublicoController> log) : BaseController
{
    // ---------- Cadastre-se ----------

    [AllowAnonymous, HttpGet("/cadastro")]
    public IActionResult Cadastro(string? returnUrl) => User.Identity?.IsAuthenticated == true ? Redirect("/meus-ingressos") : View(model: returnUrl);

    [AllowAnonymous, HttpPost("/cadastro"), EnableRateLimiting("publico")]
    public async Task<IActionResult> Cadastro(string nome, string email, string senha, string confirmacao, bool aceite, string? returnUrl)
    {
        ViewBag.Form = new { nome, email };
        var e = (email ?? "").Trim().ToLowerInvariant();
        string? erro = null;
        if (Utilidades.Limpo(nome, 120) is null) erro = "Informe seu nome.";
        else if (!Utilidades.EmailValido(e) || e.Length > 180) erro = "Informe um e-mail válido.";
        else if ((senha ?? "").Length < 8) erro = "A senha precisa ter pelo menos 8 caracteres.";
        else if (senha != confirmacao) erro = "A confirmação não confere com a senha.";
        else if (!aceite) erro = "Para continuar, aceite os termos de uso.";
        else if (await db.Usuarios.AnyAsync(u => u.Email == e)) erro = "Já existe uma conta com este e-mail. Use \"Entrar\" ou \"Esqueci minha senha\".";
        if (erro is not null) { ViewBag.Erro = erro; return View(model: returnUrl); }

        var u = new Usuario { Nome = Utilidades.Limpo(nome, 120)!, Email = e, Perfil = Perfil.Participante };
        u.SenhaHash = hasher.HashPassword(u, senha!);
        db.Usuarios.Add(u);
        await db.SaveChangesAsync();
        await Sessao.EntrarAsync(HttpContext, u);
        try { await codigos.EnviarAsync(e, FinalidadeCodigo.ConfirmarEmail, u.Nome); }
        catch (RegraNegocioException ex) { log.LogWarning("Código de confirmação não enviado para {Email}: {Erro}", e, ex.Message); }
        TempData["msg"] = "Conta criada! Enviamos um código para o seu e-mail: confirme para ver seus ingressos.";
        return Redirect("/confirmar-email" + (Url.IsLocalUrl(returnUrl) ? "?returnUrl=" + Uri.EscapeDataString(returnUrl!) : ""));
    }

    // ---------- Esqueci minha senha ----------

    [AllowAnonymous, HttpGet("/esqueci-senha")]
    public IActionResult EsqueciSenha(string? email) => View(model: email);

    /// <summary>Sempre responde igual (exista a conta ou não), para ninguém descobrir quais e-mails têm cadastro.</summary>
    [AllowAnonymous, HttpPost("/esqueci-senha"), EnableRateLimiting("publico")]
    public async Task<IActionResult> EsqueciSenha(string email, bool _ = false)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        if (!Utilidades.EmailValido(e)) { ViewBag.Erro = "Informe um e-mail válido."; return View(model: email); }
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Email == e && x.Ativo);
        if (u is not null)
        {
            try { await codigos.EnviarAsync(e, FinalidadeCodigo.RedefinirSenha, u.Nome); }
            catch (RegraNegocioException ex) when (ex.Message.StartsWith("Acabamos")) { /* já há um código recente: segue igual */ }
        }
        TempData["msg"] = "Se houver uma conta com este e-mail, enviamos um código de 6 dígitos. Confira também o spam.";
        return Redirect("/redefinir-senha?email=" + Uri.EscapeDataString(e));
    }

    [AllowAnonymous, HttpGet("/redefinir-senha")]
    public IActionResult RedefinirSenha(string? email) => View(model: email);

    [AllowAnonymous, HttpPost("/redefinir-senha"), EnableRateLimiting("publico")]
    public async Task<IActionResult> RedefinirSenha(string email, string codigo, string nova, string confirmacao)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        string? erro = null;
        if ((nova ?? "").Length < 8) erro = "A nova senha precisa ter pelo menos 8 caracteres.";
        else if (nova != confirmacao) erro = "A confirmação não confere com a nova senha.";
        var u = erro is null ? await db.Usuarios.FirstOrDefaultAsync(x => x.Email == e && x.Ativo) : null;
        if (erro is null && (u is null || !await codigos.ValidarAsync(e, FinalidadeCodigo.RedefinirSenha, codigo)))
            erro = "Código inválido ou vencido. Confira os 6 dígitos ou peça um novo código.";
        if (erro is not null) { ViewBag.Erro = erro; return View(model: email); }

        u!.SenhaHash = hasher.HashPassword(u, nova!);
        u.EmailConfirmado = true; // recebeu o código: o e-mail é dela
        await db.SaveChangesAsync();
        await Sessao.EntrarAsync(HttpContext, u);
        TempData["msg"] = "Senha alterada! Você já está dentro.";
        return Redirect(Sessao.Inicio(u));
    }

    // ---------- Confirmar e-mail ----------

    [Authorize, HttpGet("/confirmar-email")]
    public async Task<IActionResult> ConfirmarEmail(string? returnUrl)
    {
        var u = await db.Usuarios.FirstAsync(x => x.Id == Eu.Id);
        if (u.EmailConfirmado) return Redirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Sessao.Inicio(u));
        ViewBag.Volta = returnUrl;
        return View(u);
    }

    [Authorize, HttpPost("/confirmar-email/enviar"), EnableRateLimiting("publico")]
    public async Task<IActionResult> EnviarConfirmacao()
    {
        var u = await db.Usuarios.FirstAsync(x => x.Id == Eu.Id);
        if (u.EmailConfirmado) return Redirect(Sessao.Inicio(u));
        await codigos.EnviarAsync(u.Email, FinalidadeCodigo.ConfirmarEmail, u.Nome);
        return VoltarCom($"Enviamos um novo código para {u.Email}.", "/confirmar-email");
    }

    [Authorize, HttpPost("/confirmar-email"), EnableRateLimiting("publico")]
    public async Task<IActionResult> ConfirmarEmail(string codigo, string? returnUrl)
    {
        var u = await db.Usuarios.FirstAsync(x => x.Id == Eu.Id);
        if (!u.EmailConfirmado)
        {
            if (!await codigos.ValidarAsync(u.Email, FinalidadeCodigo.ConfirmarEmail, codigo))
            {
                ViewBag.Erro = "Código inválido ou vencido. Confira os 6 dígitos ou peça um novo código.";
                ViewBag.Volta = returnUrl;
                return View(u);
            }
            u.EmailConfirmado = true;
            await db.SaveChangesAsync();
        }
        TempData["msg"] = "E-mail confirmado!";
        return Redirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Sessao.Inicio(u));
    }

    // ---------- Meus ingressos ----------

    /// <summary>
    /// Ingressos em nome da pessoa e pedidos que ela comprou (pelo e-mail). Só com e-mail confirmado:
    /// o link do ingresso é a entrada no evento, então não pode aparecer para quem só digitou o e-mail de outra pessoa.
    /// </summary>
    [Authorize, HttpGet("/meus-ingressos")]
    public async Task<IActionResult> MeusIngressos()
    {
        var u = await db.Usuarios.FirstAsync(x => x.Id == Eu.Id);
        if (!u.EmailConfirmado) return Redirect("/confirmar-email?returnUrl=%2Fmeus-ingressos");
        var itens = await db.Inscricoes.Include(i => i.Evento).Include(i => i.TipoIngresso).Include(i => i.Pedido)
            .Where(i => i.Email == u.Email || (i.Pedido != null && i.Pedido.Email == u.Email))
            .OrderByDescending(i => i.Evento!.InicioEm).Take(500).ToListAsync();
        return View(new PaginaMeusIngressos { Usuario = u, Itens = itens });
    }

    // ---------- Virar produtor ----------

    [Authorize, HttpGet("/virar-produtor")]
    public IActionResult VirarProdutor() => Sessao.Produtor(User) ? Redirect("/painel") : View();

    /// <summary>Conta de público passa a produtora (em análise, como no cadastro de produtor). Os ingressos continuam na mesma conta.</summary>
    [Authorize, HttpPost("/virar-produtor")]
    public async Task<IActionResult> VirarProdutor(string? marca, string? telefone, string? cidade, bool aceite)
    {
        if (!aceite) { ViewBag.Erro = "Para continuar, aceite os termos de uso para produtores."; return View(); }
        var u = await db.Usuarios.FirstAsync(x => x.Id == Eu.Id);
        if (u.Perfil == Perfil.Participante)
        {
            u.Perfil = Perfil.Organizador;
            u.Aprovado = false;
            u.NomePublico = Utilidades.Limpo(marca, 120);
            u.Telefone = Utilidades.SoDigitos(telefone) ?? u.Telefone;
            u.Cidade = Utilidades.Limpo(cidade, 80);
            u.Slug ??= await ContaController.SlugProdutorAsync(db, u.NomeExibicao, u.Id);
            await db.SaveChangesAsync();
            await Sessao.EntrarAsync(HttpContext, u); // nova sessão com o perfil de produtor
        }
        TempData["msg"] = "Pronto! Monte seu primeiro evento. A publicação é liberada assim que a equipe aprovar seu cadastro.";
        return Redirect("/painel");
    }
}
