$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$assetRoot = Join-Path $taskRoot 'assets'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'The Windows .NET Framework C# compiler is required.' }
$temporaryRoot = [System.IO.Path]::GetTempPath()
$buildName = 'FarmMacro.BrandBuild.' + [Guid]::NewGuid().ToString('N')
$helperSource = Join-Path $temporaryRoot ($buildName + '.cs')
$helperExe = Join-Path $temporaryRoot ($buildName + '.exe')
$source = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using FarmMacro;

static class BrandBuilder
{
    static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
    static int Main(string[] args)
    {
        string directory = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(directory);
        List<byte[]> frames = new List<byte[]>();
        foreach (int size in Sizes)
        {
            using (Bitmap mark = (Bitmap)BrandAssets.CreateMark(size)) frames.Add(EncodeFrame(mark));
        }
        string iconPath = Path.Combine(directory, "farm-macro.ico");
        using (BinaryWriter writer = new BinaryWriter(File.Create(iconPath)))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)Sizes.Length);
            uint offset = (uint)(6 + Sizes.Length * 16);
            for (int i = 0; i < Sizes.Length; i++)
            {
                writer.Write((byte)(Sizes[i] == 256 ? 0 : Sizes[i]));
                writer.Write((byte)(Sizes[i] == 256 ? 0 : Sizes[i]));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)frames[i].Length);
                writer.Write(offset);
                offset += (uint)frames[i].Length;
            }
            foreach (byte[] frame in frames) writer.Write(frame);
        }
        File.WriteAllText(Path.Combine(directory, "farm-macro.svg"), BrandAssets.SvgSource, new UTF8Encoding(false));
        using (Image preview = BrandAssets.CreateMark(256)) preview.Save(Path.Combine(directory, "farm-macro-preview.png"), ImageFormat.Png);
        Verify(iconPath);
        Console.WriteLine("Generated SVG, 256px preview, and 9-frame 32-bit ICO: " + iconPath);
        return 0;
    }

    static byte[] EncodeFrame(Bitmap bitmap)
    {
        int size = bitmap.Width;
        int maskStride = ((size + 31) / 32) * 4;
        byte[] mask = new byte[maskStride * size];
        using (MemoryStream memory = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(memory))
        {
            writer.Write((uint)40);
            writer.Write(size);
            writer.Write(size * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)0);
            writer.Write((uint)(size * size * 4 + mask.Length));
            writer.Write(0);
            writer.Write(0);
            writer.Write((uint)0);
            writer.Write((uint)0);
            for (int row = 0; row < size; row++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color pixel = bitmap.GetPixel(x, size - 1 - row);
                    writer.Write(pixel.B);
                    writer.Write(pixel.G);
                    writer.Write(pixel.R);
                    writer.Write(pixel.A);
                    if (pixel.A == 0) mask[row * maskStride + x / 8] |= (byte)(1 << (7 - x % 8));
                }
            }
            writer.Write(mask);
            return memory.ToArray();
        }
    }

    static void Verify(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes)))
        {
            if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1 || reader.ReadUInt16() != Sizes.Length)
                throw new InvalidDataException("Incorrect ICO header.");
            for (int i = 0; i < Sizes.Length; i++)
            {
                reader.BaseStream.Position = 6 + i * 16;
                int width = reader.ReadByte();
                int height = reader.ReadByte();
                reader.ReadByte(); reader.ReadByte();
                int planes = reader.ReadUInt16();
                int bits = reader.ReadUInt16();
                uint length = reader.ReadUInt32();
                uint offset = reader.ReadUInt32();
                if ((width == 0 ? 256 : width) != Sizes[i] || (height == 0 ? 256 : height) != Sizes[i] || planes != 1 || bits != 32 || offset + length > bytes.Length)
                    throw new InvalidDataException("Invalid ICO frame " + i + ".");
                // A single-frame stream removes Windows' DPI-dependent choice
                // between neighboring sizes (20px/24px/40px in particular).
                using (MemoryStream stream = new MemoryStream())
                {
                    using (BinaryWriter single = new BinaryWriter(stream, Encoding.UTF8, true))
                    {
                        single.Write((ushort)0); single.Write((ushort)1); single.Write((ushort)1);
                        single.Write(bytes, 6 + i * 16, 12);
                        single.Write((uint)22);
                        single.Write(bytes, (int)offset, (int)length);
                    }
                    stream.Position = 0;
                    using (Icon icon = new Icon(stream, Sizes[i], Sizes[i]))
                    {
                        if (icon.Width != Sizes[i] || icon.Height != Sizes[i])
                            throw new InvalidDataException("Frame " + Sizes[i] + " loaded as " + icon.Width + "x" + icon.Height + ".");
                        using (Bitmap decoded = icon.ToBitmap())
                            if (decoded.GetPixel(Sizes[i] / 2, Sizes[i] / 2).A == 0) throw new InvalidDataException("Decoded icon has an empty center.");
                    }
                }
            }
        }
        using (MemoryStream allFrames = new MemoryStream(bytes))
        using (Icon icon = new Icon(allFrames))
        using (Bitmap decoded = icon.ToBitmap())
            if (decoded.Width == 0) throw new InvalidDataException("Multi-frame icon did not load.");
        // Exercise fallback ownership too: LoadIcon must return an independent icon.
        using (Icon fallback = BrandAssets.LoadIcon())
        using (Bitmap decoded = fallback.ToBitmap())
            if (decoded.Width == 0) throw new InvalidDataException("Fallback icon did not load.");
    }
}
'@
try {
    [System.IO.File]::WriteAllText($helperSource, $source)
    & $compilerPath /nologo /target:exe /optimize+ /r:System.Drawing.dll "/out:$helperExe" (Join-Path $taskRoot 'BrandAssets.cs') $helperSource
    if ($LASTEXITCODE -ne 0) { throw 'Brand asset helper compilation failed.' }
    & $helperExe $assetRoot
    if ($LASTEXITCODE -ne 0) { throw 'Brand asset generation or validation failed.' }
}
finally {
    if (Test-Path -LiteralPath $helperSource) { Remove-Item -LiteralPath $helperSource -Force }
    if (Test-Path -LiteralPath $helperExe) { Remove-Item -LiteralPath $helperExe -Force }
}
