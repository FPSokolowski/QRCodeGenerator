using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using QRCodeGenerator.Models;
using QRCoder;
using QrGenerator = QRCoder.QRCodeGenerator;

namespace QRCodeGenerator.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View(new QrCodeViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Index(QrCodeViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        model.PngDataUri = $"data:image/png;base64,{Convert.ToBase64String(CreatePng(model.Text!))}";

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Download(string text, string format)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest("Tekst jest wymagany.");
        }

        var normalizedFormat = format.ToLowerInvariant();
        var fileName = $"qr-code-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{normalizedFormat}";

        return normalizedFormat switch
        {
            "png" => File(CreatePng(text), "image/png", fileName),
            "svg" => File(CreateSvg(text), "image/svg+xml", fileName),
            "txt" => File(CreatePlainText(text), "text/plain; charset=utf-8", fileName),
            _ => BadRequest("Nieobslugiwany format.")
        };
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private static byte[] CreatePng(string text)
    {
        using var generator = new QrGenerator();
        using var data = generator.CreateQrCode(text, QrGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(data);

        return qrCode.GetGraphic(20);
    }

    private static byte[] CreateSvg(string text)
    {
        using var generator = new QrGenerator();
        using var data = generator.CreateQrCode(text, QrGenerator.ECCLevel.Q);
        var qrCode = new SvgQRCode(data);

        return System.Text.Encoding.UTF8.GetBytes(qrCode.GetGraphic(20));
    }

    private static byte[] CreatePlainText(string text)
    {
        using var generator = new QrGenerator();
        using var data = generator.CreateQrCode(text, QrGenerator.ECCLevel.Q);
        var qrCode = new AsciiQRCode(data);

        return System.Text.Encoding.UTF8.GetBytes(qrCode.GetGraphic(1));
    }
}
