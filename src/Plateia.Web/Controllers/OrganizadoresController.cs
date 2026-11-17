using System.Security.Cryptography;
using Plateia.Application;
using Plateia.Domain;
using Plateia.Infrastructure;
using Plateia.Web.Infra;
using Plateia.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Web.Controllers;

[Authorize(Policy = Politicas.Admin)]
public class OrganizadoresController(AppDbContext db, IPasswordHasher<Usuario> hasher) : BaseController
{
    [HttpGet("/organizadores")]
    public async Task<IActionResult> Index() => View(await db.Usuarios.OrderByDescending(u => u.Ativo).ThenBy(u => u.Nome)
        .Select(u => new OrganizadorLinha(u.Id, u.Nome, u.Email, u.Telefone, u.Perfil, u.Ativo, db.Eventos.Count(e => e.OrganizadorId == u.Id)))
        .ToListAsync());

    [HttpPost("/organizadores")]
    public async Task<IActionResult> Criar(string nome, string email, string? telefone, Perfil perfil)
    {
        var n = Utilidades.Limpo(nome, 120) ?? throw new RegraNegocioException("Informe o nome.");
        var e = (email ?? "").Trim().ToLowerInvariant();
        if (!Utilidades.EmailValido(e)) throw new RegraNegocioException("Informe um e-mail válido.");
        if (await db.Usuarios.AnyAsync(u => u.Email == e)) throw new RegraNegocioException("Já existe um acesso com este e-mail.");

        var u = new Usuario { Nome = n, Email = e, Telefone = Utilidades.SoDigitos(telefone), Perfil = perfil };
        var senha = SenhaTemporaria();
        u.SenhaHash = hasher.HashPassword(u, senha);
        db.Usuarios.Add(u);
        await db.SaveChangesAsync();
        TempData["senha"] = $"{u.Email}|{senha}";
        return Redirect("/organizadores");
    }

    [HttpPost("/organizadores/{id:int}/senha")]
    public async Task<IActionResult> NovaSenha(int id)
    {
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == id);
        if (u is null) return NotFound();
        var senha = SenhaTemporaria();
        u.SenhaHash = hasher.HashPassword(u, senha);
        await db.SaveChangesAsync();
        TempData["senha"] = $"{u.Email}|{senha}";
        return Redirect("/organizadores");
    }

    [HttpPost("/organizadores/{id:int}/ativo")]
    public async Task<IActionResult> Ativo(int id, bool ativo)
    {
        if (id == Eu.Id) throw new RegraNegocioException("Você não pode desativar o próprio acesso.");
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == id);
        if (u is null) return NotFound();
        u.Ativo = ativo;
        await db.SaveChangesAsync();
        return VoltarCom(ativo ? $"{u.Nome} pode entrar de novo." : $"{u.Nome} não consegue mais entrar.");
    }

    static string SenhaTemporaria()
    {
        const string a = "abcdefghjkmnpqrstuvwxyz23456789";
        return string.Concat(Enumerable.Range(0, 10).Select(_ => a[RandomNumberGenerator.GetInt32(a.Length)]));
    }
}
