using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Vistia.Core.Minecraft;

/// <summary>Lectura segura de packs/mods/mundos, sin extraer ni ejecutar contenido.</summary>
public static class MinecraftPackageReader
{
    private const int MaxTexto = 1024 * 1024;

    public static string LeerComoJson(string ruta, string extension)
    {
        try
        {
            using var zip = ZipFile.OpenRead(ruta);
            string ext = extension.TrimStart('.').ToLowerInvariant();
            string kind = ext switch
            {
                "mcpack" => "bedrockPack",
                "mcaddon" => "bedrockAddon",
                "mcworld" or "mctemplate" => "bedrockWorld",
                "jar" => "javaMod",
                _ => ""
            };
            if (kind == "") return "";
            var mc = new Dictionary<string, object?> { ["kind"] = kind };
            var advertencias = new List<string>();
            var entradas = zip.Entries.ToList();
            var manifiestos = entradas.Where(e => e.FullName.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)).Take(24).ToList();
            if (manifiestos.Count > 0)
            {
                LeerManifest(ZipEntryBytes(manifiestos[0]), mc);
                if (kind == "bedrockAddon" && manifiestos.Count > 1)
                {
                    var packs = new List<Dictionary<string, object?>>();
                    foreach (var m in manifiestos)
                    {
                        var pack = new Dictionary<string, object?>();
                        LeerManifest(ZipEntryBytes(m), pack);
                        pack["path"] = m.FullName;
                        packs.Add(pack);
                    }
                    mc["packs"] = packs;
                }
            }
            else if (kind == "bedrockPack") advertencias.Add("No se encontró manifest.json en la raíz del paquete");

            var icono = entradas.FirstOrDefault(e => e.FullName.Equals("pack_icon.png", StringComparison.OrdinalIgnoreCase));
            if (icono != null) mc["iconIndex"] = entradas.IndexOf(icono);
            mc["iconExt"] = "png";

            var skins = new List<Dictionary<string, object?>>();
            var skinsJson = entradas.FirstOrDefault(e => e.FullName.Equals("skins.json", StringComparison.OrdinalIgnoreCase));
            if (skinsJson != null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(ZipEntryBytes(skinsJson));
                    if (doc.RootElement.TryGetProperty("skins", out var lista))
                    {
                        foreach (var s in lista.EnumerateArray().Take(128))
                        {
                            string textura = s.TryGetProperty("texture", out var tex) ? tex.GetString() ?? "" : "";
                            string nombre = s.TryGetProperty("localization_name", out var loc) ? loc.GetString() ?? textura : textura;
                            skins.Add(new Dictionary<string, object?> { ["name"] = nombre, ["texture"] = textura });
                        }
                    }
                }
                catch { advertencias.Add("skins.json no se pudo leer"); }
            }
            if (skins.Count > 0) mc["skins"] = skins;

