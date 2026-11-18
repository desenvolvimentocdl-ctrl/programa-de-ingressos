using Microsoft.AspNetCore.Authentication;

namespace Plateia.Web.Infra;

/// <summary>
/// Login com contas externas (Google, Apple, Microsoft). Cada provedor só é ativado quando as credenciais
/// estão configuradas (variáveis de ambiente Login__Google__ClientId etc.); sem nenhuma, as telas mostram só e-mail e senha.
///
/// Endereços de retorno a cadastrar em cada provedor (troque pelo domínio do site, sempre com https):
///   Google     https://SEU-DOMINIO/entrar/google/retorno      (Google Cloud Console → Credenciais → ID do cliente OAuth)
///   Microsoft  https://SEU-DOMINIO/entrar/microsoft/retorno   (portal.azure.com → Registros de aplicativo; contas pessoais e corporativas)
///   Apple      https://SEU-DOMINIO/entrar/apple/retorno       (developer.apple.com → Identifiers → Services ID com "Sign in with Apple")
/// </summary>
public static class LoginExternoConfig
{
    /// <summary>Esquema temporário: guarda a resposta do provedor só até o Plateia decidir qual conta abrir.</summary>
    public const string EsquemaExterno = "Externo";

    public static readonly string[] Provedores = ["Google", "Apple", "Microsoft"];

    /// <summary>Provedores em que o e-mail informado é sempre verificado (pode ligar a uma conta que já existe com o mesmo e-mail).</summary>
    public static bool EmailConfiavel(string provedor) => provedor is "Google" or "Apple";

    /// <summary>Nome mostrado nos botões e mensagens.</summary>
    public static string Nome(string provedor) => provedor switch
    {
        "Apple" => "Apple (iCloud)",
        "Microsoft" => "Microsoft (Outlook/Hotmail)",
        _ => provedor
    };

    public static void Adicionar(AuthenticationBuilder auth, IConfiguration config, IWebHostEnvironment env)
    {
        auth.AddCookie(EsquemaExterno, o =>
        {
            o.Cookie.Name = "plateia_ext";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        });

        var g = config.GetSection("Login:Google");
        if (Preenchido(g["ClientId"]) && Preenchido(g["ClientSecret"]))
            auth.AddGoogle("Google", o =>
            {
                o.SignInScheme = EsquemaExterno;
                o.ClientId = g["ClientId"]!;
                o.ClientSecret = g["ClientSecret"]!;
                o.CallbackPath = "/entrar/google/retorno";
                o.Scope.Add("email");
                o.Scope.Add("profile");
            });

        var m = config.GetSection("Login:Microsoft");
        if (Preenchido(m["ClientId"]) && Preenchido(m["ClientSecret"]))
            auth.AddMicrosoftAccount("Microsoft", o =>
            {
                o.SignInScheme = EsquemaExterno;
                o.ClientId = m["ClientId"]!;
                o.ClientSecret = m["ClientSecret"]!;
                o.CallbackPath = "/entrar/microsoft/retorno";
            });

        // Apple: Services ID (ClientId), Team ID, Key ID e o arquivo .p8 da chave "Sign in with Apple".
        var a = config.GetSection("Login:Apple");
        if (Preenchido(a["ClientId"]) && Preenchido(a["TeamId"]) && Preenchido(a["KeyId"]) && Preenchido(a["ArquivoChave"]))
            auth.AddApple("Apple", o =>
            {
                o.SignInScheme = EsquemaExterno;
                o.ClientId = a["ClientId"]!;
                o.TeamId = a["TeamId"]!;
                o.KeyId = a["KeyId"]!;
                o.GenerateClientSecret = true;
                var arquivo = Path.IsPathRooted(a["ArquivoChave"]!) ? a["ArquivoChave"]! : Path.Combine(env.ContentRootPath, a["ArquivoChave"]!);
                o.PrivateKey = async (_, ct) => (await File.ReadAllTextAsync(arquivo, ct)).AsMemory();
                o.CallbackPath = "/entrar/apple/retorno";
                o.Scope.Add("name");
                o.Scope.Add("email");
            });
    }

    static bool Preenchido(string? v) => !string.IsNullOrWhiteSpace(v);
}
