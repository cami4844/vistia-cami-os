using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Vistia.Core;

/// <summary>Sprites de fotogramas bajo demanda, sin tocar el video principal.</summary>
public sealed class MediaMiniaturas
{
    private readonly ConcurrentDictionary<string, Trabajo> _trabajos = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Vistia", "cache", "miniaturas");
    private CancellationTokenSource _actual = new();

    private sealed class Trabajo
    {
        public string Id = "";
        public string Ruta = "";
        public string Estado = "cola";
        public string Mensaje = "En cola";
        public string Sprite = "";
        public string Json = "";
    }

    public string Solicitar(string ruta, string id)
    {
        Directory.CreateDirectory(_carpeta);
        var t = _trabajos.GetOrAdd(id, _ => new Trabajo { Id = id, Ruta = ruta });
        if (t.Estado == "lista" && !File.Exists(t.Sprite)) t.Estado = "cola";
        if (t.Estado is "preparando" or "lista") return t.Json;
        lock (t)
        {
            if (t.Estado == "preparando") return t.Json;
            t.Estado = "preparando";
            t.Mensaje = "Creando miniaturas";
            _ = Task.Run(() => Generar(t, _actual.Token));
        }
        return t.Json;
    }

    public string Estado(string ruta, string id) => Solicitar(ruta, id);
    public string? Sprite(string id) => _trabajos.TryGetValue(id, out var t) && File.Exists(t.Sprite) ? t.Sprite : null;

    public void Cancelar()
    {
        try { _actual.Cancel(); } catch { }
        _actual.Dispose();
        _actual = new CancellationTokenSource();
        foreach (var t in _trabajos.Values)
        {
            if (t.Estado != "preparando") continue;
            t.Estado = "cola"; t.Mensaje = "En cola";
        }
    }

    private static void AgregarArgumento(ProcessStartInfo psi, string valor)
    {
        if (string.IsNullOrWhiteSpace(valor) || valor.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
            throw new ArgumentException("Argumento inválido", nameof(valor));
        psi.ArgumentList.Add(valor);
    }

    private async Task Generar(Trabajo t, CancellationToken cancelacion)
    {
        string temporal = "";
        try
        {
            double duracion = await Duracion(t.Ruta, cancelacion);
            if (duracion <= 0) throw new InvalidDataException("No se pudo leer la duración");
            const int total = 24, columnas = 6, filas = 4;
            double intervalo = duracion / total;
            string final = Path.Combine(_carpeta, t.Id + ".jpg");
            temporal = Path.Combine(_carpeta, t.Id + ".part.jpg");
            var psi = new ProcessStartInfo("ffmpeg")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true
            };
            AgregarArgumento(psi, "-hide_banner"); AgregarArgumento(psi, "-y");
            AgregarArgumento(psi, "-threads"); AgregarArgumento(psi, "2");
            AgregarArgumento(psi, "-filter_threads"); AgregarArgumento(psi, "1");
            AgregarArgumento(psi, "-i"); AgregarArgumento(psi, t.Ruta);
            AgregarArgumento(psi, "-vf");
            AgregarArgumento(psi, "fps=" + (1 / intervalo).ToString("0.########", CultureInfo.InvariantCulture) + ",scale=240:-1,tile=6x4");
            AgregarArgumento(psi, "-frames:v"); AgregarArgumento(psi, "1");
            AgregarArgumento(psi, "-q:v"); AgregarArgumento(psi, "4");
            AgregarArgumento(psi, temporal);
            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (!p.Start()) throw new InvalidOperationException("No se pudo iniciar FFmpeg");
            var errores = p.StandardError.ReadToEndAsync(cancelacion);
            using var registro = cancelacion.Register(() => { try { if (!p.HasExited) p.Close(); } catch { } });
            var terminado = await Task.WhenAny(p.WaitForExitAsync(cancelacion), Task.Delay(TimeSpan.FromSeconds(75), cancelacion));
            if (terminado is not Task<bool>)
            {
                try { p.Close(); } catch { }
                throw new TimeoutException("La creación de miniaturas superó el tiempo disponible");
            }
            string detalle = await errores;
            if (p.ExitCode != 0) throw new InvalidDataException("No se pudieron crear miniaturas. " + detalle);
            if (!File.Exists(temporal) || new FileInfo(temporal).Length == 0) throw new InvalidDataException("El sprite quedó vacío");
            File.Move(temporal, final, true);
            int alto = 135;
            using (var bmp = new System.Drawing.Bitmap(final)) alto = Math.Max(1, bmp.Height / filas);
            t.Sprite = final;
            t.Estado = "lista"; t.Mensaje = "Listas";
            t.Json = JsonSerializer.Serialize(new
            {
                estado = t.Estado, mensaje = t.Mensaje, url = "/miniatura?id=" + t.Id,
                ancho = 240, alto, intervalo, columnas, filas, total
            });
        }
        catch (Exception ex)
        {
            t.Estado = "error"; t.Mensaje = ex.Message;
            t.Json = JsonSerializer.Serialize(new { estado = t.Estado, mensaje = t.Mensaje });
            ServidorAssets.Registrar("MINIATURAS " + ex.Message);
            if (File.Exists(temporal)) try { File.Delete(temporal); } catch { }
        }
    }

    private static async Task<double> Duracion(string ruta, CancellationToken cancelacion)
    {
        var psi = new ProcessStartInfo("ffprobe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        AgregarArgumento(psi, "-v"); AgregarArgumento(psi, "error");
        AgregarArgumento(psi, "-show_entries"); AgregarArgumento(psi, "format=duration");
        AgregarArgumento(psi, "-of"); AgregarArgumento(psi, "json"); AgregarArgumento(psi, ruta);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ffprobe no está disponible");
        string salida = await p.StandardOutput.ReadToEndAsync(cancelacion);
        await p.WaitForExitAsync(cancelacion);
        using var doc = JsonDocument.Parse(salida);
        string? texto = doc.RootElement.GetProperty("format").GetProperty("duration").GetString();
        return double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out double segundos) ? segundos : 0;
    }
}
