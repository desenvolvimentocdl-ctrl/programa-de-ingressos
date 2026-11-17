using System.Globalization;
using System.Security.Claims;
using Plateia.Application;
using Plateia.Domain;
using Plateia.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace Plateia.Web.Infra;

public static class Politicas
{
    public const string Admin = "Admin";
}

public abstract class BaseController : Controller
{
    protected UsuarioAtual Eu => new(
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
        User.Identity!.Name!,
        Enum.Parse<Perfil>(User.FindFirstValue(ClaimTypes.Role)!));

    protected IActionResult VoltarCom(string mensagem, string? url = null)
    {
        TempData["msg"] = mensagem;
        var referer = Request.Headers.Referer.ToString();
        return Redirect(url ?? (referer.Length > 0 ? referer : "/painel"));
    }

    protected bool QuerJson => Request.Headers.Accept.ToString().Contains("application/json");
}

/// <summary>Erros de regra de negócio viram mensagem amigável (JSON para chamadas via fetch, aviso no topo para formulários).</summary>
public class RegraNegocioFilter : IExceptionFilter
{
    public void OnException(ExceptionContext ctx)
    {
        if (ctx.Exception is not RegraNegocioException e) return;
        var req = ctx.HttpContext.Request;
        if (req.Headers.Accept.ToString().Contains("application/json"))
            ctx.Result = new BadRequestObjectResult(new { erro = e.Message });
        else
        {
            var tempData = ctx.HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataDictionaryFactory>().GetTempData(ctx.HttpContext);
            tempData["erro"] = e.Message;
            var volta = req.Headers.Referer.ToString();
            ctx.Result = new RedirectResult(string.IsNullOrEmpty(volta) ? "/" : volta);
        }
        ctx.ExceptionHandled = true;
    }
}

/// <summary>Datas são gravadas em UTC e mostradas no horário de Brasília.</summary>
public static class Hora
{
    static readonly TimeZoneInfo Br = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Br);
    public static DateTime ParaUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Br);
    public static DateTime HojeLocal => Local(DateTime.UtcNow).Date;

    public static string Relativa(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        if (d.TotalMinutes < 1) return "agora";
        if (d.TotalMinutes < 60) return $"há {(int)d.TotalMinutes} min";
        if (d.TotalHours < 24) return $"há {(int)d.TotalHours} h";
        if (d.TotalDays < 7) return $"há {(int)d.TotalDays} d";
        return Local(utc).ToString("dd/MM/yyyy", PtBr);
    }

    /// <summary>"qui, 27 nov · 19h30"</summary>
    public static string Data(DateTime utc)
    {
        var l = Local(utc);
        var hora = l.Minute == 0 ? l.ToString("H'h'") : l.ToString("H'h'mm");
        return $"{l.ToString("ddd, dd MMM", PtBr).Replace(".", "")} · {hora}";
    }

    public static string DataLonga(DateTime utc)
    {
        var l = Local(utc);
        var hora = l.Minute == 0 ? l.ToString("H'h'") : l.ToString("H'h'mm");
        return $"{l.ToString("dddd, d 'de' MMMM", PtBr)} · {hora}";
    }

    public static string Hhmm(DateTime utc) => Local(utc).ToString("HH:mm");

    /// <summary>Valor para input datetime-local.</summary>
    public static string Input(DateTime? utc) => utc is { } u ? Local(u).ToString("yyyy-MM-dd'T'HH:mm") : "";

    public static string Dinheiro(decimal v) => v == Math.Floor(v) ? v.ToString("C0", PtBr) : v.ToString("C2", PtBr);
}

public static class Texto
{
    public static string Iniciais(string? nome)
    {
        var p = (nome ?? "?").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length == 0 ? "?" : p.Length == 1 ? p[0][..1].ToUpper() : (p[0][..1] + p[^1][..1]).ToUpper();
    }

