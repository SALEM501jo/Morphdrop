using ImageMagick;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Morphdrop.Cutout;

/// <summary>
/// Runs the cut-out model on a picture and returns how much each pixel belongs to the subject (0 = background,
/// 255 = subject). The model sees the picture squeezed to 1024 × 1024; the answer is scaled back to the size asked for.
/// Loading the model takes a moment and holds a few hundred MB, so it's loaded per use and released straight after.
/// </summary>
internal static class SubjectFinder
{
    private const int Side = 1024;

    // IS-Net takes pixels as 0–1 shifted by a half.
    private const float Mean = 0.5f;

    /// <summary>Slow (a few seconds on a CPU): call it off the UI thread.</summary>
    public static byte[] FindMask(IMagickImage<byte> picture, int width, int height, string? modelPath = null)
    {
        string model = modelPath ?? CutoutModel.Find() ?? throw new InvalidOperationException("The cut-out model isn't downloaded yet.");
        DenseTensor<float> input = ToInput(picture);

        using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1);
        using var session = new InferenceSession(model, options);
        string inputName = session.InputMetadata.Keys.First();
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
            session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, input) });
        // The first of its outputs is the finest one, already squashed to 0–1.
        float[] scores = outputs.First().AsEnumerable<float>().Take(Side * Side).ToArray();
        return Scale(ToMask(scores), width, height);
    }

    private static DenseTensor<float> ToInput(IMagickImage<byte> picture)
    {
        using IMagickImage<byte> image = picture.Clone();
        if (image.HasAlpha)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
        }

        image.ColorSpace = ColorSpace.sRGB;
        image.FilterType = FilterType.Triangle;
        image.Resize(new MagickGeometry(Side, Side) { IgnoreAspectRatio = true });
        byte[] rgb = image.GetPixels().ToByteArray(PixelMapping.RGB)!;

        var tensor = new DenseTensor<float>(new[] { 1, 3, Side, Side });
        Span<float> data = tensor.Buffer.Span;
        const int plane = Side * Side;
        for (int i = 0; i < plane; i++)
        {
            for (int c = 0; c < 3; c++)
                data[c * plane + i] = rgb[i * 3 + c] / 255f - Mean;
        }

        return tensor;
    }

    /// <summary>Stretches the scores so the surest background is 0 and the surest subject 255.</summary>
    internal static byte[] ToMask(float[] values)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (float value in values)
        {
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        // The faintest 10 % at either end become fully transparent or fully kept, which clears the haze the model
        // leaves around a subject without hardening its real edges.
        float range = Math.Max(max - min, 1e-6f);
        var mask = new byte[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            float share = ((values[i] - min) / range - 0.1f) / 0.8f;
            mask[i] = (byte)Math.Clamp(MathF.Round(share * 255f), 0, 255);
        }

        return mask;
    }

    private static byte[] Scale(byte[] mask, int width, int height)
    {
        using var image = new MagickImage();
        image.ReadPixels(mask, new PixelReadSettings(Side, Side, StorageType.Char, "R"));
        image.ColorType = ColorType.Grayscale;
        image.FilterType = FilterType.Triangle; // smooth, without the ringing (a light halo) Lanczos leaves
        image.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
        return image.GetPixels().ToByteArray(0, 0, (uint)width, (uint)height, "R")!;
    }
}
