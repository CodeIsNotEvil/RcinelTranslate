using CINE.Translate.Editor.Services;

namespace CINE.Translate.Editor.Tests;

public class ImageInfoTests
{
  [Fact]
  public void Reads_png() => Assert.Equal((1, 1), ImageInfo.ReadSize(EditorFactory.TinyPng));

  [Fact]
  public void Reads_gif() => Assert.Equal((300, 200), ImageInfo.ReadSize([.. "GIF89a"u8, 0x2C, 0x01, 0xC8, 0x00]));

  [Fact]
  public void Reads_jpeg()
  {
    // SOI, an APP0 segment of 16 bytes, then SOF0 with height 480 and width 640.
    byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. new byte[14], 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0xE0, 0x02, 0x80, 0x03, 0, 0, 0, 0];
    Assert.Equal((640, 480), ImageInfo.ReadSize(jpeg));
  }

  [Fact]
  public void Reads_webp_extended()
  {
    byte[] webp = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. "VP8X"u8, 10, 0, 0, 0, 0, 0, 0, 0, 0x7F, 0x02, 0x00, 0xDF, 0x01, 0x00];
    Assert.Equal((640, 480), ImageInfo.ReadSize(webp));
  }

  [Fact]
  public void Anything_else_is_not_an_image() => Assert.Null(ImageInfo.ReadSize("{\"not\": \"an image\"}"u8));
}
