using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Vistia.Core;

public enum ModoVista
{
    ImagenNativa, ImagenWeb, ImagenExtendida, Media, Pdf, Html, Texto, Markdown,
    Docx, Hoja, Presentacion, Zip, Modelo3d, Fuente, Carpeta, Tarjeta, Psd
}

/// <summary>Clasifica un archivo en un modo de vista y entrega sus metadatos.</summary>
public static class PreviewRouter
{
    private static readonly HashSet<string> ImgNativa = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".jpe", ".bmp", ".dib", ".ico", ".tif", ".tiff", ".wdp" };

    private static readonly HashSet<string> ImgWeb = new(StringComparer.OrdinalIgnoreCase)
        { ".gif", ".webp", ".svg", ".svgz", ".avif" };

    // Formatos que ImageMagick lee localmente y Vistia entrega como PNG al visor.
    private static readonly HashSet<string> ImgExtendida = new(StringComparer.OrdinalIgnoreCase)
    {
        ".heic", ".heif", ".hif", ".jxl", ".dng", ".cr2", ".cr3", ".crw", ".nef", ".nrw",
        ".arw", ".srf", ".sr2", ".orf", ".rw2", ".raf", ".pef", ".raw", ".erf", ".kdc",
        ".jp2", ".j2k", ".jpf", ".jpx", ".j2c", ".qoi", ".tga", ".dds", ".exr", ".hdr",
        ".psb", ".xcf", ".ai", ".eps", ".epsf", ".ps"
    };

    private static readonly HashSet<string> Media = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".m4v", ".mov", ".mkv", ".avi", ".wmv", ".flv", ".mpg", ".mpeg",
        ".m2v", ".ts", ".m2ts", ".mts", ".m2t", ".vob", ".3gp", ".3g2", ".divx", ".rm", ".rmvb",
        ".mp3", ".wav", ".ogg", ".oga", ".flac", ".m4a", ".aac", ".opus", ".wma", ".aiff", ".aif", ".ape"
    };

    private static readonly HashSet<string> Html = new(StringComparer.OrdinalIgnoreCase)
        { ".html", ".htm", ".xhtml" };

    private static readonly HashSet<string> Texto = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".json", ".xml", ".yml", ".yaml", ".toml", ".ini", ".cfg", ".conf", ".inf",
        ".properties", ".env", ".reg", ".url",
        ".ps1", ".psm1", ".bat", ".cmd", ".sh", ".bash",
        ".py", ".pyw", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".ino",
        ".css", ".scss", ".less", ".vue", ".svelte",
        ".c", ".h", ".cpp", ".hpp", ".cc", ".cs", ".java", ".kt", ".kts", ".rs", ".go", ".rb", ".php",
        ".sql", ".pas", ".asm", ".s", ".vbs", ".vb", ".f90", ".m", ".r", ".jl", ".lua", ".pl", ".dart",
        ".swift", ".scala", ".fs", ".fsx", ".clj", ".groovy", ".gradle", ".cmake", ".mk",
        ".srt", ".vtt", ".ass", ".ssa", ".nfo", ".diff", ".patch", ".cuh", ".cu", ".xaml", ".razor",
        ".mcmeta", ".mcfunction", ".lang"
    };

    private static readonly HashSet<string> Markdown = new(StringComparer.OrdinalIgnoreCase)
        { ".md", ".markdown", ".mdown", ".mkd" };

    private static readonly HashSet<string> Modelo3d = new(StringComparer.OrdinalIgnoreCase)
        { ".glb", ".gltf", ".obj", ".stl", ".ply", ".fbx", ".3ds", ".dae", ".3mf" };

    private static readonly HashSet<string> Fuente = new(StringComparer.OrdinalIgnoreCase)
        { ".ttf", ".otf", ".woff", ".woff2", ".ttc" };

    private static readonly HashSet<string> Hoja = new(StringComparer.OrdinalIgnoreCase)
        { ".xlsx", ".xlsm", ".xlsb", ".xls", ".csv", ".tsv", ".ods" };

    private static readonly HashSet<string> Docx = new(StringComparer.OrdinalIgnoreCase)
        { ".docx", ".docm", ".dotx", ".dotm" };

    private static readonly HashSet<string> Presentacion = new(StringComparer.OrdinalIgnoreCase)
        { ".pptx", ".pptm", ".ppsx", ".ppsm", ".potx", ".potm" };

    /// <summary>Clasifica una ruta. Devuelve modo + nombre de icono.</summary>
    public static (ModoVista modo, string icono) Clasificar(string ruta)
    {
        try
        {
            if (Directory.Exists(ruta)) return (ModoVista.Carpeta, "carpeta");
        }
        catch { }

        string ext = (Path.GetExtension(ruta) ?? "").ToLowerInvariant();

        if (ImgNativa.Contains(ext)) return (ModoVista.ImagenNativa, "imagen");
        if (ImgWeb.Contains(ext)) return (ModoVista.ImagenWeb, "imagen");
        if (ImgExtendida.Contains(ext)) return (ModoVista.ImagenExtendida, "imagen");
        if (ext == ".pdf") return (ModoVista.Pdf, "pdf");
        if (ext == ".psd") return (ModoVista.Psd, "imagen");
        if (Media.Contains(ext)) return (ModoVista.Media, EsAudio(ext) ? "musica" : "video");
        if (Html.Contains(ext)) return (ModoVista.Html, "texto");
        if (Markdown.Contains(ext)) return (ModoVista.Markdown, "texto");
        if (Docx.Contains(ext)) return (ModoVista.Docx, "documento");
        if (Hoja.Contains(ext)) return (ModoVista.Hoja, "hoja");
        if (Presentacion.Contains(ext)) return (ModoVista.Presentacion, "presentacion");
        if (ext == ".zip" || ext == ".jar" || ext == ".apk" || ext == ".whl" || ext == ".vsix" || ext == ".xpi" || ext == ".docm" || ext == ".xlsm" || ext == ".cbz" || ext == ".epub" || ext == ".mcpack" || ext == ".mcaddon" || ext == ".mcworld" || ext == ".mctemplate")
            return (ModoVista.Zip, "comprimido");
        if (Modelo3d.Contains(ext)) return (ModoVista.Modelo3d, "modelo");
        if (Fuente.Contains(ext)) return (ModoVista.Fuente, "fuente");
        if (Texto.Contains(ext)) return (ModoVista.Texto, "texto");

        // Sin extensión conocida: oler el contenido
        if (PareceTexto(ruta)) return (ModoVista.Texto, "texto");
        return (ModoVista.Tarjeta, "archivo");
    }

    /// <summary>¿El archivo parece texto plano? (sin bytes nulos, mayoría imprimible)</summary>
    public static bool PareceTexto(string ruta)
    {
        try
        {
            var fi = new FileInfo(ruta);
            if (!fi.Exists || fi.Length == 0 || fi.Length > 4_000_000) return false;
            byte[] buf = new byte[Math.Min(4096, (int)fi.Length)];
            using var fs = fi.OpenRead();
            int leido = fs.Read(buf, 0, buf.Length);
            int raros = 0;
            for (int i = 0; i < leido; i++)
            {
                byte b = buf[i];
                if (b == 0) return false;
                if (b < 9 || (b > 13 && b < 32)) raros++;
            }
            return raros * 20 < leido;
        }
        catch { return false; }
    }

    public static bool EsAudio(string ext) => ext.TrimStart('.').ToLowerInvariant() is
        "mp3" or "wav" or "ogg" or "oga" or "flac" or "m4a" or "aac" or "opus" or "wma" or "aiff" or "aif" or "ape";

    public static string Icono3dExt(string ext) => ext.TrimStart('.');
}
