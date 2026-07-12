using System.IO.Compression;
using System.Text;
using Jalium.UI.Ink.Shaders;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Ink;

internal static class InkStrokeSerializer
{
    private static readonly byte[] s_magic = Encoding.ASCII.GetBytes("JALINK01");
    private static readonly byte[] s_emptyWpfInk = Convert.FromBase64String("AAYCAA8AHwA=");
    private const int CurrentVersion = 2;
    private const int MaximumItemCount = 10_000_000;

    internal static void Save(StrokeCollection strokes, Stream stream, bool compress)
    {
        if (strokes.Count == 0 && strokes.PropertyData.Count == 0)
        {
            stream.Write(s_emptyWpfInk);
            return;
        }

        stream.Write(s_magic);
        stream.WriteByte(compress ? (byte)1 : (byte)0);

        if (compress)
        {
            using var compressed = new DeflateStream(stream, CompressionLevel.Optimal, leaveOpen: true);
            WritePayload(strokes, compressed);
        }
        else
        {
            WritePayload(strokes, stream);
        }
    }

    internal static void Load(StrokeCollection destination, Stream source)
    {
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        if (bytes.AsSpan().SequenceEqual(s_emptyWpfInk))
            return;
        if (bytes.Length <= s_magic.Length ||
            !bytes.AsSpan(0, s_magic.Length).SequenceEqual(s_magic))
        {
            throw new ArgumentException("The stream does not contain supported ink data.", nameof(source));
        }

        byte compressionFlag = bytes[s_magic.Length];
        if (compressionFlag > 1)
            throw new ArgumentException("The ink compression flag is invalid.", nameof(source));

        using var payload = new MemoryStream(
            bytes,
            s_magic.Length + 1,
            bytes.Length - s_magic.Length - 1,
            writable: false);
        try
        {
            if (compressionFlag == 1)
            {
                using var decompressed = new DeflateStream(payload, CompressionMode.Decompress);
                ReadPayload(destination, decompressed);
            }
            else
            {
                ReadPayload(destination, payload);
            }
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or InvalidDataException or IOException or OverflowException)
        {
            throw new ArgumentException("The ink stream is corrupt or incomplete.", nameof(source), exception);
        }
    }

