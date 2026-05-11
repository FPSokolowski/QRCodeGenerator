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
    public async Task<IActionResult> Index(QrCodeViewModel model)
    {
        await LoadPrintImageAsync(model);
        await LoadBrandingLogoAsync(model);
        var payload = BuildQrPayload(model);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var errorCorrectionLevel = ParseErrorCorrectionLevel(model.ErrorCorrectionLevel);
        model.GeneratedPayload = payload;
        model.PngDataUri = $"data:image/png;base64,{Convert.ToBase64String(CreatePng(payload, errorCorrectionLevel))}";
        model.BrandingWorkflowResult = ValidateBrandingWorkflow(model);
        model.ValidationResult = ValidateQr(payload, errorCorrectionLevel, model);
        model.PrintWorkflowResult = ValidatePrintWorkflow(model, model.ValidationResult.ModuleCount);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Download(string text, string format, string errorCorrectionLevel = "Q")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest("Tekst jest wymagany.");
        }

        var level = ParseErrorCorrectionLevel(errorCorrectionLevel);
        var normalizedFormat = format?.ToLowerInvariant();
        var fileName = $"qr-code-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{normalizedFormat}";

        return normalizedFormat switch
        {
            "png" => File(CreatePng(text, level), "image/png", fileName),
            "svg" => File(CreateSvg(text, level), "image/svg+xml", fileName),
            "txt" => File(CreatePlainText(text, level), "text/plain; charset=utf-8", fileName),
            _ => BadRequest("Nieobslugiwany format.")
        };
    }

    private string BuildQrPayload(QrCodeViewModel model)
    {
        return model.ContentType switch
        {
            "url" => Require(model.Url, nameof(model.Url), "Wpisz adres strony."),
            "wifi" => BuildWifiPayload(model),
            "vcard" => BuildVCardPayload(model),
            "email" => BuildEmailPayload(model),
            "sms" => BuildSmsPayload(model),
            "phone" => $"tel:{Require(model.PhoneNumber, nameof(model.PhoneNumber), "Wpisz numer telefonu.")}",
            "geo" => BuildGeoPayload(model),
            "event" => BuildEventPayload(model),
            "crypto" => BuildCryptoPayload(model),
            "app" => Require(model.AppStoreUrl, nameof(model.AppStoreUrl), "Wpisz link do aplikacji."),
            "pdf" => Require(model.PdfUrl, nameof(model.PdfUrl), "Wpisz link do dokumentu PDF."),
            "social" => Require(model.SocialUrl, nameof(model.SocialUrl), "Wpisz link do profilu social media."),
            "multilink" => BuildMultiLinkPayload(model),
            "plain" => Require(model.Text, nameof(model.Text), "Wpisz tekst."),
            _ => Require(model.Text, nameof(model.Text), "Wpisz tekst do zakodowania.")
        };
    }

    private string Require(string? value, string fieldName, string errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        ModelState.AddModelError(fieldName, errorMessage);
        return string.Empty;
    }

    private string BuildWifiPayload(QrCodeViewModel model)
    {
        var ssid = Require(model.WifiSsid, nameof(model.WifiSsid), "Wpisz nazwe sieci WiFi.");
        var encryption = model.WifiEncryption is "WEP" or "nopass" ? model.WifiEncryption : "WPA";
        var password = encryption == "nopass" ? string.Empty : EscapeWifi(model.WifiPassword ?? string.Empty);

        return $"WIFI:T:{encryption};S:{EscapeWifi(ssid)};P:{password};H:{model.WifiHidden.ToString().ToLowerInvariant()};;";
    }

    private string BuildVCardPayload(QrCodeViewModel model)
    {
        var name = Require(model.VCardName, nameof(model.VCardName), "Wpisz imie i nazwisko kontaktu.");

        return string.Join("\n", new[]
        {
            "BEGIN:VCARD",
            "VERSION:3.0",
            $"FN:{EscapeVCard(name)}",
            OptionalLine("ORG", model.VCardCompany),
            OptionalLine("TEL", model.VCardPhone),
            OptionalLine("EMAIL", model.VCardEmail),
            "END:VCARD"
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private string BuildEmailPayload(QrCodeViewModel model)
    {
        var address = Require(model.EmailAddress, nameof(model.EmailAddress), "Wpisz adres email.");
        var query = new List<string>();

        if (!string.IsNullOrWhiteSpace(model.EmailSubject))
        {
            query.Add($"subject={Uri.EscapeDataString(model.EmailSubject)}");
        }

        if (!string.IsNullOrWhiteSpace(model.EmailBody))
        {
            query.Add($"body={Uri.EscapeDataString(model.EmailBody)}");
        }

        return $"mailto:{address}{(query.Count > 0 ? "?" + string.Join("&", query) : string.Empty)}";
    }

    private string BuildSmsPayload(QrCodeViewModel model)
    {
        var phone = Require(model.PhoneNumber, nameof(model.PhoneNumber), "Wpisz numer telefonu.");

        return string.IsNullOrWhiteSpace(model.SmsMessage)
            ? $"SMSTO:{phone}:"
            : $"SMSTO:{phone}:{model.SmsMessage.Trim()}";
    }

    private string BuildGeoPayload(QrCodeViewModel model)
    {
        var latitude = Require(model.GeoLatitude, nameof(model.GeoLatitude), "Wpisz szerokosc geograficzna.");
        var longitude = Require(model.GeoLongitude, nameof(model.GeoLongitude), "Wpisz dlugosc geograficzna.");

        return $"geo:{latitude},{longitude}";
    }

    private string BuildEventPayload(QrCodeViewModel model)
    {
        var title = Require(model.EventTitle, nameof(model.EventTitle), "Wpisz nazwe wydarzenia.");

        if (model.EventStart is null)
        {
            ModelState.AddModelError(nameof(model.EventStart), "Wpisz date rozpoczecia.");
        }

        if (model.EventEnd is null)
        {
            ModelState.AddModelError(nameof(model.EventEnd), "Wpisz date zakonczenia.");
        }

        return string.Join("\n", new[]
        {
            "BEGIN:VEVENT",
            $"SUMMARY:{EscapeVCard(title)}",
            model.EventStart is null ? string.Empty : $"DTSTART:{FormatEventDate(model.EventStart.Value)}",
            model.EventEnd is null ? string.Empty : $"DTEND:{FormatEventDate(model.EventEnd.Value)}",
            OptionalLine("LOCATION", model.EventLocation),
            "END:VEVENT"
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private string BuildCryptoPayload(QrCodeViewModel model)
    {
        var address = Require(model.CryptoAddress, nameof(model.CryptoAddress), "Wpisz adres portfela.");
        var scheme = model.CryptoNetwork.Equals("ethereum", StringComparison.OrdinalIgnoreCase) ? "ethereum" : "bitcoin";
        var amount = string.IsNullOrWhiteSpace(model.CryptoAmount) ? string.Empty : $"?amount={Uri.EscapeDataString(model.CryptoAmount)}";

        return $"{scheme}:{address}{amount}";
    }

    private string BuildMultiLinkPayload(QrCodeViewModel model)
    {
        var links = Require(model.MultiLinks, nameof(model.MultiLinks), "Wpisz co najmniej jeden link.");

        return string.Join("\n", links
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(10));
    }

    private static string OptionalLine(string name, string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : $"{name}:{EscapeVCard(value.Trim())}";
    }

    private static string EscapeWifi(string value)
    {
        return value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace(":", "\\:");
    }

    private static string EscapeVCard(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\n", "\\n").Replace(",", "\\,").Replace(";", "\\;");
    }

    private static string FormatEventDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private static byte[] CreatePng(string text, QrGenerator.ECCLevel errorCorrectionLevel)
    {
        using var generator = new QrGenerator();
        using var data = generator.CreateQrCode(text, errorCorrectionLevel);
        var qrCode = new PngByteQRCode(data);

        return qrCode.GetGraphic(20);
    }

    private static QrValidationResult ValidateQr(string text, QrGenerator.ECCLevel errorCorrectionLevel, QrCodeViewModel model)
    {
        var moduleCount = GetModuleCount(text, errorCorrectionLevel);
        var densityPenalty = Math.Max(0, moduleCount - 29) * 0.85;
        var distancePenalty = Math.Max(0, model.ScanDistanceCm - 35) * 0.35 + Math.Max(0, 18 - model.ScanDistanceCm) * 1.15;
        var cameraPenalty = model.CameraProfile switch
        {
            "ideal" => 0,
            "budget" => 17,
            _ => 7
        };
        var lowLightPenalty = model.LowLight ? 16 : 0;
        var blurPenalty = model.BlurLevel * 5.5;
        var brandingPenalty = CalculateBrandingRisk(model) * 0.45;
        var eccBonus = errorCorrectionLevel switch
        {
            QrGenerator.ECCLevel.M => 5,
            QrGenerator.ECCLevel.Q => 11,
            QrGenerator.ECCLevel.H => 16,
            _ => 0
        };

        var readabilityScore = ClampScore(100 - densityPenalty - distancePenalty - cameraPenalty - lowLightPenalty - blurPenalty - brandingPenalty + eccBonus);
        var printSafetyScore = ClampScore(96 - (Math.Max(0, moduleCount - 25) * 1.15) - (text.Length > 250 ? 8 : 0) - (brandingPenalty * 0.55) + eccBonus);
        var isReadable = readabilityScore >= 70 && printSafetyScore >= 60;
        var verdict = isReadable ? "Czytelny w symulowanych warunkach" : "Ryzykowny do skanowania";
        var recommendation = BuildRecommendation(model, errorCorrectionLevel, readabilityScore, printSafetyScore, moduleCount);

        return new QrValidationResult(readabilityScore, printSafetyScore, isReadable, verdict, moduleCount, recommendation);
    }

    private async Task LoadPrintImageAsync(QrCodeViewModel model)
    {
        if (model.PrintImage is null || model.PrintImage.Length == 0)
        {
            return;
        }

        if (!model.PrintImage.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.PrintImage), "Plik musi byc obrazem.");
            return;
        }

        if (model.PrintImage.Length > 3 * 1024 * 1024)
        {
            ModelState.AddModelError(nameof(model.PrintImage), "Obrazek moze miec maksymalnie 3 MB.");
            return;
        }

        await using var stream = model.PrintImage.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        model.PrintImageDataUri = $"data:{model.PrintImage.ContentType};base64,{Convert.ToBase64String(memory.ToArray())}";
    }

    private async Task LoadBrandingLogoAsync(QrCodeViewModel model)
    {
        if (model.BrandingLogo is null || model.BrandingLogo.Length == 0)
        {
            return;
        }

        if (!model.BrandingLogo.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.BrandingLogo), "Logo musi byc obrazem.");
            return;
        }

        if (model.BrandingLogo.Length > 2 * 1024 * 1024)
        {
            ModelState.AddModelError(nameof(model.BrandingLogo), "Logo moze miec maksymalnie 2 MB.");
            return;
        }

        await using var stream = model.BrandingLogo.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        model.BrandingLogoDataUri = $"data:{model.BrandingLogo.ContentType};base64,{Convert.ToBase64String(memory.ToArray())}";
    }

    private static BrandingWorkflowResult ValidateBrandingWorkflow(QrCodeViewModel model)
    {
        var riskScore = CalculateBrandingRisk(model);
        var shouldRunValidation = model.DesignEnabled && riskScore >= 10;
        var warning = shouldRunValidation
            ? "Po modyfikacji wygladu niektore urzadzenia moga miec problem ze skanowaniem. Uruchom QR Validation po kazdej zmianie brandingu."
            : "Branding jest lekki, ale warto sprawdzic QR Validation przed drukiem lub publikacja.";
        var recommendation = riskScore switch
        {
            >= 35 => "Zmniejsz logo, wylacz mocny gradient albo podnies korekte bledu do H.",
            >= 20 => "Przetestuj kod na slabszej kamerze i w trybie low-light.",
            >= 10 => "Sprawdz wynik Readability po zmianach wizualnych.",
            _ => "Stylizacja ma niski wplyw na czytelnosc."
        };

        return new BrandingWorkflowResult(riskScore, shouldRunValidation, warning, recommendation);
    }

    private static int CalculateBrandingRisk(QrCodeViewModel model)
    {
        if (!model.DesignEnabled)
        {
            return 0;
        }

        var risk = 0;
        risk += model.QrDesignGradient == "none" ? 0 : 8;
        risk += model.RoundedModules ? 6 : 0;
        risk += model.EyeStyle == "standard" ? 0 : 7;
        risk += string.IsNullOrWhiteSpace(model.BrandingLogoDataUri) ? 0 : 12;
        risk += model.FrameStyle == "none" ? 0 : 4;
        risk += model.DarkMode ? 8 : 0;
        risk += model.DesignTemplate is "premium" or "sticker" ? 6 : 0;
        risk += HasLowContrast(model.ForegroundColor, model.BackgroundColor) ? 12 : 0;

        return Math.Clamp(risk, 0, 100);
    }

    private static bool HasLowContrast(string foreground, string background)
    {
        return Math.Abs(GetColorBrightness(foreground) - GetColorBrightness(background)) < 90;
    }

    private static int GetColorBrightness(string color)
    {
        if (color.Length != 7 || color[0] != '#')
        {
            return 255;
        }

        var r = Convert.ToInt32(color.Substring(1, 2), 16);
        var g = Convert.ToInt32(color.Substring(3, 2), 16);
        var b = Convert.ToInt32(color.Substring(5, 2), 16);

        return (int)((r * 0.299) + (g * 0.587) + (b * 0.114));
    }

    private static PrintWorkflowResult ValidatePrintWorkflow(QrCodeViewModel model, int moduleCount)
    {
        var qrPixelSize = moduleCount * 20;
        var effectiveDpi = (int)Math.Round(qrPixelSize / (model.QrPrintSizeMm / 25.4));
        var isDpiSafe = effectiveDpi >= 300;
        var isBleedSafe = model.PrintBleedMm >= 3;
        var isCmykSafe = model.CmykSafeExport && model.PrintGradient is "cmyk" or "graphite" or "mint";
        var formatLabel = model.PrintFormat switch
        {
            "a5" => "A5",
            "letter" => "US Letter",
            "dl" => "DL",
            "square" => "Kwadrat 210 mm",
            _ => "A4"
        };
        var recommendation = BuildPrintRecommendation(model, effectiveDpi, isDpiSafe, isBleedSafe, isCmykSafe);

        return new PrintWorkflowResult(effectiveDpi, isDpiSafe, isCmykSafe, isBleedSafe, formatLabel, recommendation);
    }

    private static string BuildPrintRecommendation(QrCodeViewModel model, int effectiveDpi, bool isDpiSafe, bool isBleedSafe, bool isCmykSafe)
    {
        if (!isDpiSafe)
        {
            return $"Zwieksz DPI lub zmniejsz QR na wydruku. Aktualnie ok. {effectiveDpi} DPI.";
        }

        if (!isBleedSafe)
        {
            return "Ustaw bleed na minimum 3 mm dla bezpiecznego ciecia.";
        }

        if (!isCmykSafe)
        {
            return "Dla eksportu CMYK-safe wybierz gradient CMYK-safe, Graphite albo Mint i pozostaw wlaczony tryb CMYK-safe.";
        }

        if (model.PrintMarginMm < 8)
        {
            return "Margines jest niski. Dla drukarni bezpieczniej zostawic co najmniej 8 mm.";
        }

        return "Uklad jest gotowy do wydruku z bezpiecznym DPI, bleed i paleta CMYK-safe.";
    }

    private static int GetModuleCount(string text, QrGenerator.ECCLevel errorCorrectionLevel)
    {
        using var generator = new QrGenerator();
        using var data = generator.CreateQrCode(text, errorCorrectionLevel);

        return data.ModuleMatrix.Count;
    }

    private static int ClampScore(double score)
    {
        return (int)Math.Clamp(Math.Round(score), 0, 100);
    }

    private static string BuildRecommendation(QrCodeViewModel model, QrGenerator.ECCLevel errorCorrectionLevel, int readabilityScore, int printSafetyScore, int moduleCount)
    {
        if (readabilityScore >= 85 && printSafetyScore >= 80)
        {
            return "Kod ma dobry zapas czytelnosci.";
        }

        if (model.BlurLevel >= 5)
        {
            return "Zmniejsz rozmycie lub zwieksz fizyczny rozmiar kodu.";
        }

        if (model.LowLight)
        {
            return "Popraw oswietlenie albo uzyj wyzszego poziomu korekty bledu.";
        }

        if (moduleCount >= 37)
        {
            return "Skroc tekst lub drukuj kod w wiekszym rozmiarze.";
        }

        if (errorCorrectionLevel is QrGenerator.ECCLevel.L or QrGenerator.ECCLevel.M)
        {
            return "Podnies korekte bledu do Q albo H.";
        }

        return "Zwieksz kontrast i unikaj skanowania ze zbyt duzej odleglosci.";
    }

    private static byte[] CreateSvg(string text, QrGenerator.ECCLevel errorCorrectionLevel)
    {
        using var generator = new QrGenerator();
        using var data = generator.CreateQrCode(text, errorCorrectionLevel);
        var qrCode = new SvgQRCode(data);

        return System.Text.Encoding.UTF8.GetBytes(qrCode.GetGraphic(20));
    }

    private static byte[] CreatePlainText(string text, QrGenerator.ECCLevel errorCorrectionLevel)
    {
        using var generator = new QrGenerator();
        using var data = generator.CreateQrCode(text, errorCorrectionLevel);
        var qrCode = new AsciiQRCode(data);

        return System.Text.Encoding.UTF8.GetBytes(qrCode.GetGraphic(1));
    }

    private static QrGenerator.ECCLevel ParseErrorCorrectionLevel(string? errorCorrectionLevel)
    {
        return errorCorrectionLevel?.ToUpperInvariant() switch
        {
            "L" => QrGenerator.ECCLevel.L,
            "M" => QrGenerator.ECCLevel.M,
            "Q" => QrGenerator.ECCLevel.Q,
            "H" => QrGenerator.ECCLevel.H,
            _ => QrGenerator.ECCLevel.Q
        };
    }
}
