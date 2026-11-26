using System;
using System.Globalization;

namespace Vistia.Core;

/// <summary>Un único rango bytes válido, incluidos sufijos y errores 416.</summary>
public readonly record struct RangoHttp(bool Valido, long Desde, long Hasta)
{
    public static bool Intentar(string cabecera, long total, out RangoHttp rango)
    {
        rango = default;
        if (string.IsNullOrWhiteSpace(cabecera) || total <= 0 || !cabecera.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
        string[] partes = cabecera.Substring(6).Split('-');
        if (partes.Length != 2 || partes[0].Contains(',')) return false;
        string inicioTexto = partes[0].Trim(), finTexto = partes[1].Trim();
        if (inicioTexto.Length == 0 && finTexto.Length == 0) return false;
        long desde = 0, hasta = total - 1;
        if (inicioTexto.Length == 0)
        {
            if (!long.TryParse(finTexto, NumberStyles.None, CultureInfo.InvariantCulture, out long sufijo) || sufijo <= 0) return false;
            desde = Math.Max(0, total - sufijo);
        }
        else
        {
            if (!long.TryParse(inicioTexto, NumberStyles.None, CultureInfo.InvariantCulture, out desde) || desde < 0) return false;
            if (finTexto.Length > 0 && (!long.TryParse(finTexto, NumberStyles.None, CultureInfo.InvariantCulture, out hasta) || hasta < 0)) return false;
        }
        if (desde >= total || hasta < desde) return false;
        rango = new RangoHttp(true, desde, Math.Min(hasta, total - 1));
        return true;
    }
}
