using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Vistia.Core;

/// <summary>Prepara videos no compatibles sin tocar el original; caché máxima: 5 GB.</summary>
public sealed class ConversionMedia
{
    private const long LimiteCache = 5L * 1024 * 1024 * 1024;
    private readonly ConcurrentDictionary<string, TrabajoMedia> _trabajos = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _unoALaVez = new(1, 1);
    private readonly string _carpeta = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Vistia", "cache", "media");

    public sealed class TrabajoMedia
    {
        public string Id { get; init; } = "";
        public string Ruta { get; init; } = "";
        public string Salida { get; set; } = "";
        public string Estado { get; set; } = "nuevo";
        public string Mensaje { get; set; } = "En cola";
        public double Progreso { get; set; }
        public double Duracion { get; set; }
        public string Mime { get; set; } = "video/mp4";
        public bool ForzarTranscodificar { get; set; }
        public CancellationTokenSource Cancelacion { get; set; } = new();
    }

    public string IdPara(string ruta)
    {
        var fi = new FileInfo(ruta);
        string datos = Path.GetFullPath(ruta) + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks + "|v2";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(datos))).ToLowerInvariant();
    }

    public TrabajoMedia Solicitar(string ruta)
    {
        Directory.CreateDirectory(_carpeta);
        string id = IdPara(ruta);
        while (true)
        {
            var anterior = _trabajos.GetOrAdd(id, _ => new TrabajoMedia { Id = id, Ruta = ruta });
            lock (anterior)
            {
                if (anterior.Estado == "lista" && File.Exists(anterior.Salida)) return anterior;
                if ((anterior.Estado is "preparando" or "cola") && !anterior.Cancelacion.IsCancellationRequested)
                    return anterior;

                // Un trabajo cancelado nunca se reutiliza: su tarea todavía puede
                // terminar tarde. El intento nuevo tiene estado y temporal propios.
                var trabajo = new TrabajoMedia
                {
                    Id = id,
                    Ruta = ruta,
                    Estado = "cola",
                    Mensaje = "En cola",
                    ForzarTranscodificar = anterior.ForzarTranscodificar
                };
                if (!_trabajos.TryUpdate(id, trabajo, anterior)) continue;
                _ = Task.Run(() => Convertir(trabajo));
                return trabajo;
            }
        }
    }

    public TrabajoMedia Forzar(string ruta)
    {
        string id = IdPara(ruta);
        var t = _trabajos.GetOrAdd(id, _ => new TrabajoMedia { Id = id, Ruta = ruta });
        lock (t)
        {
            t.ForzarTranscodificar = true;
            try { t.Cancelacion.Cancel(); } catch { }
            if (!string.IsNullOrEmpty(t.Salida) && File.Exists(t.Salida))
            {
                try { File.Delete(t.Salida); } catch { }
            }
            t.Estado = "error";
            t.Mensaje = "Forzando copia compatible";
        }
        return Solicitar(ruta);
    }

    public TrabajoMedia? Obtener(string id) => _trabajos.TryGetValue(id, out var t) ? t : null;

    public void CancelarTodo()
    {
        foreach (var t in _trabajos.Values)
        {
            if (t.Estado is not ("preparando" or "cola")) continue;
            try { t.Cancelacion.Cancel(); } catch { }
        }
    }

    public void MarcarAcceso(TrabajoMedia trabajo)
    {
        try { if (File.Exists(trabajo.Salida)) File.SetLastAccessTimeUtc(trabajo.Salida, DateTime.UtcNow); } catch { }
    }

    private async Task Convertir(TrabajoMedia t)
    {
        bool semaforoTomado = false;
        string temporal = "";
        try
        {
            await _unoALaVez.WaitAsync(t.Cancelacion.Token);
            semaforoTomado = true;
            t.Cancelacion.Token.ThrowIfCancellationRequested();
            t.Estado = "preparando";
            t.Mensaje = "Leyendo el formato";
            var sonda = await Sondear(t.Ruta, t.Cancelacion.Token);
            t.Duracion = sonda.Duracion;

            bool alfa = sonda.PixFmt.StartsWith("yuva", StringComparison.OrdinalIgnoreCase)
                || sonda.PixFmt is "rgba" or "argb" or "abgr" or "bgra" or "gbrap";
            bool videoWebmCopiable = alfa && (sonda.CodecVideo is "vp9" or "vp8" or "av1") && !t.ForzarTranscodificar;
            bool videoMp4Copiable = !alfa && sonda.CodecVideo == "h264" && !t.ForzarTranscodificar;
            string extension = !sonda.TieneVideo ? ".m4a" : alfa ? ".webm" : ".mp4";
            string final = Path.Combine(_carpeta, t.Id + extension);
            t.Salida = final;
            t.Mime = extension == ".webm" ? "video/webm" : extension == ".m4a" ? "audio/mp4" : "video/mp4";
            if (File.Exists(final) && new FileInfo(final).Length > 0)
            {
                lock (t)
                {
                    if (!EsActual(t)) return;
                    t.Estado = "lista"; t.Progreso = 1; t.Mensaje = "Lista desde caché";
                }
                MarcarAcceso(t); return;
            }

            temporal = Path.Combine(_carpeta, t.Id + "." + Guid.NewGuid().ToString("N") + ".part" + extension);
            t.Mensaje = alfa ? "Preparando video con transparencia" : videoMp4Copiable ? "Adaptando el contenedor, sin recomprimir el video" : "Preparando una copia compatible";
            ServidorAssets.Registrar("MEDIA-INICIO " + Path.GetFileName(t.Ruta) + " alfa=" + alfa + " copiaVideo=" + (videoMp4Copiable || videoWebmCopiable));

            var psi = new ProcessStartInfo("ffmpeg")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true
            };
            psi.ArgumentList.Add("-hide_banner"); psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(t.Ruta);
            psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("0:v:0?");
            psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("0:a:0?");
            psi.ArgumentList.Add("-sn"); psi.ArgumentList.Add("-dn");
            if (!sonda.TieneVideo)
            {
                psi.ArgumentList.Add("-vn"); psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("aac"); psi.ArgumentList.Add("-b:a"); psi.ArgumentList.Add("192k");
                psi.ArgumentList.Add("-movflags"); psi.ArgumentList.Add("+faststart");
            }
            else if (alfa)
            {
                if (videoWebmCopiable) { psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("copy"); }
                else
                {
                    psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("libvpx-vp9");
                    psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("yuva420p");
                    psi.ArgumentList.Add("-crf"); psi.ArgumentList.Add("20"); psi.ArgumentList.Add("-b:v"); psi.ArgumentList.Add("0");
                    psi.ArgumentList.Add("-deadline"); psi.ArgumentList.Add("realtime"); psi.ArgumentList.Add("-cpu-used"); psi.ArgumentList.Add("5");
                    psi.ArgumentList.Add("-row-mt"); psi.ArgumentList.Add("1");
                }
                psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("libopus"); psi.ArgumentList.Add("-b:a"); psi.ArgumentList.Add("160k");
            }
            else
            {
                if (videoMp4Copiable) { psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("copy"); }
                else
                {
                    psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("libx264"); psi.ArgumentList.Add("-preset"); psi.ArgumentList.Add("veryfast");
                    psi.ArgumentList.Add("-crf"); psi.ArgumentList.Add("20"); psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("yuv420p");
                }
                psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("aac"); psi.ArgumentList.Add("-b:a"); psi.ArgumentList.Add("192k");
                psi.ArgumentList.Add("-movflags"); psi.ArgumentList.Add("+faststart");
            }
            psi.ArgumentList.Add(temporal);
            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var errores = new List<string>();
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                if (errores.Count < 80) errores.Add(e.Data);
                var m = Regex.Match(e.Data, @"time=(\d+):(\d+):(\d+(?:\.\d+)?)");
                if (m.Success && t.Duracion > 0)
                {
                    double actual = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60
                        + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                    t.Progreso = Math.Clamp(actual / t.Duracion, 0, 0.99);
                }
            };
            if (!p.Start()) throw new InvalidOperationException("No se pudo iniciar FFmpeg");
            p.BeginErrorReadLine(); p.BeginOutputReadLine();
            using var registroCancelacion = t.Cancelacion.Token.Register(() =>
            {
                try { if (!p.HasExited) p.Kill(true); } catch { }
            });
            await p.WaitForExitAsync(t.Cancelacion.Token);
            t.Cancelacion.Token.ThrowIfCancellationRequested();
            if (p.ExitCode != 0) throw new InvalidDataException("FFmpeg no pudo preparar este archivo. " + string.Join(" | ", errores.TakeLast(5)));
            if (!File.Exists(temporal) || new FileInfo(temporal).Length == 0) throw new InvalidDataException("La copia compatible quedó vacía");
            lock (t)
            {
                // Si otro intento reemplazó este trabajo, no puede publicar su salida.
                if (!EsActual(t) || t.Cancelacion.IsCancellationRequested) return;
                File.Move(temporal, final, true);
                t.Estado = "lista"; t.Progreso = 1; t.Mensaje = "Lista";
            }
            LimpiarCache(t.Id);
            ServidorAssets.Registrar("MEDIA-LISTA " + t.Id + " bytes=" + new FileInfo(final).Length);
        }
        catch (OperationCanceledException)
        {
            t.Estado = "error"; t.Mensaje = "Preparación cancelada";
        }
        catch (Exception ex)
        {
            t.Estado = "error"; t.Mensaje = ex.Message;
            ServidorAssets.Registrar("MEDIA-ERROR " + ex.Message);
        }
        finally
        {
            if (temporal.Length > 0)
            {
                try { if (File.Exists(temporal)) File.Delete(temporal); } catch { }
            }
            if (semaforoTomado) _unoALaVez.Release();
        }
    }

    private bool EsActual(TrabajoMedia t) =>
        _trabajos.TryGetValue(t.Id, out var actual) && ReferenceEquals(actual, t);

    private sealed record Sonda(bool TieneVideo, string CodecVideo, string PixFmt, double Duracion);

    private static async Task<Sonda> Sondear(string ruta, CancellationToken cancelacion)
    {
        var psi = new ProcessStartInfo("ffprobe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-show_entries"); psi.ArgumentList.Add("format=duration:stream=codec_type,codec_name,pix_fmt");
        psi.ArgumentList.Add("-of"); psi.ArgumentList.Add("json"); psi.ArgumentList.Add(ruta);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ffprobe no está disponible");
        string salida = await p.StandardOutput.ReadToEndAsync(cancelacion);
        string error = await p.StandardError.ReadToEndAsync(cancelacion);
        await p.WaitForExitAsync(cancelacion);
        if (p.ExitCode != 0) throw new InvalidDataException(string.IsNullOrWhiteSpace(error) ? "No se reconoce el formato" : error.Trim());
        using var doc = JsonDocument.Parse(salida);
        bool tieneVideo = false; string codec = "", pix = "";
        foreach (var st in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            if (!st.TryGetProperty("codec_type", out var ct) || ct.GetString() != "video") continue;
            if (st.TryGetProperty("codec_name", out var cn)) codec = cn.GetString() ?? "";
            if (st.TryGetProperty("pix_fmt", out var pf)) pix = pf.GetString() ?? "";
            tieneVideo = true; break;
        }
        double duracion = 0;
        if (doc.RootElement.TryGetProperty("format", out var fmt) && fmt.TryGetProperty("duration", out var d))
            double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out duracion);
        return new Sonda(tieneVideo, codec, pix, duracion);
    }

    private void LimpiarCache(string idProtegido)
    {
        try
        {
            var archivos = new DirectoryInfo(_carpeta).EnumerateFiles()
                .Where(f => f.Name.Contains(".part.") ? DateTime.UtcNow - f.LastWriteTimeUtc > TimeSpan.FromDays(1) : true)
                .OrderByDescending(f => f.LastAccessTimeUtc).ToList();
            long total = archivos.Sum(f => f.Length);
            foreach (var f in archivos.OrderBy(f => f.LastAccessTimeUtc))
            {
                if (total <= LimiteCache) break;
                if (f.Name.StartsWith(idProtegido, StringComparison.OrdinalIgnoreCase)) continue;
                long bytes = f.Length; f.Delete(); total -= bytes;
            }
        }
        catch { }
    }
}
