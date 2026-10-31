using System.Buffers.Binary;

namespace Rcinel.Translate.Editor.Services;

/// <summary>
/// Reads width and height from the header of PNG, JPEG, WebP and GIF files. The AI prompt turns
/// region fractions into pixel sizes with them ("a 180 x 36 px box"), and an image library would be
/// a large dependency for eight bytes of information.
/// </summary>
public static class ImageInfo
{
  public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp", ".gif"];

  public static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
  {
    ".png" => "image/png",
    ".jpg" or ".jpeg" => "image/jpeg",
    ".webp" => "image/webp",
    ".gif" => "image/gif",
    _ => "application/octet-stream",
  };

  /// <summary>The size, or null when the format isn't one of the four or the header is broken.</summary>
  public static (int Width, int Height)? ReadSize(ReadOnlySpan<byte> data)
  {
    if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
    {
      return (BinaryPrimitives.ReadInt32BigEndian(data[16..]), BinaryPrimitives.ReadInt32BigEndian(data[20..]));
    }
    if (data.Length >= 10 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
    {
      return (BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]));
    }
    if (data.Length >= 30 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
    {
      ReadOnlySpan<byte> chunk = data[12..16];
      if (chunk.SequenceEqual("VP8X"u8))
      {
        return (1 + (data[24] | (data[25] << 8) | (data[26] << 16)), 1 + (data[27] | (data[28] << 8) | (data[29] << 16)));
      }
      if (chunk.SequenceEqual("VP8 "u8))
      {
        return (BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF);
      }
      if (chunk.SequenceEqual("VP8L"u8))
      {
        uint bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]);
        return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
      }
      return null;
    }
    if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
    {
      // Walk the segments to the first start-of-frame marker, which holds the size.
      int i = 2;
      while (i + 9 < data.Length)
      {
        if (data[i] != 0xFF)
        {
          return null;
        }
        byte marker = data[i + 1];
        int length = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 2)..]);
        bool startOfFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
        if (startOfFrame)
        {
          return (BinaryPrimitives.ReadUInt16BigEndian(data[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(i + 5)..]));
        }
        i += 2 + length;
      }
    }
    return null;
  }
}
