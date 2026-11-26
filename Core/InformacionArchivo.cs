using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ImageMagick;

namespace Vistia.Core;

/// <summary>Metadatos bajo demanda: no frena la apertura normal del archivo.</summary>
public static class InformacionArchivo
{
    public static async Task<List<(string Clave, string Valor)>> ObtenerAsync(string ruta, ModoVista modo)
    {
        var campos = new List<(string, string)>();
        if (Directory.Exists(ruta))
        {
            var di = new DirectoryInfo(ruta);
            campos.Add(("Nombre", di.Name));
            campos.Add(("Tipo", "Carpeta"));
            campos.Add(("Ruta completa", di.FullName));
            try
            {
                campos.Add(("Elementos directos", di.EnumerateFileSystemInfos().Count().ToString("N0")));
                campos.Add(("Subcarpetas directas", di.EnumerateDirectories().Count().ToString("N0")));
                campos.Add(("Archivos directos", di.EnumerateFiles().Count().ToString("N0")));
            }
            catch (Exception ex) { campos.Add(("Lectura", ex.Message)); }
            campos.Add(("Creada", di.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")));
            campos.Add(("Modificada", di.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")));
            return campos;
        }

        var fi = new FileInfo(ruta);
        if (!fi.Exists) return new List<(string, string)> { ("Estado", "El archivo ya no existe") };
        campos.Add(("Nombre", fi.Name));
        campos.Add(("Tipo", string.IsNullOrEmpty(fi.Extension) ? "Archivo" : fi.Extension.TrimStart('.').ToUpperInvariant()));
        campos.Add(("Ruta completa", fi.FullName));
        campos.Add(("Tamaño", TamHumano(fi.Length) + "  (" + fi.Length.ToString("N0") + " bytes)"));
        campos.Add(("Creado", fi.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")));
        campos.Add(("Modificado", fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")));
        campos.Add(("Atributos", fi.Attributes.ToString()));

        if (modo is ModoVista.ImagenNativa or ModoVista.ImagenWeb or ModoVista.ImagenExtendida or ModoVista.Psd)
        {
            await Task.Run(() =>
            {
                try
                {
                    var info = new MagickImageInfo(ruta);
                    campos.Add(("Dimensiones", info.Width + " × " + info.Height + " px"));
                    campos.Add(("Formato detectado", info.Format.ToString()));
                    campos.Add(("Espacio de color", info.ColorSpace.ToString()));
                }
                catch (Exception ex) { campos.Add(("Imagen", "Metadatos no disponibles: " + ex.Message)); }
            });
        }

        if (modo == ModoVista.Media)
        {
            try { await AgregarMedia(campos, ruta); }
            catch (Exception ex) { campos.Add(("Multimedia", "No se pudieron leer los metadatos: " + ex.Message)); }
        }

        if (modo is ModoVista.Texto or ModoVista.Html or ModoVista.Markdown && fi.Length <= 64 * 1024 * 1024)
        {
            await Task.Run(() =>
            {
                try
                {
                    long lineas = 0, caracteres = 0;
                    using var sr = new StreamReader(ruta, detectEncodingFromByteOrderMarks: true);
                    var buffer = new char[81920];
                    int n;
                    while ((n = sr.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        caracteres += n;
                        for (int i = 0; i < n; i++) if (buffer[i] == '\n') lineas++;
                    }
                    if (caracteres > 0) lineas++;
                    campos.Add(("Líneas", lineas.ToString("N0")));
                    campos.Add(("Caracteres", caracteres.ToString("N0")));
                    campos.Add(("Codificación", sr.CurrentEncoding.EncodingName));
                }
                catch { }
            });
        }

        if (modo == ModoVista.Zip)
        {
            try
            {
                using var zip = ZipFile.OpenRead(ruta);
                campos.Add(("Entradas", zip.Entries.Count.ToString("N0")));
                campos.Add(("Contenido sin comprimir", TamHumano(zip.Entries.Sum(e => e.Length))));
            }
            catch { }
        }
        return campos;
    }

    private static async Task AgregarMedia(List<(string Clave, string Valor)> campos, string ruta)
    {
        var psi = new ProcessStartInfo("ffprobe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-show_entries");
        psi.ArgumentList.Add("format=duration,bit_rate:stream=codec_type,codec_name,width,height,r_frame_rate,channels,sample_rate,pix_fmt");
        psi.ArgumentList.Add("-of"); psi.ArgumentList.Add("json"); psi.ArgumentList.Add(ruta);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ffprobe no está disponible");
        var salida = await p.StandardOutput.ReadToEndAsync();
        var error = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidDataException(string.IsNullOrWhiteSpace(error) ? "ffprobe no reconoce el archivo" : error.Trim());
        using var doc = JsonDocument.Parse(salida);
        if (doc.RootElement.TryGetProperty("format", out var fmt))
        {
            if (fmt.TryGetProperty("duration", out var dur) && double.TryParse(dur.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double segundos))
                campos.Add(("Duración", Duracion(segundos)));
            if (fmt.TryGetProperty("bit_rate", out var br) && long.TryParse(br.GetString(), out long bits))
                campos.Add(("Tasa de bits", (bits / 1000.0).ToString("N0") + " kb/s"));
        }
        int numeroAudio = 0;
        foreach (var st in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            string tipo = st.TryGetProperty("codec_type", out var ct) ? ct.GetString() ?? "" : "";
            string codec = st.TryGetProperty("codec_name", out var cn) ? cn.GetString() ?? "" : "";
            if (tipo == "video")
            {
                campos.Add(("Códec de video", codec));
                if (st.TryGetProperty("width", out var w) && st.TryGetProperty("height", out var h))
                    campos.Add(("Resolución", w.GetInt32() + " × " + h.GetInt32() + " px"));
                if (st.TryGetProperty("pix_fmt", out var px)) campos.Add(("Formato de píxel", px.GetString() ?? ""));
                if (st.TryGetProperty("r_frame_rate", out var fr)) campos.Add(("Fotogramas por segundo", fr.GetString() ?? ""));
            }
            else if (tipo == "audio")
            {
                numeroAudio++;
                campos.Add(("Códec de audio " + numeroAudio, codec));
                if (st.TryGetProperty("channels", out var ch)) campos.Add(("Canales de audio " + numeroAudio, ch.GetInt32().ToString()));
                if (st.TryGetProperty("sample_rate", out var sr)) campos.Add(("Muestreo de audio " + numeroAudio, sr.GetString() + " Hz"));
            }
        }
    }

    public static string TamHumano(long b)
    {
        if (b < 1024) return b + " B";
        if (b < 1048576) return (b / 1024.0).ToString("0.#") + " KB";
        if (b < 1073741824) return (b / 1048576.0).ToString("0.#") + " MB";
        return (b / 1073741824.0).ToString("0.##") + " GB";
    }

    private static string Duracion(double segundos)
    {
        var t = TimeSpan.FromSeconds(segundos);
        return t.TotalHours >= 1 ? t.ToString(@"hh\:mm\:ss") : t.ToString(@"mm\:ss");
    }
}
