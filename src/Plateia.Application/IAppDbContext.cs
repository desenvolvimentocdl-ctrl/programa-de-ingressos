using Plateia.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Plateia.Application;

public interface IAppDbContext
{
    DbSet<Usuario> Usuarios { get; }
    DbSet<Evento> Eventos { get; }
    DbSet<TipoIngresso> TiposIngresso { get; }
    DbSet<Inscricao> Inscricoes { get; }
    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Quem está usando o sistema agora.</summary>
public record UsuarioAtual(int Id, string Nome, Perfil Perfil)
{
    public bool Admin => Perfil == Perfil.Administrador;
}

public class RegraNegocioException(string mensagem) : Exception(mensagem);
