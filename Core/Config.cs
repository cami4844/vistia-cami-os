using System;
using System.IO;
using System.Text.Json;

namespace Vistia.Core;

/// <summary>Configuración simple en %APPDATA%\Vistia\config.json</summary>
public sealed class Config
{
    public string tema { get; set; } = "claro";
    public bool autoinicio { get; set; } = true;
    public bool pausado { get; set; } = false;
    public bool autogiro { get; set; } = true;

    private static string Carpeta
    {
        get
        {
            string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vistia");
            Directory.CreateDirectory(p);
            return p;
        }
    }

    private static string Ruta => Path.Combine(Carpeta, "config.json");

    public static Config Cargar()
    {
        try
        {
            if (File.Exists(Ruta))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(Ruta)) ?? new Config();
        }
        catch { }
        return new Config();
    }

    public void Guardar()
    {
        try
        {
            File.WriteAllText(Ruta, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
