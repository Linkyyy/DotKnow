using System.Security.Cryptography;

namespace DotKnow.Core;

/// <summary>A bounded PNG kept in memory for one user-initiated screenshot session.</summary>
public sealed class ImageAttachment
{
    public const int MaxBytes = 4 * 1024 * 1024;
    public const int MaxEdge = 2048;
    public string Base64Data { get; }
    public string MimeType => "image/png";
    public string Digest { get; }
    public int Width { get; }
    public int Height { get; }
    public string Description => $"截图题目 ({Width} × {Height})";

    public ImageAttachment(byte[] png, int width, int height)
    {
        // Check the IHDR dimensions too; metadata and the transmitted image must agree.
        if (png == null || png.Length is < 33 or > MaxBytes || width is < 1 or > MaxEdge || height is < 1 or > MaxEdge ||
            !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !png.AsSpan(12, 4).SequenceEqual("IHDR"u8) ||
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)) != width ||
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)) != height)
            throw new ArgumentException("截图无效或过大，请重新选择较小的题目区域。");
        Width = width; Height = height;
        Base64Data = Convert.ToBase64String(png);
        Digest = Convert.ToHexString(SHA256.HashData(png));
    }
    // Never let default record formatting or diagnostics print screen contents.
    public override string ToString() => $"PNG {Width}x{Height}";
}

public static class ProblemSolving
{
    public static string ImageModel(AiSettings ai) => string.IsNullOrWhiteSpace(ai.ImageModel) ? ai.Model : ai.ImageModel.Trim();
    public static string? ConfigurationError(AiSettings ai)
    {
        var model = ImageModel(ai).ToLowerInvariant();
        if (ai.Provider == "DeepSeek") return "当前 DeepSeek 接口不支持截图，请切换到支持图片的服务商和视觉模型。";
        if (ai.Provider == "Qwen" && !model.Contains("vl") && !model.Contains("qvq"))
            return "截图解题需要视觉模型，请为通义千问选择 Qwen-VL 模型。";
        if (string.IsNullOrWhiteSpace(model) || model.Contains("embedding") || model.StartsWith("gpt-3.5") ||
            model.StartsWith("whisper") || model.StartsWith("tts-"))
            return "截图解题需要支持图片输入的视觉模型，请在 AI 服务与模型中设置。";
        return null;
    }
    public static TranslationRequest CreateRequest(AppSettings settings, ImageAttachment image, string? question = null)
    {
        if (ConfigurationError(settings.AI) is { } error) throw new ArgumentException(error);
        var request = AssistantPresets.CreateRequest(settings, image.Description, "solve", question);
        return request with { Image = image, Model = ImageModel(settings.AI) };
    }
    // Coordinates are physical desktop pixels, including monitors to the left/above the primary display.
    public static InputRect? Selection(double x1, double y1, double x2, double y2, InputRect desktop)
    {
        if (new[] { x1, y1, x2, y2, desktop.X, desktop.Y, desktop.Width, desktop.Height }.Any(x => !double.IsFinite(x)) || desktop.Width <= 0 || desktop.Height <= 0)
            return null;
        var left = Math.Clamp(Math.Min(x1, x2), desktop.X, desktop.X + desktop.Width);
        var top = Math.Clamp(Math.Min(y1, y2), desktop.Y, desktop.Y + desktop.Height);
        var right = Math.Clamp(Math.Max(x1, x2), desktop.X, desktop.X + desktop.Width);
        var bottom = Math.Clamp(Math.Max(y1, y2), desktop.Y, desktop.Y + desktop.Height);
        return right - left < 8 || bottom - top < 8 ? null : new(left, top, right - left, bottom - top);
    }
}