    private static void WritePayload(StrokeCollection strokes, Stream payload)
    {
        using var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true);
        writer.Write(CurrentVersion);
        WritePropertyData(writer, strokes.PropertyData);
        writer.Write(strokes.Count);
        foreach (Stroke stroke in strokes)
            WriteStroke(writer, stroke);
    }

    private static void ReadPayload(StrokeCollection destination, Stream payload)
    {
        using var reader = new BinaryReader(payload, Encoding.UTF8, leaveOpen: true);
        int version = reader.ReadInt32();
        if (version != CurrentVersion)
            throw new InvalidDataException($"Unsupported ink stream version {version}.");

        foreach ((Guid id, object value) in ReadPropertyData(reader))
            destination.LoadPropertyData(id, value);

        int strokeCount = ReadCount(reader, "stroke");
        for (int i = 0; i < strokeCount; i++)
            destination.AddLoadedStroke(ReadStroke(reader));

        if (payload.CanSeek && payload.Position != payload.Length)
            throw new InvalidDataException("Unexpected trailing ink data.");
    }

    private static void WriteStroke(BinaryWriter writer, Stroke stroke)
    {
        StylusPointCollection points = stroke.StylusPoints;
        WriteStylusPointDescription(writer, points.Description);
        writer.Write(points.Count);
        foreach (StylusPoint point in points)
        {
            writer.Write(point.X);
            writer.Write(point.Y);
            writer.Write(point.PressureFactor);
            int[] additionalValues = point.GetUnpackedAdditionalValues(points.Description);
            writer.Write(additionalValues.Length);
            foreach (int value in additionalValues)
                writer.Write(value);
        }

        DrawingAttributes attributes = stroke.DrawingAttributes;
        writer.Write(attributes.Color.A);
        writer.Write(attributes.Color.R);
        writer.Write(attributes.Color.G);
        writer.Write(attributes.Color.B);
        writer.Write(attributes.Width);
        writer.Write(attributes.Height);
        writer.Write((int)attributes.StylusTip);
        WriteMatrix(writer, attributes.StylusTipTransform);
        writer.Write(attributes.IsHighlighter);
        writer.Write(attributes.FitToCurve);
        writer.Write(attributes.IgnorePressure);
        writer.Write((int)attributes.BrushType);
        WriteBrushShader(writer, attributes.BrushShader);
        writer.Write((int)stroke.TaperMode);

        WritePropertyData(
            writer,
            attributes.PropertyData.Where(static pair => !IsStandardAttributeId(pair.Key)));
        WritePropertyData(writer, stroke.PropertyData);
    }

    private static Stroke ReadStroke(BinaryReader reader)
    {
        StylusPointDescription description = ReadStylusPointDescription(reader);
        int pointCount = ReadCount(reader, "stylus point");
        if (pointCount == 0)
            throw new InvalidDataException("A persisted stroke cannot be empty.");

        var points = new StylusPointCollection(description, pointCount);
        for (int i = 0; i < pointCount; i++)
        {
            double x = reader.ReadDouble();
            double y = reader.ReadDouble();
            float pressure = reader.ReadSingle();
            if (!double.IsFinite(x) || !double.IsFinite(y) || !float.IsFinite(pressure))
                throw new InvalidDataException("A stylus point contains a non-finite value.");
            int additionalCount = ReadCount(reader, "additional stylus value");
            if (additionalCount != description.PropertyCount - StylusPointDescription.RequiredPropertyCount)
                throw new InvalidDataException("Stylus additional data does not match its description.");
            var additionalValues = new int[additionalCount];
            for (int valueIndex = 0; valueIndex < additionalCount; valueIndex++)
                additionalValues[valueIndex] = reader.ReadInt32();
            points.Add(new StylusPoint(x, y, pressure, description, additionalValues));
        }

        var attributes = new DrawingAttributes
        {
            Color = Color.FromArgb(
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte()),
            Width = reader.ReadDouble(),
            Height = reader.ReadDouble(),
            StylusTip = (StylusTip)reader.ReadInt32(),
            StylusTipTransform = ReadMatrix(reader),
            IsHighlighter = reader.ReadBoolean(),
            FitToCurve = reader.ReadBoolean(),
            IgnorePressure = reader.ReadBoolean(),
            BrushType = (BrushType)reader.ReadInt32(),
            BrushShader = ReadBrushShader(reader),
        };

        var stroke = new Stroke(points, attributes)
        {
            TaperMode = (StrokeTaperMode)reader.ReadInt32(),
        };
        foreach ((Guid id, object value) in ReadPropertyData(reader))
            attributes.LoadCustomPropertyData(id, value);
        foreach ((Guid id, object value) in ReadPropertyData(reader))
            stroke.LoadPropertyData(id, value);
        return stroke;
    }

    private static void WriteMatrix(BinaryWriter writer, Matrix matrix)
    {
        writer.Write(matrix.M11);
        writer.Write(matrix.M12);
        writer.Write(matrix.M21);
        writer.Write(matrix.M22);
        writer.Write(matrix.OffsetX);
        writer.Write(matrix.OffsetY);
    }

    private static Matrix ReadMatrix(BinaryReader reader) => new(
        reader.ReadDouble(),
        reader.ReadDouble(),
        reader.ReadDouble(),
        reader.ReadDouble(),
        reader.ReadDouble(),
        reader.ReadDouble());

    private static void WriteStylusPointDescription(
        BinaryWriter writer,
        StylusPointDescription description)
    {
        IReadOnlyList<StylusPointPropertyInfo> properties = description.GetStylusPointProperties();
        writer.Write(properties.Count);
        foreach (StylusPointPropertyInfo property in properties)
        {
            writer.Write(property.Id.ToByteArray());
            writer.Write(property.IsButton);
            writer.Write(property.Minimum);
            writer.Write(property.Maximum);
            writer.Write((int)property.Unit);
            writer.Write(property.Resolution);
        }
    }

    private static StylusPointDescription ReadStylusPointDescription(BinaryReader reader)
    {
        int propertyCount = ReadCount(reader, "stylus property");
        if (propertyCount < StylusPointDescription.RequiredPropertyCount)
            throw new InvalidDataException("A stylus description is missing required properties.");
        var properties = new StylusPointPropertyInfo[propertyCount];
        for (int i = 0; i < propertyCount; i++)
        {
            Guid id = new(reader.ReadBytes(16));
            bool isButton = reader.ReadBoolean();
            int minimum = reader.ReadInt32();
            int maximum = reader.ReadInt32();
            var unit = (StylusPointPropertyUnit)reader.ReadInt32();
            float resolution = reader.ReadSingle();
            properties[i] = new StylusPointPropertyInfo(
                new StylusPointProperty(id, isButton),
                minimum,
                maximum,
                unit,
                resolution);
        }
        return new StylusPointDescription(properties);
    }

    private static void WriteBrushShader(BinaryWriter writer, BrushShader? shader)
    {
        writer.Write(shader is not null);
        if (shader is null)
            return;

        if (!IsKnownBrushShader(shader))
        {
            throw new NotSupportedException(
                $"The custom ink brush shader '{shader.GetType().FullName}' cannot be serialized losslessly.");
        }
        writer.Write(shader.ShaderKey);
    }

    private static BrushShader? ReadBrushShader(BinaryReader reader)
    {
        if (!reader.ReadBoolean())
            return null;
        string key = reader.ReadString();
        if (key == EraserBrushShader.Instance.ShaderKey)
            return EraserBrushShader.Instance;
        foreach (BrushType brushType in Enum.GetValues<BrushType>())
        {
            BrushShader shader = BrushShaderRegistry.GetBuiltIn(brushType);
            if (shader.ShaderKey == key)
                return shader;
        }
        throw new InvalidDataException($"The ink brush shader key '{key}' is unknown.");
    }

    private static bool IsKnownBrushShader(BrushShader shader)
    {
        if (ReferenceEquals(shader, EraserBrushShader.Instance))
            return true;
        foreach (BrushType brushType in Enum.GetValues<BrushType>())
        {
            if (ReferenceEquals(shader, BrushShaderRegistry.GetBuiltIn(brushType)))
                return true;
        }
        return false;
    }

    private static void WritePropertyData(
        BinaryWriter writer,
        IEnumerable<KeyValuePair<Guid, object>> propertyData)
    {
        KeyValuePair<Guid, object>[] values = propertyData.ToArray();
        writer.Write(values.Length);
        foreach ((Guid id, object value) in values)
        {
            writer.Write(id.ToByteArray());
            WriteValue(writer, value);
        }
    }

    private static Dictionary<Guid, object> ReadPropertyData(BinaryReader reader)
    {
        int count = ReadCount(reader, "property-data");
        var result = new Dictionary<Guid, object>(count);
        for (int i = 0; i < count; i++)
        {
            Guid id = new(reader.ReadBytes(16));
            if (id == Guid.Empty || !result.TryAdd(id, ReadValue(reader)))
                throw new InvalidDataException("An ink property identifier is empty or duplicated.");
        }
        return result;
    }

    private static void WriteValue(BinaryWriter writer, object value)
    {
        Type type = value.GetType();
        bool isArray = type.IsArray;
        Type scalarType = isArray ? type.GetElementType()! : type;
        byte typeCode = GetTypeCode(scalarType);
        writer.Write((byte)(isArray ? typeCode | 0x80 : typeCode));

        if (isArray)
        {
            Array array = (Array)value;
            writer.Write(array.Length);
            foreach (object? item in array)
            {
                if (item is null)
                    throw new InvalidDataException("Ink property arrays cannot contain null values.");
                WriteScalar(writer, typeCode, item);
            }
        }
        else
        {
            WriteScalar(writer, typeCode, value);
        }
    }

    private static object ReadValue(BinaryReader reader)
    {
        byte encodedCode = reader.ReadByte();
        bool isArray = (encodedCode & 0x80) != 0;
        byte typeCode = (byte)(encodedCode & 0x7f);
        if (!isArray)
            return ReadScalar(reader, typeCode);

        int count = ReadCount(reader, "property-data array");
        Array array = CreateArray(typeCode, count);
        for (int i = 0; i < count; i++)
            array.SetValue(ReadScalar(reader, typeCode), i);
        return array;
    }

    private static Array CreateArray(byte typeCode, int count) => typeCode switch
    {
        1 => new byte[count],
        2 => new sbyte[count],
        3 => new short[count],
        4 => new ushort[count],
        5 => new int[count],
        6 => new uint[count],
        7 => new long[count],
        8 => new ulong[count],
        9 => new float[count],
        10 => new double[count],
        11 => new bool[count],
        12 => new char[count],
        13 => new string[count],
        14 => new decimal[count],
        15 => new DateTime[count],
        _ => throw new InvalidDataException($"Unsupported ink property-data type code '{typeCode}'."),
    };

    private static byte GetTypeCode(Type type)
    {
        if (type == typeof(byte)) return 1;
        if (type == typeof(sbyte)) return 2;
        if (type == typeof(short)) return 3;
        if (type == typeof(ushort)) return 4;
        if (type == typeof(int)) return 5;
        if (type == typeof(uint)) return 6;
        if (type == typeof(long)) return 7;
        if (type == typeof(ulong)) return 8;
        if (type == typeof(float)) return 9;
        if (type == typeof(double)) return 10;
        if (type == typeof(bool)) return 11;
        if (type == typeof(char)) return 12;
        if (type == typeof(string)) return 13;
        if (type == typeof(decimal)) return 14;
        if (type == typeof(DateTime)) return 15;
        throw new InvalidDataException($"Unsupported ink property-data type '{type.FullName}'.");
    }

    private static Type GetScalarType(byte typeCode) => typeCode switch
    {
        1 => typeof(byte),
        2 => typeof(sbyte),
        3 => typeof(short),
        4 => typeof(ushort),
        5 => typeof(int),
        6 => typeof(uint),
        7 => typeof(long),
        8 => typeof(ulong),
        9 => typeof(float),
        10 => typeof(double),
        11 => typeof(bool),
        12 => typeof(char),
        13 => typeof(string),
        14 => typeof(decimal),
        15 => typeof(DateTime),
        _ => throw new InvalidDataException($"Unsupported ink property-data code '{typeCode}'."),
    };

    private static void WriteScalar(BinaryWriter writer, byte typeCode, object value)
    {
        switch (typeCode)
        {
            case 1: writer.Write((byte)value); break;
            case 2: writer.Write((sbyte)value); break;
            case 3: writer.Write((short)value); break;
            case 4: writer.Write((ushort)value); break;
            case 5: writer.Write((int)value); break;
            case 6: writer.Write((uint)value); break;
            case 7: writer.Write((long)value); break;
            case 8: writer.Write((ulong)value); break;
            case 9: writer.Write((float)value); break;
            case 10: writer.Write((double)value); break;
            case 11: writer.Write((bool)value); break;
            case 12: writer.Write((char)value); break;
            case 13: writer.Write((string)value); break;
            case 14: writer.Write((decimal)value); break;
            case 15: writer.Write(((DateTime)value).ToBinary()); break;
            default: throw new InvalidDataException($"Unsupported ink property-data code '{typeCode}'.");
        }
    }

    private static object ReadScalar(BinaryReader reader, byte typeCode) => typeCode switch
    {
        1 => reader.ReadByte(),
        2 => reader.ReadSByte(),
        3 => reader.ReadInt16(),
        4 => reader.ReadUInt16(),
        5 => reader.ReadInt32(),
        6 => reader.ReadUInt32(),
        7 => reader.ReadInt64(),
        8 => reader.ReadUInt64(),
        9 => reader.ReadSingle(),
        10 => reader.ReadDouble(),
        11 => reader.ReadBoolean(),
        12 => reader.ReadChar(),
        13 => reader.ReadString(),
        14 => reader.ReadDecimal(),
        15 => DateTime.FromBinary(reader.ReadInt64()),
        _ => throw new InvalidDataException($"Unsupported ink property-data code '{typeCode}'."),
    };

    private static int ReadCount(BinaryReader reader, string itemName)
    {
        int count = reader.ReadInt32();
        if (count is < 0 or > MaximumItemCount)
            throw new InvalidDataException($"The {itemName} count is invalid.");
        return count;
    }

    private static bool IsStandardAttributeId(Guid id) =>
        id == DrawingAttributeIds.Color ||
        id == DrawingAttributeIds.StylusTip ||
        id == DrawingAttributeIds.StylusTipTransform ||
        id == DrawingAttributeIds.StylusHeight ||
        id == DrawingAttributeIds.StylusWidth ||
        id == DrawingAttributeIds.DrawingFlags ||
        id == DrawingAttributeIds.IsHighlighter;
}
