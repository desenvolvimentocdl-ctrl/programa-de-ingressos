using System.Threading.RateLimiting;
using Plateia.Domain;
using Plateia.Infrastructure;
using Plateia.Web.Infra;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfraestrutura(builder.Configuration);
builder.Services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
builder.Services.AddScoped<Imagens>();

var autenticacao = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/entrar";
        o.AccessDeniedPath = "/sem-acesso";
        o.Cookie.Name = "plateia";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(10);
        o.SlidingExpiration = true;
    });
// Entrar/criar conta com Google, Apple e Microsoft: cada um só liga quando as credenciais estão configuradas.
LoginExternoConfig.Adicionar(autenticacao, builder.Configuration, builder.Environment);
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(Politicas.Admin, p => p.RequireRole(nameof(Perfil.Administrador)));
    // Tudo que não diz o contrário é área de produtor. Páginas da conta de público usam [Authorize] (só estar logado).
    o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireRole(nameof(Perfil.Organizador), nameof(Perfil.Administrador)).Build();
});

// Formulários públicos (inscrição) e login: limite por IP contra robôs.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("publico", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF");
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
    o.Filters.Add<RegraNegocioFilter>();
});

var app = builder.Build();

// Cria/atualiza as tabelas do banco do Plateia e o primeiro administrador.
await Inicializacao.ExecutarAsync(app.Services, app.Configuration, app.Logger);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/erro");
    app.UseHsts();
}
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    await next();
});
// Endereço oficial do site (ex.: https://meusite.com.br). Quem chega por outro endereço (IP, http) é levado para ele:
// o login com Google/Apple/Microsoft só funciona no endereço cadastrado no provedor. Avisos da Cielo não são redirecionados.
if (app.Configuration["Site:UrlPublica"]?.TrimEnd('/') is { Length: > 0 } urlPublica)
{
    app.Use(async (ctx, next) =>
    {
        var req = ctx.Request;
        var origem = $"{req.Scheme}://{req.Host}";
        if ((HttpMethods.IsGet(req.Method) || HttpMethods.IsHead(req.Method)) && !req.Path.StartsWithSegments("/cielo")
            && !string.Equals(origem, urlPublica, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.Redirect(urlPublica + req.PathBase + req.Path + req.QueryString);
            return;
        }
        await next();
    });
}
app.UseStatusCodePagesWithReExecute("/status/{0}");
app.UseStaticFiles();
// Capas enviadas pelos produtores (pasta configurável, fora do Git).
var pastaUploads = Imagens.PastaConfigurada(app.Configuration, app.Environment);
Directory.CreateDirectory(pastaUploads);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(pastaUploads),
    RequestPath = "/uploads",
    OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "public, max-age=604800"
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
