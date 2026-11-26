using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vistia.Core;

/// <summary>
/// Parsers 3D PROPIOS en C# (nativos, sin librerías de terceros en el parseo):
/// STL binario/ASCII, OBJ y PLY se convierten a GLB mínimo en memoria para que el
/// renderizador solo dibuje. Si algo falla devuelve null y se sirve el archivo original.
/// </summary>
public static class Convertidor3d
{
    public static byte[]? Convertir(string ruta, string ext)
    {
        try
        {
            switch (ext)
            {
                case "stl": return DeStl(ruta);
                case "obj": return DeObj(ruta);
                case "ply": return DePly(ruta);
            }
        }
        catch { }
        return null;
    }

    // ---------- STL ----------

    private static byte[]? DeStl(string ruta)
    {
        var datos = File.ReadAllBytes(ruta);
        if (datos.Length < 84) return null;
        int n = BitConverter.ToInt32(datos, 80);
        var pos = new List<float>(Math.Min(n, 2_000_000) * 9);
        var nor = new List<float>(Math.Min(n, 2_000_000) * 9);

        if (84 + (long)n * 50 <= datos.Length && n > 0)
        {
            // STL binario
            for (int i = 0; i < n; i++)
            {
                int o = 84 + i * 50;
                float nx = BitConverter.ToSingle(datos, o);
                float ny = BitConverter.ToSingle(datos, o + 4);
                float nz = BitConverter.ToSingle(datos, o + 8);
                float ax = BitConverter.ToSingle(datos, o + 12);
                float ay = BitConverter.ToSingle(datos, o + 16);
                float az = BitConverter.ToSingle(datos, o + 20);
                float bx = BitConverter.ToSingle(datos, o + 24);
                float by = BitConverter.ToSingle(datos, o + 28);
                float bz = BitConverter.ToSingle(datos, o + 32);
                float cx = BitConverter.ToSingle(datos, o + 36);
                float cy = BitConverter.ToSingle(datos, o + 40);
                float cz = BitConverter.ToSingle(datos, o + 44);
                AgregarTriangulo(pos, nor, ax, ay, az, bx, by, bz, cx, cy, cz, nx, ny, nz);
            }
        }
        else
        {
            // STL ASCII: vértices tras la palabra vertex
            string texto = Encoding.ASCII.GetString(datos);
            var posArchivo = new List<float>();
            int desde = 0;
            while (true)
            {
                int p = texto.IndexOf("vertex", desde, StringComparison.Ordinal);
                if (p < 0) break;
                int fin = Math.Min(texto.Length, p + 200);
                string trozo = texto.Substring(p, fin - p);
                var partes = trozo.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length >= 4 &&
                    float.TryParse(partes[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float vx) &&
                    float.TryParse(partes[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float vy) &&
                    float.TryParse(partes[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float vz))
                {
                    posArchivo.Add(vx); posArchivo.Add(vy); posArchivo.Add(vz);
                }
                desde = p + 6;
            }
            for (int i = 0; i + 8 < posArchivo.Count; i += 9)
                AgregarTriangulo(pos, nor,
                    posArchivo[i], posArchivo[i + 1], posArchivo[i + 2],
                    posArchivo[i + 3], posArchivo[i + 4], posArchivo[i + 5],
                    posArchivo[i + 6], posArchivo[i + 7], posArchivo[i + 8], 0, 0, 0);
        }

        if (pos.Count == 0) return null;
        return Glb(pos, nor);
    }

    // ---------- OBJ ----------

    private static byte[]? DeObj(string ruta)
    {
        var posIdx = new List<float>();
        var caras = new List<int>();
        foreach (var linea in File.ReadLines(ruta))
        {
            if (linea.StartsWith("v ", StringComparison.Ordinal))
            {
                var p = linea.Substring(2).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 3 &&
                    float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                {
                    posIdx.Add(x); posIdx.Add(y); posIdx.Add(z);
                }
            }
            else if (linea.StartsWith("f ", StringComparison.Ordinal))
            {
                var p = linea.Substring(2).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var idx = new List<int>();
                foreach (var grupo in p)
                {
                    int barra = grupo.IndexOf('/');
                    string num = barra >= 0 ? grupo.Substring(0, barra) : grupo;
                    if (int.TryParse(num, out int v)) idx.Add(v); // 1-based
                }
                for (int k = 2; k < idx.Count; k++) // triangulacion en abanico (quads y mas)
                {
                    caras.Add(idx[0]); caras.Add(idx[k - 1]); caras.Add(idx[k]);
                }
            }
        }
        if (posIdx.Count < 3 || caras.Count == 0) return null;

        var pos = new List<float>(caras.Count * 3);
        var nor = new List<float>(caras.Count * 3);
        for (int k = 0; k + 2 < caras.Count; k += 3)
        {
            int a = (caras[k] - 1) * 3, b = (caras[k + 1] - 1) * 3, c = (caras[k + 2] - 1) * 3;
            if (a < 0 || b < 0 || c < 0 || a + 2 >= posIdx.Count || b + 2 >= posIdx.Count || c + 2 >= posIdx.Count) continue;
            AgregarTriangulo(pos, nor,
                posIdx[a], posIdx[a + 1], posIdx[a + 2],
                posIdx[b], posIdx[b + 1], posIdx[b + 2],
                posIdx[c], posIdx[c + 1], posIdx[c + 2], 0, 0, 0);
        }
        if (pos.Count == 0) return null;
        return Glb(pos, nor);
    }

    // ---------- PLY ----------

    private static byte[]? DePly(string ruta)
    {
        var datos = File.ReadAllBytes(ruta);
        string cabeza = Encoding.ASCII.GetString(datos, 0, Math.Min(datos.Length, 4096));
        int fin = cabeza.IndexOf("end_header", StringComparison.Ordinal);
        if (fin < 0) return null;
        int inicioDatos = fin + "end_header".Length;
        while (inicioDatos < datos.Length && (datos[inicioDatos] == (byte)'\n' || datos[inicioDatos] == (byte)'\r')) inicioDatos++;

        bool binario = cabeza.Contains("binary_little_endian");
        int nVertices = 0, nCaras = 0;
        var propsVertice = new List<(string nombre, int tam)>();
        bool leyendoVertices = true;
        int tamListaCaras = 4, tipoIndiceCaras = 4;

        foreach (var lineaCruda in cabeza.Substring(0, fin).Split('\n'))
        {
            var p = lineaCruda.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            if (p[0] == "format") binario = p[1] == "binary_little_endian";
            else if (p[0] == "element")
            {
                if (p[1] == "vertex") { nVertices = int.TryParse(p[2], out int v) ? v : 0; leyendoVertices = true; }
                else if (p[1] == "face") { nCaras = int.TryParse(p[2], out int v) ? v : 0; leyendoVertices = false; }
            }
            else if (p[0] == "property")
            {
                if (leyendoVertices && p.Length >= 3)
                {
                    int tam = p[1] switch
                    {
                        "float" or "float32" => 4,
                        "double" or "float64" => 8,
                        "uchar" or "uint8" or "char" or "int8" => 1,
                        "ushort" or "uint16" or "short" or "int16" => 2,
                        "int" or "uint" or "int32" or "uint32" => 4,
                        _ => 4
                    };
                    propsVertice.Add((p[2], tam));
                }
                else if (!leyendoVertices && p.Length >= 5 && p[1] == "list")
                {
                    tipoIndiceCaras = p[3] switch
                    {
                        "int8" or "uchar" or "uint8" => 1,
                        "int16" or "uint16" => 2,
                        _ => 4
                    };
                    if (p[4] == "int8" || p[4] == "uchar" || p[4] == "uint8") tamListaCaras = 1;
                }
            }
        }

        int offsetM = -1, offsetMy = -1, offsetMz = -1, stride = 0;
        for (int i = 0; i < propsVertice.Count; i++)
        {
            if (propsVertice[i].nombre == "x") offsetM = stride;
            if (propsVertice[i].nombre == "y") offsetMy = stride;
            if (propsVertice[i].nombre == "z") offsetMz = stride;
            stride += propsVertice[i].tam;
        }
        if (offsetM < 0 || offsetMy < 0 || offsetMz < 0) return null;

        var pos = new List<float>(Math.Min(nVertices, 2_000_000) * 3);
        var nor = new List<float>();
        int cursor = inicioDatos;
        float LeeFloat(byte[] b, int o)
        {
            if (o + 4 > b.Length) return 0f;
            return BitConverter.ToSingle(b, o);
        }

        if (binario)
        {
            bool xFlota = propsVertice[offsetMx(propsVertice)].tam == 4;
            var posV = new List<float>(Math.Min(nVertices, 2_000_000) * 3);
            for (int i = 0; i < nVertices && cursor + stride <= datos.Length; i++)
            {
                float x = xFlota ? LeeFloat(datos, cursor + offsetM) : (float)BitConverter.Int64BitsToDouble(BitConverter.ToInt64(datos, cursor + offsetM));
                float y = xFlota ? LeeFloat(datos, cursor + offsetMy) : (float)BitConverter.Int64BitsToDouble(BitConverter.ToInt64(datos, cursor + offsetMy));
                float z = xFlota ? LeeFloat(datos, cursor + offsetMz) : (float)BitConverter.Int64BitsToDouble(BitConverter.ToInt64(datos, cursor + offsetMz));
                posV.Add(x); posV.Add(y); posV.Add(z);
                cursor += stride;
            }
            for (int i = 0; i < nCaras && cursor < datos.Length; i++)
            {
                int m = tipoIndiceCaras switch
                {
                    1 => (int)datos[cursor],
                    2 => BitConverter.ToUInt16(datos, cursor),
                    _ => (int)BitConverter.ToUInt32(datos, cursor)
                };
                cursor += tamListaCaras;
                var idx = new List<int>();
                for (int k = 0; k < m && cursor + 4 <= datos.Length; k++)
                {
                    idx.Add((int)BitConverter.ToUInt32(datos, cursor));
                    cursor += 4;
                }
                for (int k = 2; k < idx.Count; k++)
                {
                    int a = (idx[0] - 1) * 3, b = (idx[k - 1] - 1) * 3, c = (idx[k] - 1) * 3;
                    if (a < 0 || b < 0 || c < 0 || a + 2 >= posV.Count || b + 2 >= posV.Count || c + 2 >= posV.Count) continue;
                    AgregarTriangulo(pos, nor,
                        posV[a], posV[a + 1], posV[a + 2],
                        posV[b], posV[b + 1], posV[b + 2],
                        posV[c], posV[c + 1], posV[c + 2], 0, 0, 0);
                }
            }
        }
        else
        {
            string cuerpo = Encoding.ASCII.GetString(datos, inicioDatos, datos.Length - inicioDatos);
            var tokens = cuerpo.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int t = 0;
            for (int i = 0; i < nVertices && t + 2 < tokens.Length; i++)
            {
                float.TryParse(tokens[t + (offsetM / Math.Max(1, stride / Math.Max(1, propsVertice.Count)))], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x);
                // ascii: los vértices vienen por propiedades en orden; x/y/z son las primeras 3 en el caso común
                float.TryParse(tokens[t + 0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x);
                float.TryParse(tokens[t + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y);
                float.TryParse(tokens[t + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z);
                pos.Add(x); pos.Add(y); pos.Add(z);
                t += propsVertice.Count;
            }
            for (int i = 0; i < nCaras && t < tokens.Length; i++)
            {
                int m = int.TryParse(tokens[t], out int mm) ? mm : 0;
                t++;
                var idx = new List<int>();
                for (int k = 0; k < m && t < tokens.Length; k++)
                {
                    if (int.TryParse(tokens[t], out int iv)) idx.Add(iv);
                    t++;
                }
                for (int k = 2; k < idx.Count; k++)
                {
                    int a = (idx[0] - 1) * 3, b = (idx[k - 1] - 1) * 3, c = (idx[k] - 1) * 3;
                    if (a < 0 || b < 0 || c < 0 || a + 2 >= pos.Count || b + 2 >= pos.Count || c + 2 >= pos.Count) continue;
                    AgregarTriangulo(pos, nor,
                        pos[a], pos[a + 1], pos[a + 2],
                        pos[b], pos[b + 1], pos[b + 2],
                        pos[c], pos[c + 1], pos[c + 2], 0, 0, 0);
                }
            }
        }

        if (pos.Count == 0) return null;
        return Glb(pos, nor);
    }

    private static int offsetMx(List<(string nombre, int tam)> props)
    {
        for (int i = 0; i < props.Count; i++)
            if (props[i].nombre == "x") return i;
        return 0;
    }

    private static void AgregarTriangulo(List<float> pos, List<float> nor,
        float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz,
        float nx, float ny, float nz)
    {
        if (nx == 0 && ny == 0 && nz == 0)
        {
            float ux = bx - ax, uy = by - ay, uz = bz - az;
            float vx = cx - ax, vy = cy - ay, vz = cz - az;
            nx = uy * vz - uz * vy; ny = uz * vx - ux * vz; nz = ux * vy - uy * vx;
        }
        pos.AddRange(new[] { ax, ay, az, bx, by, bz, cx, cy, cz });
        nor.AddRange(new[] { nx, ny, nz, nx, ny, nz, nx, ny, nz });
    }

    // ---------- GLB ----------

    private static byte[] Glb(List<float> pos, List<float> nor)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i + 2 < pos.Count; i += 3)
        {
            if (pos[i] < minX) minX = pos[i];
            if (pos[i] > maxX) maxX = pos[i];
            if (pos[i + 1] < minY) minY = pos[i + 1];
            if (pos[i + 1] > maxY) maxY = pos[i + 1];
            if (pos[i + 2] < minZ) minZ = pos[i + 2];
            if (pos[i + 2] > maxZ) maxZ = pos[i + 2];
        }

        var norBytes = new byte[nor.Count * 4];
        Buffer.BlockCopy(nor.ToArray(), 0, norBytes, 0, norBytes.Length);
        int offPos = (norBytes.Length + 3) / 4 * 4;
        var posBytes = new byte[pos.Count * 4];
        Buffer.BlockCopy(pos.ToArray(), 0, posBytes, 0, posBytes.Length);
        int largoBin = offPos + posBytes.Length;

        string json =
            "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}]," +
            "\"nodes\":[{\"mesh\":0}]," +
            "\"meshes\":[{\"primitives\":[{\"attributes\":{\"NORMAL\":0,\"POSITION\":1},\"material\":0,\"mode\":4}]}]," +
            "\"materials\":[{\"pbrMetallicRoughness\":{\"baseColorFactor\":[0.79,0.72,0.64,1],\"metallicFactor\":0.05,\"roughnessFactor\":0.55}}]," +
            "\"buffers\":[{\"byteLength\":" + largoBin + "}]," +
            "\"bufferViews\":[" +
            "{\"buffer\":0,\"byteOffset\":0,\"byteLength\":" + norBytes.Length + "}," +
            "{\"buffer\":0,\"byteOffset\":" + offPos + ",\"byteLength\":" + posBytes.Length + "}]," +
            "\"accessors\":[" +
            "{\"bufferView\":0,\"componentType\":5126,\"count\":" + (nor.Count / 3) + ",\"type\":\"VEC3\"}," +
            "{\"bufferView\":1,\"componentType\":5126,\"count\":" + (pos.Count / 3) + ",\"type\":\"VEC3\"," +
            "\"min\":[" + Min(minX) + "," + Min(minY) + "," + Min(minZ) + "]," +
            "\"max\":[" + Min(maxX) + "," + Min(maxY) + "," + Min(maxZ) + "]}]}";

        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        int padJson = (4 - jsonBytes.Length % 4) % 4;
        int padBin = (4 - largoBin % 4) % 4;

        using var salida = new MemoryStream();
        int total = 12 + 8 + jsonBytes.Length + padJson + 8 + largoBin + padBin;
        salida.Write(BitConverter.GetBytes(0x46546C67)); // "glTF"
        salida.Write(BitConverter.GetBytes(2));
        salida.Write(BitConverter.GetBytes(total));
        salida.Write(BitConverter.GetBytes(jsonBytes.Length + padJson));
        salida.Write(BitConverter.GetBytes(0x4E4F534A)); // "JSON"
        salida.Write(jsonBytes);
        // el spec GLB exige relleno de ESPACIOS (0x20) en el chunk JSON; los bytes
        // nulos rompen JSON.parse de GLTFLoader cuando el JSON no cae en multiplo de 4
        var relleno = new byte[padJson];
        for (int i = 0; i < padJson; i++) relleno[i] = 0x20;
        salida.Write(relleno);
        salida.Write(BitConverter.GetBytes(largoBin + padBin));
        salida.Write(BitConverter.GetBytes(0x004E4942)); // "BIN\0"
        salida.Write(norBytes);
        salida.Write(posBytes);
        salida.Write(new byte[padBin]);
        return salida.ToArray();
    }

    private static string Min(float v) => v.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
}
