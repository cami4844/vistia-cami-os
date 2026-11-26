using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ImageMagick;

namespace Vistia.Core;

/// <summary>Lectura local de HEIC, RAW, JXL y otros formatos mediante Magick.NET.</summary>
public static class ImagenesAvanzadas
{
    private const int LadoMaximoVista = 4096;
    private static readonly string CarpetaCache = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Vistia", "cache", "imagenes");

    static ImagenesAvanzadas()
    {
        try
        {
            // Motor portátil incluido con Vistia: EPS/AI PostScript sin instalación.
            string raizLocal = Path.Combine(AppContext.BaseDirectory, "Assets", "ghostscript");
            string binLocal = Path.Combine(raizLocal, "bin");
            if (File.Exists(Path.Combine(binLocal, "gsdll64.dll")))
            {
                MagickNET.SetGhostscriptDirectory(binLocal);
                Environment.SetEnvironmentVariable("GS_LIB", string.Join(";",
                    Path.Combine(raizLocal, "lib"),
                    Path.Combine(raizLocal, "Resource", "Init"),
                    Path.Combine(raizLocal, "Resource", "CMap")));
                Environment.SetEnvironmentVariable("GS_FONTPATH", Path.Combine(raizLocal, "Resource", "Font"));
                return;
            }
            // Reserva: usar Ghostscript del equipo si ya existe.
            foreach (var raiz in new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                string gs = Path.Combine(raiz, "gs");
                if (!Directory.Exists(gs)) continue;
                var bin = Directory.GetDirectories(gs, "gs*")
                    .Select(d => Path.Combine(d, "bin"))
                    .Where(d => File.Exists(Path.Combine(d, "gswin64c.exe")) || File.Exists(Path.Combine(d, "gswin32c.exe")))
                    .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (bin != null)
                {
                    MagickNET.SetGhostscriptDirectory(bin);
                    break;
                }
            }
        }
        catch (Exception ex) { ServidorAssets.Registrar("IMAGEN-GS " + ex.Message); }
    }

    public sealed record Resultado(
        string RutaPng, int AnchoOriginal, int AltoOriginal,
        string FormatoOriginal, int Fotogramas);

    public static Resultado Convertir(string ruta)
    {
        Directory.CreateDirectory(CarpetaCache);
        string destino = Path.Combine(CarpetaCache, Clave(ruta) + ".png");
        int ancho = 0, alto = 0, fotogramas = 1;
        string formato = Path.GetExtension(ruta).TrimStart('.').ToUpperInvariant();

        if (!File.Exists(destino) || new FileInfo(destino).Length == 0)
        {
            string temporal = destino + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using var imagenes = new MagickImageCollection(ruta);
                if (imagenes.Count == 0) throw new InvalidDataException("El archivo no contiene una imagen legible.");
                var imagen = imagenes[0];
                ancho = (int)imagen.Width;
                alto = (int)imagen.Height;
                fotogramas = imagenes.Count;
                formato = imagen.Format.ToString().ToUpperInvariant();
                imagen.AutoOrient();
                if (imagen.Width > LadoMaximoVista || imagen.Height > LadoMaximoVista)
                    imagen.Thumbnail(LadoMaximoVista, LadoMaximoVista);
                imagen.Format = MagickFormat.Png;
                imagen.Write(temporal);
                File.Move(temporal, destino, true);
            }
            finally
            {
                if (File.Exists(temporal)) File.Delete(temporal);
            }
        }

        if (ancho == 0 || alto == 0)
        {
            var info = new MagickImageInfo(ruta);
            ancho = (int)info.Width;
            alto = (int)info.Height;
            formato = info.Format.ToString().ToUpperInvariant();
        }
        return new Resultado(destino, ancho, alto, formato, fotogramas);
    }

    public static bool EsPdfCompatible(string ruta)
    {
        try
        {
            using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> cabecera = stackalloc byte[5];
            return fs.Read(cabecera) == 5 && cabecera.SequenceEqual("%PDF-"u8);
        }
        catch { return false; }
    }

    private static string Clave(string ruta)
    {
        var fi = new FileInfo(ruta);
        string datos = Path.GetFullPath(ruta) + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks + "|" + LadoMaximoVista;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(datos))).ToLowerInvariant();
    }
}