    public static string PrimeiroNome(string? nome) => (nome ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    public static string NomePerfil(Perfil p) => p == Perfil.Administrador ? "Administrador" : "Organizador";

    public static string NomePagamento(StatusPagamento p) => p switch
    {
        StatusPagamento.Pago => "Pago",
        StatusPagamento.Cortesia => "Cortesia",
        _ => "Pendente"
    };

    public static string ClassePagamento(StatusPagamento p) => p switch
    {
        StatusPagamento.Pago => "ok",
        StatusPagamento.Cortesia => "info",
        _ => "alerta"
    };

    public static string NomeStatus(StatusEvento s) => s switch
    {
        StatusEvento.Publicado => "Publicado",
        StatusEvento.Encerrado => "Encerrado",
        StatusEvento.Cancelado => "Cancelado",
        _ => "Rascunho"
    };

    public static string Preco(decimal p) => p <= 0 ? "Gratuito" : Hora.Dinheiro(p);

    /// <summary>Número com zeros à esquerda, como numeração de ingresso.</summary>
    public static string Numero(int n, int casas = 3) => n.ToString(new string('0', casas));

    /// <summary>Cor de texto legível sobre a cor do evento.</summary>
    public static string TintaSobre(string hex)
    {
        if (!Utilidades.CorValida(hex)) return "#111";
        double C(int i) { var v = Convert.ToInt32(hex.Substring(i, 2), 16) / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        var lum = 0.2126 * C(1) + 0.7152 * C(3) + 0.0722 * C(5);
        return lum > 0.32 ? "#14110F" : "#FFFFFF";
    }

    public static string LinkWhatsApp(string telefone, string mensagem)
    {
        var d = Utilidades.SoDigitos(telefone) ?? "";
        if (d.Length is 10 or 11) d = "55" + d;
        return $"https://wa.me/{d}?text={Uri.EscapeDataString(mensagem)}";
    }

    /// <summary>Menor preço pago à venda (ignora cortesias e lotes encerrados); 0 quando o evento é todo gratuito.</summary>
    public static decimal APartirDe(IEnumerable<TipoIngresso> tipos) => CatalogoService.APartirDe(tipos);

    public static string NomeCategoria(CategoriaEvento c) => c switch
    {
        CategoriaEvento.Negocios => "Negócios",
        CategoriaEvento.Cursos => "Cursos e workshops",
        CategoriaEvento.Tecnologia => "Tecnologia",
        CategoriaEvento.Marketing => "Marketing e vendas",
        CategoriaEvento.Saude => "Saúde e bem-estar",
        CategoriaEvento.Gastronomia => "Gastronomia",
        CategoriaEvento.Musica => "Música e shows",
        CategoriaEvento.ArteCultura => "Arte, teatro e cultura",
        CategoriaEvento.Esportes => "Esportes",
        CategoriaEvento.Infantil => "Infantil",
        CategoriaEvento.Networking => "Networking",
        _ => "Outros"
    };

    public static string SlugCategoria(CategoriaEvento c) => c.ToString().ToLowerInvariant();

    public static CategoriaEvento? CategoriaDoSlug(string? s) =>
        Enum.GetValues<CategoriaEvento>().Cast<CategoriaEvento?>().FirstOrDefault(c => SlugCategoria(c!.Value) == (s ?? "").ToLowerInvariant());

    public static string NomeFormato(FormatoEvento f) => f switch { FormatoEvento.Online => "Online", FormatoEvento.Hibrido => "Presencial + online", _ => "Presencial" };

    /// <summary>Endereço para abrir no mapa (Google Maps, sem chave de API).</summary>
    public static string LinkMapa(Evento e) =>
        "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(string.Join(", ",
            new[] { e.Local, e.Endereco, e.Bairro, e.CidadeUf }.Where(x => !string.IsNullOrWhiteSpace(x))));

    public static string Onde(CartaoPublico c) => c.Formato == FormatoEvento.Online ? "Online" :
        string.Join(" · ", new[] { c.Local, c.Cidade is null ? null : $"{c.Cidade}/{c.Uf}" }.Where(x => x is not null));

    public static string PrecoCartao(CartaoPublico c) => c.Gratuito ? "Gratuito" : "a partir de " + Hora.Dinheiro(c.APartirDe);

    /// <summary>Número para CSS (sempre com ponto, independente da cultura do servidor).</summary>
    public static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    public static readonly string[] Cores = ["#FF6B4A", "#FFB547", "#3DDC97", "#2EC4D6", "#5B7CFA", "#A770EF", "#FF5C9A", "#14110F"];
}

public static class Qr
{
    /// <summary>QR code em SVG (sem imagem externa), com a cor indicada.</summary>
    public static string Svg(string conteudo, string cor = "#14110F")
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(conteudo, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data).GetGraphic(new System.Drawing.Size(240, 240), cor, "transparent", drawQuietZones: false,
            sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
        return svg;
    }
}

public static class Inicializacao
{
    public static async Task ExecutarAsync(IServiceProvider sp, IConfiguration config, ILogger log)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        if (!await db.Usuarios.AnyAsync())
        {
            var email = config["Admin:Email"];
            var senha = config["Admin:Senha"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(senha))
            {
                log.LogWarning("Nenhum usuário cadastrado. Defina Admin__Email e Admin__Senha para criar o primeiro administrador.");
                return;
            }
            var admin = new Usuario { Nome = config["Admin:Nome"] ?? "Administrador", Email = email.Trim().ToLowerInvariant(), Perfil = Perfil.Administrador };
            admin.SenhaHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>().HashPassword(admin, senha);
            db.Usuarios.Add(admin);
            await db.SaveChangesAsync();
            log.LogInformation("Administrador inicial criado: {Email}", admin.Email);
        }

        if (config.GetValue<bool>("Demo:Criar") && !await db.Eventos.AnyAsync())
        {
            await Demo.CriarAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>());
            log.LogInformation("Dados de demonstração criados.");
        }
    }
}