            var imagenes = entradas.Where(e => e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                .Take(96).Select(e => new Dictionary<string, object?> { ["name"] = e.FullName, ["index"] = entradas.IndexOf(e), ["ext"] = Path.GetExtension(e.FullName).TrimStart('.').ToLowerInvariant() }).ToList();
            mc["images"] = imagenes;

            if (kind == "bedrockWorld") LeerMundo(zip, mc, advertencias);
            if (kind == "javaMod") LeerModJava(zip, mc, advertencias);
            if (advertencias.Count > 0) mc["warnings"] = advertencias;
            return ",\"mc\":" + JsonSerializer.Serialize(mc);
        }
        catch (Exception ex)
        {
            return ",\"mc\":" + JsonSerializer.Serialize(new { kind = "desconocido", warnings = new[] { ex.Message } });
        }
    }

    private static void LeerManifest(byte[] bytes, Dictionary<string, object?> mc)
    {
        if (bytes.Length == 0) return;
        using var doc = JsonDocument.Parse(bytes);
        var raiz = doc.RootElement;
        var header = raiz.TryGetProperty("header", out var h) ? h : raiz;
        if (header.TryGetProperty("name", out var nombre)) mc["name"] = nombre.GetString();
        if (header.TryGetProperty("description", out var descripcion)) mc["description"] = descripcion.GetString();
        if (header.TryGetProperty("uuid", out var uuid)) mc["uuid"] = uuid.GetString();
        if (header.TryGetProperty("version", out var versionManifest)) mc["version"] = TextoVersion(versionManifest);
        if (raiz.TryGetProperty("format_version", out var formato)) mc["formatVersion"] = formato.GetRawText();
        if (raiz.TryGetProperty("min_engine_version", out var motor)) mc["minEngineVersion"] = TextoVersion(motor);
        var modulos = new List<string>();
        if (raiz.TryGetProperty("modules", out var mods))
            foreach (var m in mods.EnumerateArray())
            {
                string tipo = m.TryGetProperty("type", out var t) ? t.GetString() ?? "módulo" : "módulo";
                string version = m.TryGetProperty("version", out var v) ? TextoVersion(v) : "";
                modulos.Add(tipo + (version.Length > 0 ? " " + version : ""));
            }
        if (modulos.Count > 0) mc["modules"] = modulos;
        var dependencias = new List<string>();
        if (raiz.TryGetProperty("dependencies", out var deps))
            foreach (var d in deps.EnumerateArray())
            {
                string id = d.TryGetProperty("uuid", out var u) ? u.GetString() ?? "" : "";
                string version = d.TryGetProperty("version", out var v) ? TextoVersion(v) : "";
                dependencias.Add((id + " " + version).Trim());
            }
        if (dependencias.Count > 0) mc["dependencies"] = dependencias;
    }

    private static string TextoVersion(JsonElement elemento)
    {
        if (elemento.ValueKind == JsonValueKind.Array) return string.Join('.', elemento.EnumerateArray().Select(v => v.GetRawText()));
        return elemento.GetRawText().Trim('"');
    }

    private static void LeerMundo(ZipArchive zip, Dictionary<string, object?> mc, List<string> advertencias)
    {
        var nombre = zip.Entries.FirstOrDefault(e => e.FullName.Equals("levelname.txt", StringComparison.OrdinalIgnoreCase));
        if (nombre != null)
        {
            string texto = Encoding.UTF8.GetString(ZipEntryBytes(nombre)).Trim();
            if (texto.Length > 0) mc["worldName"] = texto;
        }
        var icono = zip.Entries.FirstOrDefault(e => e.FullName.Equals("world_icon.jpeg", StringComparison.OrdinalIgnoreCase) || e.FullName.Equals("world_icon.png", StringComparison.OrdinalIgnoreCase));
        if (icono != null)
        {
            mc["iconIndex"] = icono.FullName;
            mc["iconExt"] = Path.GetExtension(icono.FullName).TrimStart('.').ToLowerInvariant();
        }
        var level = zip.Entries.FirstOrDefault(e => e.FullName.Equals("level.dat", StringComparison.OrdinalIgnoreCase));
        if (level != null)
        {
            try
            {
                var nbt = BedrockNbtReader.LeerLevelDat(ZipEntryBytes(level));
                foreach (var par in nbt)
                    if (par.Key is "LevelName" or "GameType" or "Difficulty" or "Time" or "SpawnX" or "SpawnY" or "SpawnZ" or "lastOpenedWithVersion")
                        mc[par.Key] = par.Value;
            }
            catch (Exception ex) { advertencias.Add("level.dat: " + ex.Message); }
        }
        LeerPacksMundo(zip, mc, "world_behavior_packs.json", "behaviorPacks");
        LeerPacksMundo(zip, mc, "world_resource_packs.json", "resourcePacks");
    }

    private static void LeerPacksMundo(ZipArchive zip, Dictionary<string, object?> mc, string archivo, string destino)
    {
        var entrada = zip.Entries.FirstOrDefault(e => e.FullName.Equals(archivo, StringComparison.OrdinalIgnoreCase));
        if (entrada == null) return;
        try
        {
            using var doc = JsonDocument.Parse(ZipEntryBytes(entrada));
            var packs = new List<Dictionary<string, object?>>();
            foreach (var p in doc.RootElement.EnumerateArray().Take(32))
                packs.Add(new Dictionary<string, object?>
                {
                    ["uuid"] = p.TryGetProperty("pack_id", out var id) ? id.GetString() : "",
                    ["version"] = p.TryGetProperty("version", out var v) ? TextoVersion(v) : ""
                });
            mc[destino] = packs;
        }
        catch { }
    }

    private static void LeerModJava(ZipArchive zip, Dictionary<string, object?> mc, List<string> advertencias)
    {
        var entrada = zip.Entries.FirstOrDefault(e => e.FullName.Equals("fabric.mod.json", StringComparison.OrdinalIgnoreCase) || e.FullName.Equals("quilt.mod.json", StringComparison.OrdinalIgnoreCase));
        if (entrada != null)
        {
            using var doc = JsonDocument.Parse(ZipEntryBytes(entrada));
            var r = doc.RootElement;
            if (r.TryGetProperty("name", out var n)) mc["name"] = n.GetString();
            if (r.TryGetProperty("id", out var id)) mc["modId"] = id.GetString();
            if (r.TryGetProperty("version", out var v)) mc["version"] = v.GetString();
            if (r.TryGetProperty("description", out var d)) mc["description"] = d.GetString();
            mc["loader"] = entrada.FullName.StartsWith("fabric", StringComparison.OrdinalIgnoreCase) ? "Fabric" : "Quilt";
            return;
        }
        var toml = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("mods.toml", StringComparison.OrdinalIgnoreCase));
        if (toml != null)
        {
            string texto = Encoding.UTF8.GetString(ZipEntryBytes(toml));
            mc["loader"] = texto.Contains("neoforge", StringComparison.OrdinalIgnoreCase) ? "NeoForge" : "Forge";
            mc["modId"] = CampoToml(texto, "modId");
            mc["name"] = CampoToml(texto, "displayName");
            mc["version"] = CampoToml(texto, "version");
            mc["description"] = CampoToml(texto, "description");
            return;
        }
        var info = zip.Entries.FirstOrDefault(e => e.FullName.Equals("mcmod.info", StringComparison.OrdinalIgnoreCase));
        if (info != null)
        {
            using var doc = JsonDocument.Parse(ZipEntryBytes(info));
            var r = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0 ? doc.RootElement[0] : doc.RootElement;
            if (r.TryGetProperty("name", out var n)) mc["name"] = n.GetString();
            if (r.TryGetProperty("modid", out var id)) mc["modId"] = id.GetString();
            if (r.TryGetProperty("version", out var v)) mc["version"] = v.GetString();
            if (r.TryGetProperty("description", out var d)) mc["description"] = d.GetString();
            mc["loader"] = "Forge";
        }
        else advertencias.Add("No se detectó manifiesto de mod Java; se muestra el contenido JAR");
    }

    private static string CampoToml(string texto, string campo)
    {
        var m = System.Text.RegularExpressions.Regex.Match(texto, "(?m)^\\s*" + System.Text.RegularExpressions.Regex.Escape(campo) + "\\s*=\\s*[\"']([^\"']*)[\"']");
        return m.Success ? m.Groups[1].Value : "";
    }

    private static byte[] ZipEntryBytes(ZipArchiveEntry entrada)
    {
        if (entrada.Length > MaxTexto && !entrada.FullName.Equals("level.dat", StringComparison.OrdinalIgnoreCase)) return Array.Empty<byte>();
        if (entrada.Length > 4 * 1024 * 1024) return Array.Empty<byte>();
        using var flujo = entrada.Open();
        using var memoria = new MemoryStream();
        flujo.CopyTo(memoria);
        return memoria.ToArray();
    }
}
