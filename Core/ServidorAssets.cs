using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Vistia.Core.Minecraft;

namespace Vistia.Core;

/// <summary>
/// Sirve el visor (assets) y el archivo en vista al WebView2 por un host virtual
/// (https://vistia.local). Cero internet: todo sale del disco local.
/// Soporta Range para que el video pueda avanzar y retroceder.
/// </summary>
public sealed class ServidorAssets
{
    public const string Origen = "https://vistia.local";

    private readonly string _raiz;
    private readonly ConversionMedia _media = new();
    private readonly MediaMiniaturas _motorMiniaturas = new();
    private string _archivoActual = "";
    private ModoVista _modoActual = ModoVista.Tarjeta;
    private string _jsonActual = "{}";
    private Task<string> _tareaJson = Task.FromResult("{}");
    private int _versionEstado;
    private readonly object _candado = new();
    private readonly Dictionary<int, byte[]> _miniaturas = new();
    private readonly Dictionary<int, string> _rutaMiniatura = new();
    private volatile bool _listo;

    private static readonly Dictionary<string, string> Tipos = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".xml"] = "application/xml; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".mp4"] = "video/mp4",
        [".mov"] = "video/mp4",
        [".ico"] = "image/x-icon",
        [".wasm"] = "application/wasm",
        [".woff2"] = "font/woff2",
        [".woff"] = "font/woff",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
    };

    /// <summary>Registro de diagnóstico en %LOCALAPPDATA%\Vistia\registro.log</summary>
    public static void Registrar(string linea)
    {
        try
        {
            string carpeta = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vistia");
            Directory.CreateDirectory(carpeta);
            lock (_candadoLog)
            {
                File.AppendAllText(Path.Combine(carpeta, "registro.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + linea + Environment.NewLine);
            }
        }
        catch { }
    }

    private static readonly object _candadoLog = new();

    public ServidorAssets(string raizAssets)
    {
        _raiz = Path.GetFullPath(raizAssets);
    }

    /// <summary>Nuevo archivo en vista: prepara contexto para /file y JSONs.</summary>
    public void EstablecerArchivo(string ruta, ModoVista modo, string tema, bool autogiro)
    {
        int version;
        var tarea = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_candado)
        {
            _archivoActual = ruta;
            _modoActual = modo;
            _media.CancelarTodo();
            _motorMiniaturas.Cancelar();
            _jsonActual = "{}";
            _miniaturas.Clear();
            _rutaMiniatura.Clear();
            _listo = false;
            version = ++_versionEstado;
            _tareaJson = tarea.Task;
        }
        Task.Run(() =>
        {
            string json;
            try
            {
                json = GenerarJson(ruta, modo, tema, autogiro);
                json = AgregarArchivoId(json, ruta);
            }
            catch (Exception ex)
            {
                Registrar("ESTADO EXCEPCION " + ex);
                json = "{\"modo\":\"tarjeta\",\"tema\":\"" + tema + "\",\"nombre\":" + JsonEncoderEscape.Escape(Path.GetFileName(ruta)) +
                    ",\"error\":" + JsonEncoderEscape.Escape(ex.Message) + "}";
            }
            lock (_candado)
            {
                // Una conversión o lectura lenta de un archivo anterior jamás pisa el nuevo.
                if (version == _versionEstado)
                {
                    _jsonActual = json;
                    _listo = true;
                }
            }
            tarea.TrySetResult(json);
        });
    }

    public void Conectar(CoreWebView2 nucleo)
    {
        nucleo.AddWebResourceRequestedFilter(Origen + "/*", CoreWebView2WebResourceContext.All);
        nucleo.WebResourceRequested += AlPedirRecurso;
    }

    // El evento corre en el hilo del navegador-dispatcher y DEBE responderse ahí:
    // los args de WebView2 no son accesibles desde otros hilos (deferral probado =
    // cuelgue). El hilo no se bloquea en la práctica: el JSON se pregenera antes de
    // navegar y los streams de archivos los consume el navegador aparte.
    private void AlPedirRecurso(object? remitente, CoreWebView2WebResourceRequestedEventArgs e)
    {
        ManejarPedido(e);
    }

    private void ManejarPedido(CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var uri = new Uri(e.Request.Uri);
            string ruta = uri.AbsolutePath;
            Registrar("REQ " + ruta);

            if (ruta == "/ping") { Responder(e, Encoding.UTF8.GetBytes("ok"), "text/plain"); return; }

            if (ruta == "/media-preparar")
            {
                lock (_candado)
                {
                    string id = ValorConsulta(uri.Query, "id");
                    string actual = _archivoActual;
                    if (actual == "" || !File.Exists(actual) || id != _media.IdPara(actual))
                    {
                        Responder(e, Encoding.UTF8.GetBytes("{\"estado\":\"error\",\"mensaje\":\"El archivo cambió; vuelve a intentarlo\"}"), "application/json", 409);
                        return;
                    }
                    var trabajo = _media.Solicitar(actual);
                    Responder(e, Encoding.UTF8.GetBytes(JsonMedia(trabajo)), "application/json");
                    return;
                }
            }

            if (ruta == "/media-forzar")
            {
                lock (_candado)
                {
                    string id = ValorConsulta(uri.Query, "id");
                    string actual = _archivoActual;
                    if (actual == "" || !File.Exists(actual) || id != _media.IdPara(actual))
                    {
                        Responder(e, Encoding.UTF8.GetBytes("{\"estado\":\"error\",\"mensaje\":\"El archivo cambió\"}"), "application/json", 409);
                        return;
                    }
                    Responder(e, Encoding.UTF8.GetBytes(JsonMedia(_media.Forzar(actual))), "application/json");
                    return;
                }
            }

            if (ruta == "/media-estado")
            {
                var trabajo = _media.Obtener(ValorConsulta(uri.Query, "id"));
                if (trabajo == null) { Responder(e, Encoding.UTF8.GetBytes("{\"estado\":\"error\",\"mensaje\":\"Preparación no encontrada\"}"), "application/json", 404); return; }
                Responder(e, Encoding.UTF8.GetBytes(JsonMedia(trabajo)), "application/json");
                return;
            }

            if (ruta == "/miniaturas-estado")
            {
                string id = ValorConsulta(uri.Query, "id");
                string actual = _archivoActual;
                if (actual == "" || !File.Exists(actual) || id != _media.IdPara(actual))
                {
                    Responder(e, Encoding.UTF8.GetBytes("{\"estado\":\"error\",\"mensaje\":\"El archivo cambió\"}"), "application/json", 409);
                    return;
                }
                Responder(e, Encoding.UTF8.GetBytes(_motorMiniaturas.Estado(actual, id)), "application/json");
                return;
            }

            if (ruta == "/miniatura")
            {
                string? sprite = _motorMiniaturas.Sprite(ValorConsulta(uri.Query, "id"));
                if (sprite == null || !File.Exists(sprite)) { Responder(e, Array.Empty<byte>(), "image/jpeg", 404); return; }
                ServirArchivoFisico(e, sprite, "image/jpeg");
                return;
            }

            if (ruta.StartsWith("/media-cache/"))
            {
                string id = ruta.Substring("/media-cache/".Length);
                var trabajo = _media.Obtener(id);
                if (trabajo == null || trabajo.Estado != "lista" || !File.Exists(trabajo.Salida))
                {
                    Responder(e, Array.Empty<byte>(), "text/plain", 404); return;
                }
                _media.MarcarAcceso(trabajo);
                ServirArchivoFisico(e, trabajo.Salida, trabajo.Mime,
                    new List<(string, string)> { ("Cache-Control", "no-store") });
                return;
            }

            if (ruta.StartsWith("/image-cache/"))
            {
                string nombre = Path.GetFileName(Uri.UnescapeDataString(ruta.Substring("/image-cache/".Length)));
                string fisico = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Vistia", "cache", "imagenes", nombre);
                if (File.Exists(fisico)) ServirArchivoFisico(e, fisico, "image/png");
                else Responder(e, Array.Empty<byte>(), "image/png", 404);
                return;
            }

            if (ruta.StartsWith("/html/"))
            {
                ServirHtml(e, ruta);
                return;
            }

            // assets estáticos
            if (ruta.StartsWith("/css/") || ruta.StartsWith("/app/") || ruta.StartsWith("/js/") || ruta == "/viewer.html")
            {
                string rel = ruta == "/viewer.html" ? "viewer.html" : ruta.TrimStart('/');
                string fisico = RutaSegura(rel);
                if (fisico != "" && File.Exists(fisico))
                {
                    // Solo las bibliotecas cambian poco. CSS, viewer y visores propios
                    // deben revalidarse o una actualización conserva el diseño anterior.
                    var extra = (ruta == "/viewer.html" || ruta.StartsWith("/app/") || ruta.StartsWith("/css/"))
                        ? new List<(string, string)> { ("Cache-Control", "no-cache") }
                        : new List<(string, string)> { ("Cache-Control", "max-age=31536000") };
                    Responder(e, File.ReadAllBytes(fisico), TipoPorExt(Path.GetExtension(fisico)), 200, extra);
                }
                else if (_modoActual == ModoVista.Html) ServirHtml(e, "/html" + ruta);
                else Responder(e, Encoding.UTF8.GetBytes("// no encontrado"), "text/javascript", 404);
                return;
            }

            if (ruta == "/estado.json")
            {
                EsperarJson();
                Responder(e, Encoding.UTF8.GetBytes(_jsonActual), "application/json");
                return;
            }

            if (ruta == "/folder.json")
            {
                EsperarJson();
                Responder(e, Encoding.UTF8.GetBytes(_jsonActual), "application/json");
                return;
            }

            if (ruta.StartsWith("/thumb/"))
            {
                int idx = int.TryParse(ruta.Substring(7), out int n) ? n : -1;
                byte[]? png = Miniatura(idx);
                if (png != null) Responder(e, png, "image/png");
                else Responder(e, Array.Empty<byte>(), "image/png", 404);
                return;
            }

            if (ruta.StartsWith("/zipfile/"))
            {
                ServirZipInterno(e, ruta);
                return;
            }

            if (ruta.StartsWith("/file/"))
            {
                ServirArchivo(e);
                return;
            }

            if (_modoActual == ModoVista.Html) ServirHtml(e, "/html" + ruta);
            else Responder(e, Encoding.UTF8.GetBytes("no encontrado"), "text/plain", 404);
        }
        catch (Exception ex)
        {
            Registrar("ERROR handler " + e.Request.Uri + " : " + ex.Message);
            try { Responder(e, Encoding.UTF8.GetBytes("error: " + ex.Message), "text/plain", 500); } catch { }
        }
    }

    /// <summary>Sirve una entrada INTERNA del comprimido actual sin descomprimir al disco.</summary>
    private void ServirZipInterno(CoreWebView2WebResourceRequestedEventArgs e, string ruta)
    {
        try
        {
            // /zipfile/<indice>/<ext>
            string[] partes = ruta.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length < 2 || !int.TryParse(partes[1], out int indice))
            {
                Responder(e, Encoding.UTF8.GetBytes("indice invalido"), "text/plain", 400);
                return;
            }
            string ext = partes.Length > 2 ? partes[2] : "";
            string archivo = _archivoActual;
            if (archivo == "" || !File.Exists(archivo))
            {
                Responder(e, Array.Empty<byte>(), "text/plain", 404);
                return;
            }
            using var fs = new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read);
            if (indice < 0 || indice >= zip.Entries.Count)
            {
                Responder(e, Array.Empty<byte>(), "text/plain", 404);
                return;
            }
            var entrada = zip.Entries[indice];
            if (entrada.Length > 64 * 1024 * 1024)
            {
                Responder(e, Encoding.UTF8.GetBytes("La entrada supera el límite de 64 MB para lectura directa"), "text/plain", 413);
                return;
            }
            using var flujo = entrada.Open();
            using var memoria = new MemoryStream();
            flujo.CopyTo(memoria);
            Responder(e, memoria.ToArray(), TipoPorExt("." + ext.TrimStart('.')));
        }
        catch (Exception ex)
        {
            Registrar("ZIPINTERNO " + ex.Message);
            Responder(e, Encoding.UTF8.GetBytes("error: " + ex.Message), "text/plain", 500);
        }
    }

    private string AgregarArchivoId(string json, string ruta)
    {
        try
        {
            int cierre = json.LastIndexOf('}');
            if (cierre < 0 || ruta.Length == 0) return json;
            string id = _media.IdPara(ruta);
            return json.Substring(0, cierre) + ",\"archivoId\":\"" + id + "\"" + json.Substring(cierre);
        }
        catch { return json; }
    }

    /// <summary>Publica un JSON construido por el controlador (zip interno y similares).</summary>
    public void EstablecerEstadoDirecto(string json)
    {
        lock (_candado)
        {
            _versionEstado++;
            _jsonActual = AgregarArchivoId(json, _archivoActual);
            _tareaJson = Task.FromResult(_jsonActual);
            _listo = true;
        }
    }

    private void EsperarJson()
    {
        for (int i = 0; i < 2400; i++) // máx ~1.5 s; el JSON suele estar listo de antes
        {
            bool listo;
            lock (_candado) { listo = _listo; }
            if (listo) return;
            System.Threading.Thread.Sleep(25);
        }
    }

    /// <summary>Devuelve la tarea exacta del archivo que se acaba de pedir, no una bandera compartida.</summary>
    public System.Threading.Tasks.Task<string> EsperarJsonAsync()
    {
        lock (_candado) { return _tareaJson; }
    }

    /// <summary>JSON del archivo/estado actual (para el canal de mensajes del visor).</summary>
    public string JsonActual
    {
        get { lock (_candado) { return _jsonActual; } }
    }

    private string RutaSegura(string relativa)
    {
        if (relativa.Contains("..")) return "";
        foreach (char c in relativa)
            if (!(char.IsLetterOrDigit(c) || c == '/' || c == '.' || c == '-' || c == '_' || c == ' ')) return "";
        string fisico = Path.GetFullPath(Path.Combine(_raiz, relativa.Replace('/', Path.DirectorySeparatorChar)));
        string prefijo = _raiz.EndsWith(Path.DirectorySeparatorChar) ? _raiz : _raiz + Path.DirectorySeparatorChar;
        if (!fisico.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)) return "";
        return fisico;
    }

    private void ServirArchivo(CoreWebView2WebResourceRequestedEventArgs e)
    {
        string ruta;
        lock (_candado) { ruta = _archivoActual; }
        if (ruta == "" || !File.Exists(ruta)) { Responder(e, Array.Empty<byte>(), "text/plain", 404); return; }
        string id = ValorConsulta(new Uri(e.Request.Uri).Query, "id");
        if (id.Length > 0 && id != _media.IdPara(ruta))
        {
            Registrar("ARCHIVO-OBSOLETO id=" + id);
            Responder(e, Encoding.UTF8.GetBytes("El archivo cambió; cierra esta carga anterior"), "text/plain", 409);
            return;
        }

        // 3D nativo: STL/OBJ/PLY se convierten a GLB con el parser propio de Vistia
        string ext3d = Path.GetExtension(ruta).TrimStart('.').ToLowerInvariant();
        if (ext3d == "stl" || ext3d == "obj" || ext3d == "ply")
        {
            var glb = Convertidor3d.Convertir(ruta, ext3d);
            if (glb != null && glb.Length > 0)
            {
                Registrar("3D-NATIVO " + ext3d + " glb=" + glb.Length);
                Responder(e, glb, "model/gltf-binary");
                return;
            }
            Registrar("3D-NATIVO fallo " + ext3d + " -> original");
        }
        ServirArchivoFisico(e, ruta, TipoArchivo(ruta),
            new List<(string, string)> { ("Cache-Control", "no-store") });
    }

    private void ServirArchivoFisico(CoreWebView2WebResourceRequestedEventArgs e, string ruta, string tipo,
        List<(string, string)>? adicionales = null)
    {
        long total = new FileInfo(ruta).Length;
        var cabeceras = new List<(string, string)>
        {
            ("Access-Control-Allow-Origin", "null"),
            ("Accept-Ranges", "bytes"),
            ("X-Content-Type-Options", "nosniff")
        };
        if (adicionales != null) cabeceras.AddRange(adicionales);

        string rango = "";
        try { if (e.Request.Headers.Contains("Range")) rango = e.Request.Headers.GetHeader("Range") ?? ""; } catch { }

        if (rango.Length > 0)
        {
            if (!RangoHttp.Intentar(rango, total, out var parcial))
            {
                cabeceras.Add(("Content-Range", $"bytes */{total}"));
                cabeceras.Add(("Content-Length", "0"));
                e.Response = CrearRespuesta(e, new MemoryStream(), tipo, 416, cabeceras);
                return;
            }
            long longitud = parcial.Hasta - parcial.Desde + 1;
            var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(parcial.Desde, SeekOrigin.Begin);
            cabeceras.Add(("Content-Range", $"bytes {parcial.Desde}-{parcial.Hasta}/{total}"));
            cabeceras.Add(("Content-Length", longitud.ToString()));
            e.Response = CrearRespuesta(e, new RangoStream(fs, longitud), tipo, 206, cabeceras);
            return;
        }

        cabeceras.Add(("Content-Length", total.ToString()));
        var fs2 = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        e.Response = CrearRespuesta(e, fs2, tipo, 200, cabeceras);
    }

    private void ServirHtml(CoreWebView2WebResourceRequestedEventArgs e, string ruta)
    {
        string actual = _archivoActual;
        if (_modoActual != ModoVista.Html || actual == "" || !File.Exists(actual))
        {
            Responder(e, Array.Empty<byte>(), "text/plain", 404); return;
        }
        string id = ValorConsulta(new Uri(e.Request.Uri).Query, "id");
        if (id.Length > 0 && id != _media.IdPara(actual))
        {
            Responder(e, Encoding.UTF8.GetBytes("El archivo cambió; cierra esta carga anterior"), "text/plain", 409);
            return;
        }
        string baseDir = Path.GetFullPath(Path.GetDirectoryName(actual) ?? "");
        string rel = Uri.UnescapeDataString(ruta.Substring("/html/".Length)).Replace('/', Path.DirectorySeparatorChar);
        string destino = string.IsNullOrEmpty(rel) || string.Equals(rel, Path.GetFileName(actual), StringComparison.OrdinalIgnoreCase)
            ? actual : Path.GetFullPath(Path.Combine(baseDir, rel));
        string prefijo = baseDir.EndsWith(Path.DirectorySeparatorChar) ? baseDir : baseDir + Path.DirectorySeparatorChar;
        if (!string.Equals(destino, actual, StringComparison.OrdinalIgnoreCase) && !destino.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
        {
            Responder(e, Encoding.UTF8.GetBytes("Recurso fuera de la carpeta local"), "text/plain", 403); return;
        }
        if (!File.Exists(destino)) { Responder(e, Array.Empty<byte>(), "text/plain", 404); return; }
        var cabeceras = new List<(string, string)>
        {
            ("Cache-Control", "no-cache"),
            ("Content-Security-Policy", "default-src 'self' data: blob:; script-src 'self' 'unsafe-inline' 'unsafe-eval' blob: data:; style-src 'self' 'unsafe-inline' data:; img-src 'self' data: blob:; media-src 'self' data: blob:; font-src 'self' data:; connect-src 'self' data: blob:; frame-src 'self' data: blob:; object-src 'none'; form-action 'none'; base-uri 'self'")
        };
        ServirArchivoFisico(e, destino, TipoArchivo(destino), cabeceras);
    }

    private static string ValorConsulta(string consulta, string clave)
    {
        foreach (var parte in consulta.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var trozos = parte.Split('=', 2);
            if (trozos.Length == 2 && string.Equals(trozos[0], clave, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(trozos[1]);
        }
        return "";
    }

    private static string JsonMedia(ConversionMedia.TrabajoMedia t)
    {
        string url = t.Estado == "lista" ? "/media-cache/" + t.Id : "";
        return "{\"estado\":\"" + t.Estado + "\",\"mensaje\":" + JsonEncoderEscape.Escape(t.Mensaje) +
            ",\"progreso\":" + t.Progreso.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
            ",\"url\":\"" + url + "\"}";
    }

    /// <summary>Stream que se cierra solo al llegar a su límite (para 206).</summary>
    private sealed class RangoStream : Stream
    {
        private readonly FileStream _base;
        private long _restante;
        public RangoStream(FileStream fs, long longitud) { _base = fs; _restante = longitud; }
        public override bool CanRead => _restante > 0;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _restante;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_restante <= 0) return 0;
            int paraLeer = (int)Math.Min(count, _restante);
            int leido = _base.Read(buffer, offset, paraLeer);
            _restante -= leido;
            if (_restante <= 0) _base.Dispose();
            return leido;
        }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        protected override void Dispose(bool dispose) { _base.Dispose(); base.Dispose(dispose); }
    }

    private byte[]? Miniatura(int idx)
    {
        lock (_candado)
        {
            if (_miniaturas.TryGetValue(idx, out var cache)) return cache;
        }
        string ruta;
        lock (_candado)
        {
            if (!_rutaMiniatura.TryGetValue(idx, out ruta!)) return null;
        }
        // BitmapImage exige hilo STA: el servidor corre en hilo de fondo, así que
        // la creación del PNG va en un hilo STA propio.
        byte[]? resultado = null;
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            resultado = CrearPng(ruta);
        }
        else
        {
            var h = new Thread(() => resultado = CrearPng(ruta)) { IsBackground = true };
            h.SetApartmentState(ApartmentState.STA);
            h.Start();
            h.Join(5000);
        }
        if (resultado != null)
        {
            lock (_candado) { _miniaturas[idx] = resultado; }
        }
        return resultado;
    }

    private byte[]? CrearPng(string ruta)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(ruta);
            bi.DecodePixelWidth = 320;
            bi.EndInit();
            bi.Freeze();
            var cod = new PngBitmapEncoder();
            cod.Frames.Add(BitmapFrame.Create(bi));
            using var ms = new MemoryStream();
            cod.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    private string GenerarJson(string ruta, ModoVista modo, string tema, bool autogiro)
    {
        string tipo = Path.GetExtension(ruta).TrimStart('.').ToLowerInvariant();
        string nombre = Path.GetFileName(ruta);
        string esc(string s) => JsonEncoderEscape.Escape(s);

        if (modo == ModoVista.Carpeta)
        {
            var items = new List<string>();
            int thumbIdx = 0;
            try
            {
                var entradas = new DirectoryInfo(ruta).EnumerateFileSystemInfos().Take(400).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var info in entradas)
                {
                    bool esCarpeta = info is DirectoryInfo;
                    string ext = esCarpeta ? "" : Path.GetExtension(info.Name).TrimStart('.').ToLowerInvariant();
                    string categoria = esCarpeta ? "carpeta" : Categoria(ext);
                    string mini = "";
                    if (!esCarpeta && Categoria(ext) == "imagen" && thumbIdx < 60)
                    {
                        lock (_candado) { _rutaMiniatura[thumbIdx] = info.FullName; }
                        mini = $"/thumb/{thumbIdx}";
                        thumbIdx++;
                    }
                    items.Add($"{{\"n\":{esc(info.Name)},\"cat\":\"{categoria}\",\"m\":{(esCarpeta ? "1" : "0")},\"t\":\"{mini}\"}}");
                }
            }
            catch { }
            return $"{{\"modo\":\"carpeta\",\"tema\":\"{tema}\",\"autogiro\":{(autogiro ? "true" : "false")},\"nombre\":{esc(nombre)},\"items\":[{string.Join(",", items)}]}}";
        }

        if (modo == ModoVista.Zip)
        {
            var filas = new List<string>();
            long totalDes = 0, totalCom = 0; int count = 0; int indiceReal = -1;
            try
            {
                using var zip = ZipFile.OpenRead(ruta);
                foreach (var ent in zip.Entries)
                {
                    indiceReal++;
                    if (count >= 1500) break;
                    if (ent.FullName.EndsWith("/")) continue;
                    totalDes += ent.Length; totalCom += ent.CompressedLength;
                    string extInterna = Path.GetExtension(ent.FullName).TrimStart('.').ToLowerInvariant();
                    filas.Add($"{{\"i\":{indiceReal},\"e\":{esc(extInterna)},\"n\":{esc(ent.FullName)},\"t\":{ent.Length},\"c\":{ent.CompressedLength}}}");
                    count++;
                }
                int omitidas = Math.Max(0, zip.Entries.Count - count);
                string mcJson = MinecraftPackageReader.LeerComoJson(ruta, tipo);
                return $"{{\"modo\":\"zip\",\"tema\":\"{tema}\",\"nombre\":{esc(nombre)},\"total\":{zip.Entries.Count},\"omitidas\":{omitidas},\"des\":{totalDes},\"com\":{totalCom},\"filas\":[{string.Join(",", filas)}]{mcJson}}}";
            }
            catch (Exception ex)
            {
                return $"{{\"modo\":\"zip\",\"tema\":\"{tema}\",\"nombre\":{esc(nombre)},\"error\":{esc(ex.Message)}}}";
            }
        }

        if (modo == ModoVista.ImagenExtendida)
        {
            if (tipo == "ai" && ImagenesAvanzadas.EsPdfCompatible(ruta))
                return $"{{\"modo\":\"pdf\",\"tema\":\"{tema}\",\"nombre\":{esc(nombre)},\"ext\":\"ai\"}}";
            var imagen = ImagenesAvanzadas.Convertir(ruta);
            string src = "/image-cache/" + Path.GetFileName(imagen.RutaPng);
            return $"{{\"modo\":\"imagenweb\",\"tema\":\"{tema}\",\"nombre\":{esc(nombre)},\"ext\":\"{tipo}\",\"src\":\"{src}\",\"ancho\":{imagen.AnchoOriginal},\"alto\":{imagen.AltoOriginal},\"fotogramas\":{imagen.Fotogramas},\"formatoOriginal\":{esc(imagen.FormatoOriginal)}}}";
        }

        if (modo == ModoVista.Tarjeta)
        {
            var fi = new FileInfo(ruta);
            return $"{{\"modo\":\"tarjeta\",\"tema\":\"{tema}\",\"nombre\":{esc(nombre)},\"ext\":\"{tipo}\",\"tam\":{fi.Length},\"cat\":\"{Categoria(tipo)}\",\"modif\":\"{fi.LastWriteTime:yyyy-MM-dd HH:mm}\"}}";
        }

        // visores que solo necesitan saber qué archivo y qué tema
        // --- texto instantáneo por parsers propios (C#): sin esperar el render con formato ---
        if (modo == ModoVista.Docx)
        {
            var parrafos = new List<string>();
            try
            {
                using var zipDocx = ZipFile.OpenRead(ruta);
                var entrada = zipDocx.GetEntry("word/document.xml");
                if (entrada != null)
                {
                    using var sr = new StreamReader(entrada.Open(), Encoding.UTF8);
                    string xml = sr.ReadToEnd();
                    foreach (var m in System.Text.RegularExpressions.Regex.Matches(xml, "<w:p[ >].*?</w:p>", System.Text.RegularExpressions.RegexOptions.Singleline).Cast<System.Text.RegularExpressions.Match>())
                    {
                        var sb = new StringBuilder();
                        foreach (var t in System.Text.RegularExpressions.Regex.Matches(m.Value, "<w:t[^>]*>(.*?)</w:t>", System.Text.RegularExpressions.RegexOptions.Singleline).Cast<System.Text.RegularExpressions.Match>())
                            sb.Append(t.Groups[1].Value);
                        string texto = sb.ToString().Trim();
                        if (texto.Length > 0 && parrafos.Count < 4000) parrafos.Add(esc(texto));
                    }
                }
            }
            catch { }
            if (parrafos.Count > 0)
                return $"{{\"modo\":\"docx\",\"tema\":\"{tema}\",\"nombre\":{esc(nombre)},\"ext\":\"{tipo}\",\"textoDocx\":[{string.Join(",", parrafos)}]}}";
        }

        if (modo == ModoVista.Presentacion)
        {
            var laminas = new List<string>();
            try
            {
                using var zipPptx = ZipFile.OpenRead(ruta);
                var slides = zipPptx.Entries
                    .Where(en => en.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) && en.FullName.EndsWith(".xml"))
                    .OrderBy(en => { var d = System.Text.RegularExpressions.Regex.Match(en.FullName, @"slide(\d+)\.xml$"); return int.TryParse(d.Groups[1].Value, out int n) ? n : 0; })
                    .Take(300)
                    .ToList();
                int numLamina = 0;
                foreach (var en in slides)
                {
                    numLamina++;
                    using var sr = new StreamReader(en.Open(), Encoding.UTF8);
                    string xml = sr.ReadToEnd();
                    var sb = new StringBuilder();
                    foreach (var t in System.Text.RegularExpressions.Regex.Matches(xml, "<a:t>(.*?)</a:t>", System.Text.RegularExpressions.RegexOptions.Singleline).Cast<System.Text.RegularExpressions.Match>())
                        sb.Append(t.Groups[1].Value).Append(' ');
                    string texto = sb.ToString().Trim();
                    if (texto.Length == 0) texto = "(lámina vacía)";
                    laminas.Add($"{{\"n\":{numLamina},\"t\":{esc(texto)}}}");
                }
            }
            catch { }
            if (laminas.Count > 0)
                return $"{{\"modo\":\"presentacion\",\"tema\":\"{tema}\",\"nombre\":{esc(nombre)},\"ext\":\"{tipo}\",\"laminas\":[{string.Join(",", laminas)}]}}";
        }

        string flagGlb = (tipo == "stl" || tipo == "obj" || tipo == "ply") ? ",\"glb\":1" : "";
        string datosMedia = modo == ModoVista.Media
            ? ",\"mediaId\":\"" + _media.IdPara(ruta) + "\",\"preparar\":" + (NecesitaPreparacion(tipo) ? "true" : "false")
            : "";
        return $"{{\"modo\":\"{modo.ToString().ToLowerInvariant()}\",\"tema\":\"{tema}\",\"autogiro\":{(autogiro ? "true" : "false")},\"nombre\":{esc(nombre)},\"ext\":\"{tipo}\"{flagGlb}{datosMedia}}}";
    }

    public static string Categoria(string ext)
    {
        ext = ext.TrimStart('.').ToLowerInvariant();
        if (ext.Length == 0) return "archivo";
        var (modo, icono) = PreviewRouter.Clasificar("a." + ext);
        return icono;
    }

    private static bool NecesitaPreparacion(string ext) => ext is
        "mkv" or "avi" or "wmv" or "flv" or "mpg" or "mpeg" or "m2v" or "ts" or "m2ts" or "mts" or "m2t" or "vob"
        or "3gp" or "3g2" or "divx" or "rm" or "rmvb" or "wma" or "aiff" or "aif" or "ape";

    private static string TipoArchivo(string ruta)
    {
        string ext = Path.GetExtension(ruta).ToLowerInvariant();
        switch (ext)
        {
            case ".png": return "image/png";
            case ".jpg": case ".jpeg": case ".jpe": return "image/jpeg";
            case ".gif": return "image/gif";
            case ".webp": return "image/webp";
            case ".svg": case ".svgz": return "image/svg+xml";
            case ".bmp": return "image/bmp";
            case ".ico": return "image/x-icon";
            case ".avif": return "image/avif";
            case ".jxl": return "image/jxl";
            case ".heic": return "image/heic";
            case ".heif": return "image/heif";
            case ".pdf": case ".ai": return "application/pdf";
            case ".html": case ".htm": case ".xhtml": return "text/html; charset=utf-8";
            case ".css": return "text/css; charset=utf-8";
            case ".js": case ".mjs": return "text/javascript; charset=utf-8";
            case ".json": return "application/json; charset=utf-8";
            case ".xml": return "application/xml; charset=utf-8";
            case ".txt": case ".log": case ".md": return "text/plain; charset=utf-8";
            case ".mp4": return "video/mp4";
            case ".webm": return "video/webm";
            case ".m4v": case ".mov": return "video/mp4";
            case ".mkv": return "video/x-matroska";
            case ".avi": return "video/x-msvideo";
            case ".wmv": return "video/x-ms-wmv";
            case ".flv": return "video/x-flv";
            case ".mpg": case ".mpeg": case ".m2v": return "video/mpeg";
            case ".ts": case ".m2ts": case ".mts": case ".m2t": return "video/mp2t";
            case ".3gp": return "video/3gpp";
            case ".3g2": return "video/3gpp2";
            case ".mp3": return "audio/mpeg";
            case ".wav": return "audio/wav";
            case ".ogg": case ".oga": case ".opus": return "audio/ogg";
            case ".flac": return "audio/flac";
            case ".m4a": return "audio/mp4";
            case ".aac": return "audio/aac";
            case ".wma": return "audio/x-ms-wma";
            case ".aiff": case ".aif": return "audio/aiff";
            default: return "application/octet-stream";
        }
    }

    private static string TipoPorExt(string ext) =>
        Tipos.TryGetValue(ext.ToLowerInvariant(), out var t) ? t : "application/octet-stream";

    private static void Responder(CoreWebView2WebResourceRequestedEventArgs e, byte[] contenido, string tipo, int codigo = 200)
        => Responder(e, contenido, tipo, codigo, null);

    private static void Responder(CoreWebView2WebResourceRequestedEventArgs e, byte[] contenido, string tipo, int codigo, List<(string, string)>? extra)
    {
        var env = CoreWebView2EnvironmentWrapper.Dame();
        string cabeceras = $"Content-Type: {tipo}\r\nAccess-Control-Allow-Origin: null\r\n";
        bool tieneCache = extra != null && extra.Any(x => x.Item1.Equals("Cache-Control", StringComparison.OrdinalIgnoreCase));
        if (!tieneCache) cabeceras += "Cache-Control: no-store\r\n";
        if (extra != null) foreach (var (k, v) in extra) cabeceras += k + ": " + v + "\r\n";
        e.Response = env.CreateWebResourceResponse(new MemoryStream(contenido), codigo, codigo == 200 ? "OK" : codigo == 404 ? "Not Found" : "Error",
            cabeceras);
    }

    private static CoreWebView2WebResourceResponse CrearRespuesta(CoreWebView2WebResourceRequestedEventArgs e, Stream contenido, string tipo, int codigo, List<(string, string)> cabeceras)
    {
        var env = CoreWebView2EnvironmentWrapper.Dame();
        var sb = new StringBuilder();
        sb.Append("Content-Type: ").Append(tipo).Append("\r\n");
        foreach (var (k, v) in cabeceras) sb.Append(k).Append(": ").Append(v).Append("\r\n");
        string razon = codigo == 206 ? "Partial Content" : codigo == 416 ? "Requested Range Not Satisfiable" : "OK";
        return env.CreateWebResourceResponse(contenido, codigo, razon, sb.ToString());
    }
}

/// <summary>Guarda el entorno WebView2 activo para crear respuestas.</summary>
public static class CoreWebView2EnvironmentWrapper
{
    public static CoreWebView2Environment? Actual;
    public static CoreWebView2Environment Dame() =>
        Actual ?? throw new InvalidOperationException("WebView2 no inicializado");
}

/// <summary>Escapador JSON mínimo para cadenas seguras.</summary>
internal static class JsonEncoderEscape
{
    public static string Escape(string s)
    {
        if (s == null) return "\"\"";
        var sb = new StringBuilder(s.Length + 8);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
