using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vistia.Core.Minecraft;

/// <summary>Lector NBT Bedrock little-endian, limitado a metadatos pequeños.</summary>
public sealed class BedrockNbtReader
{
    private readonly byte[] _data;
    private int _pos;
    private int _entradas;

    private BedrockNbtReader(byte[] data) { _data = data; }

    public static Dictionary<string, object?> LeerLevelDat(byte[] data)
    {
        var salida = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (data == null || data.Length < 10 || data.Length > 4 * 1024 * 1024) return salida;
        try
        {
            var lector = new BedrockNbtReader(data);
            lector._pos = 8; // versión y longitud del encabezado Bedrock
            byte tipo = lector.Byte();
            if (tipo != 10) return salida;
            lector.Texto(); // nombre de raíz
            lector.Compuesto(salida, 0);
        }
        catch { }
        return salida;
    }

    private void Compuesto(Dictionary<string, object?> destino, int profundidad)
    {
        if (profundidad > 24) throw new InvalidDataException("NBT demasiado profundo");
        while (_pos < _data.Length)
        {
            byte tipo = Byte();
            if (tipo == 0) return;
            string nombre = Texto();
            if (++_entradas > 5000) throw new InvalidDataException("NBT demasiado grande");
            object? valor = Valor(tipo, nombre, destino, profundidad);
            if (valor != null && !destino.ContainsKey(nombre)) destino[nombre] = valor;
        }
    }

    private object? Valor(byte tipo, string nombre, Dictionary<string, object?> destino, int profundidad)
    {
        switch (tipo)
        {
            case 1: return (sbyte)Byte();
            case 2: return (short)U16();
            case 3: return (int)U32();
            case 4: return (long)U64();
            case 5: return BitConverter.ToSingle(Bytes(4), 0);
            case 6: return BitConverter.ToDouble(Bytes(8), 0);
            case 7: return Bytes((int)U32());
            case 8: return Texto();
            case 9:
                byte interno = Byte(); int cantidad = (int)U32();
                if (cantidad < 0 || cantidad > 2048) throw new InvalidDataException("Lista NBT excesiva");
                if (interno == 0) return null;
                var simple = new List<object?>();
                for (int i = 0; i < cantidad; i++)
                {
                    if (interno == 3) simple.Add((int)U32());
                    else if (interno == 2) simple.Add((short)U16());
                    else if (interno == 4) simple.Add((long)U64());
                    else if (interno == 8) simple.Add(Texto());
                    else { Saltar(interno, 0); }
                }
                return simple;
            case 10:
                var anidado = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                Compuesto(anidado, profundidad + 1);
                return anidado;
            case 11: return Bytes((int)U32() * 4);
            case 12: return Bytes((int)U32() * 8);
            default: throw new InvalidDataException("Tipo NBT desconocido");
        }
    }

    private void Saltar(byte tipo, int profundidad)
    {
        if (profundidad > 24) throw new InvalidDataException("NBT demasiado profundo");
        switch (tipo)
        {
            case 1: _pos++; break;
            case 2: _pos += 2; break;
            case 3: case 5: _pos += 4; break;
            case 4: case 6: _pos += 8; break;
            case 7: _pos += (int)U32(); break;
            case 8: Texto(); break;
            case 9:
                byte interno = Byte(); int cantidad = (int)U32();
                for (int i = 0; i < cantidad; i++) Saltar(interno, profundidad + 1);
                break;
            case 10:
                while (true)
                {
                    byte t = Byte(); if (t == 0) break;
                    Texto(); Saltar(t, profundidad + 1);
                }
                break;
            case 11: _pos += (int)U32() * 4; break;
            case 12: _pos += (int)U32() * 8; break;
            default: throw new InvalidDataException("Tipo NBT desconocido");
        }
        if (_pos > _data.Length) throw new EndOfStreamException();
    }

    private byte Byte()
    {
        if (_pos >= _data.Length) throw new EndOfStreamException();
        return _data[_pos++];
    }

    private ushort U16()
    {
        var b = Bytes(2);
        return (ushort)(b[0] | b[1] << 8);
    }

    private uint U32()
    {
        var b = Bytes(4);
        return (uint)(b[0] | b[1] << 8 | b[2] << 16 | b[3] << 24);
    }

    private ulong U64()
    {
        var b = Bytes(8);
        ulong valor = 0;
        for (int i = 0; i < 8; i++) valor |= (ulong)b[i] << (8 * i);
        return valor;
    }

    private string Texto()
    {
        int n = U16();
        if (n > 65535 || _pos + n > _data.Length) throw new EndOfStreamException();
        string s = Encoding.UTF8.GetString(_data, _pos, n);
        _pos += n;
        return s;
    }

    private byte[] Bytes(int cantidad)
    {
        if (cantidad < 0 || _pos + cantidad > _data.Length) throw new EndOfStreamException();
        var b = new byte[cantidad];
        Buffer.BlockCopy(_data, _pos, b, 0, cantidad);
        _pos += cantidad;
        return b;
    }
}
