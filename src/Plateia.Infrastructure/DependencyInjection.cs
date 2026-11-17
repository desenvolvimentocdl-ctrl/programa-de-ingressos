using Plateia.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Plateia.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfraestrutura(this IServiceCollection services, IConfiguration config)
    {
        var conexao = config.GetConnectionString("Plateia") is { Length: > 0 } c
            ? c
            : throw new InvalidOperationException("Defina ConnectionStrings:Plateia (variável de ambiente ConnectionStrings__Plateia).");

        // Versão fixa para não precisar abrir conexão só para descobrir o servidor (MySQL 8, o mesmo dos outros sistemas).
        services.AddDbContext<AppDbContext>(o => o.UseMySql(conexao, new MySqlServerVersion(new Version(8, 0, 36)),
            m => m.EnableRetryOnFailure(3)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<EventoService>();
        services.AddScoped<InscricaoService>();
        return services;
    }
}
