using Plateia.Application;

namespace Plateia.Web.Infra;

/// <summary>
/// Imagens de capa enviadas pelos produtores. Ficam numa pasta configurável (Uploads:Pasta), fora do Git,
/// servidas em /uploads. O tipo é conferido pelos primeiros bytes do arquivo, não pela extensão.
/// </summary>
public class Imagens(IConfiguration config, IWebHostEnvironment env)
{
    public const long TamanhoMaximo = 4 * 1024 * 1024;

    public string Pasta => PastaConfigurada(config, env);

    public static string PastaConfigurada(IConfiguration config, IWebHostEnvironment env) =>
        config["Uploads:Pasta"] is { Length: > 0 } p ? p : Path.Combine(env.ContentRootPath, "dados", "uploads");

    public async Task<string> SalvarCapaAsync(IFormFile arquivo)
    {
        if (arquivo.Length == 0) throw new RegraNegocioException("A imagem enviada está vazia.");
        if (arquivo.Length > TamanhoMaximo) throw new RegraNegocioException("A imagem precisa ter no máximo 4 MB.");

        var cabeca = new byte[12];
        await using (var s = arquivo.OpenReadStream()) _ = await s.ReadAsync(cabeca);
        var ext = cabeca switch
        {
            [0xFF, 0xD8, 0xFF, ..] => "jpg",
            [0x89, 0x50, 0x4E, 0x47, ..] => "png",
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P'] => "webp",
            _ => throw new RegraNegocioException("Envie a capa em JPG, PNG ou WEBP.")
        };

        var pasta = Path.Combine(Pasta, "eventos");
        Directory.CreateDirectory(pasta);
        var nome = $"{Guid.NewGuid():N}.{ext}";
        await using (var destino = File.Create(Path.Combine(pasta, nome)))
            await arquivo.CopyToAsync(destino);
        return $"/uploads/eventos/{nome}";
    }

    /// <summary>Apaga uma capa antiga (só arquivos dentro da pasta de uploads).</summary>
    public void Apagar(string? caminho)
    {
        if (caminho is null || !caminho.StartsWith("/uploads/eventos/")) return;
        var nome = Path.GetFileName(caminho);
        var arquivo = Path.Combine(Pasta, "eventos", nome);
        if (File.Exists(arquivo)) File.Delete(arquivo);
    }
}
