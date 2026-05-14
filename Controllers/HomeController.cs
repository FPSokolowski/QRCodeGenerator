using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QRCodeGenerator.Models;
using QRCodeGenerator.Services;
using QRCoder;
using QrGenerator = QRCoder.QRCodeGenerator;

namespace QRCodeGenerator.Controllers;

public class HomeController : Controller
{
    private const string PreferencesCookieName = "qr_generator_preferences";

    public IActionResult Index()
    {
        var preferences = ReadPreferences();
        var model = ApplyPreferenceDefaults(new QrCodeViewModel(), preferences);
        SetPreferenceViewData(preferences);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(QrCodeViewModel model)
    {
        var preferences = ReadPreferences();
        model.Language = preferences.Language;
        model.ThemePreference = preferences.Theme;
        SetPreferenceViewData(preferences);

        await LoadPrintImageAsync(model);
        await LoadBrandingLogoAsync(model);
        var payload = BuildQrPayload(model);
        LocalizeModelState(model.Language);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var errorCorrectionLevel = ParseErrorCorrectionLevel(model.ErrorCorrectionLevel);
        model.GeneratedPayload = payload;
        model.PngDataUri = $"data:image/png;base64,{Convert.ToBase64String(CreatePng(payload, errorCorrectionLevel))}";
        model.BrandingWorkflowResult = ValidateBrandingWorkflow(model);
        model.PrintWorkflowResult = ValidatePrintWorkflow(model, GetModuleCount(payload, errorCorrectionLevel));
        SavePreferences(UpdateRecentPreferences(preferences, model));

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Validate(QrCodeViewModel model, string generatedPayload)
    {
        var preferences = ReadPreferences();
        model.Language = preferences.Language;
        model.ThemePreference = preferences.Theme;
        SetPreferenceViewData(preferences);

        if (string.IsNullOrWhiteSpace(generatedPayload))
        {
            ModelState.AddModelError(nameof(model.GeneratedPayload), T(model, "ValidationNoQr"));
            return View(nameof(Index), model);
        }

        var errorCorrectionLevel = ParseErrorCorrectionLevel(model.ErrorCorrectionLevel);
        model.GeneratedPayload = generatedPayload;
        model.PngDataUri = $"data:image/png;base64,{Convert.ToBase64String(CreatePng(generatedPayload, errorCorrectionLevel))}";

        if (model.ContentType == "social")
        {
            model.SocialLogoDataUri = GetSocialLogoDataUri(NormalizeSocialPlatform(model.SocialPlatform));
            model.BrandingLogoDataUri ??= model.SocialLogoDataUri;
        }

        model.BrandingWorkflowResult = ValidateBrandingWorkflow(model);
        model.ValidationResult = ValidateQr(generatedPayload, errorCorrectionLevel, model);
        model.PrintWorkflowResult = ValidatePrintWorkflow(model, model.ValidationResult.ModuleCount);

        return View(nameof(Index), model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetPreferences(string language, string theme, string? returnUrl = null)
    {
        var preferences = ReadPreferences();
        preferences.Language = UiText.NormalizeLanguage(language);
        preferences.Theme = theme == "dark" ? "dark" : "light";
        SavePreferences(preferences);

        return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? Url.Action(nameof(Index))! : returnUrl);
    }

    [Route("dynamic")]
    public IActionResult Dynamic()
    {
        var preferences = ReadPreferences();
        SetPreferenceViewData(preferences);

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Download(string text, string format, string errorCorrectionLevel = "Q")
    {
        var preferences = ReadPreferences();
        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest(UiText.Get(preferences.Language, "ValidationTextRequired"));
        }

        var level = ParseErrorCorrectionLevel(errorCorrectionLevel);
        var normalizedFormat = format?.ToLowerInvariant();
        var fileName = $"qr-code-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{normalizedFormat}";

        return normalizedFormat switch
        {
            "png" => File(CreatePng(text, level), "image/png", fileName),
            "svg" => File(CreateSvg(text, level), "image/svg+xml", fileName),
            "txt" => File(CreatePlainText(text, level), "text/plain; charset=utf-8", fileName),
            _ => BadRequest(UiText.Get(preferences.Language, "ValidationUnsupportedFormat"))
        };
    }

    private string BuildQrPayload(QrCodeViewModel model)
    {
        return model.ContentType switch
        {
            "url" => Require(model, model.Url, nameof(model.Url), "ValidationUrlRequired"),
            "wifi" => BuildWifiPayload(model),
            "vcard" => BuildVCardPayload(model),
            "email" => BuildEmailPayload(model),
            "sms" => BuildSmsPayload(model),
            "phone" => $"tel:{Require(model, model.PhoneNumber, nameof(model.PhoneNumber), "ValidationPhoneRequired")}",
            "geo" => BuildGeoPayload(model),
            "event" => BuildEventPayload(model),
            "crypto" => BuildCryptoPayload(model),
            "app" => Require(model, model.AppStoreUrl, nameof(model.AppStoreUrl), "ValidationAppRequired"),
            "pdf" => Require(model, model.PdfUrl, nameof(model.PdfUrl), "ValidationPdfRequired"),
            "social" => BuildSocialPayload(model),
            "multilink" => BuildMultiLinkPayload(model),
            "plain" => Require(model, model.Text, nameof(model.Text), "ValidationPlainTextRequired"),
            _ => Require(model, model.Text, nameof(model.Text), "ValidationPayloadRequired")
        };
    }

    private string Require(QrCodeViewModel model, string? value, string fieldName, string errorKey)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        ModelState.AddModelError(fieldName, T(model, errorKey));
        return string.Empty;
    }

    private static string T(QrCodeViewModel model, string key) => UiText.Get(model.Language, key);

    private void LocalizeModelState(string language)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Tekst moze miec maksymalnie 1000 znakow."] = "ValidationTextMax",
            ["Wybierz poprawny typ QR."] = "ValidationContentType",
            ["Wybierz poprawny poziom korekty bledu."] = "ValidationErrorCorrection",
            ["Wybierz poprawny profil kamery."] = "ValidationCameraProfile",
            ["Odleglosc musi byc w zakresie 10-200 cm."] = "ValidationScanDistance",
            ["Rozmycie musi byc w zakresie 0-10."] = "ValidationBlur",
            ["Wybierz poprawny format arkusza."] = "ValidationPrintFormat",
            ["Tytul moze miec maksymalnie 80 znakow."] = "ValidationPrintTitleMax",
            ["Opis moze miec maksymalnie 220 znakow."] = "ValidationDescriptionMax",
            ["Etykieta moze miec maksymalnie 40 znakow."] = "ValidationLabelMax",
            ["Wybierz poprawny gradient."] = "ValidationPrintGradient",
            ["Margines musi byc w zakresie 0-40 mm."] = "ValidationPrintMargin",
            ["Bleed musi byc w zakresie 0-8 mm."] = "ValidationBleed",
            ["Rozmiar QR musi byc w zakresie 20-180 mm."] = "ValidationQrSize",
            ["Liczba etykiet musi byc w zakresie 1-24."] = "ValidationLabelCount",
            ["Podaj kolor w formacie HEX."] = "ValidationHexColor",
            ["Wybierz poprawny gradient QR."] = "ValidationQrGradient",
            ["Wybierz poprawny styl oczu."] = "ValidationEyeStyle",
            ["Wybierz poprawna ramke."] = "ValidationFrame",
            ["CTA moze miec maksymalnie 40 znakow."] = "ValidationCtaMax",
            ["Wybierz poprawny template."] = "ValidationTemplate"
        };

        foreach (var key in ModelState.Keys.ToList())
        {
            var entry = ModelState[key];
            if (entry is null || entry.Errors.Count == 0)
            {
                continue;
            }

            var translated = entry.Errors
                .Select(error => TranslateValidationError(language, error.ErrorMessage, map))
                .ToList();

            entry.Errors.Clear();
            foreach (var message in translated)
            {
                entry.Errors.Add(message);
            }
        }
    }

    private static string TranslateValidationError(string language, string message, IReadOnlyDictionary<string, string> map)
    {
        if (map.TryGetValue(message, out var key))
        {
            return UiText.Get(language, key);
        }

        if (message.StartsWith("The ", StringComparison.Ordinal) && message.EndsWith(" field is required.", StringComparison.Ordinal))
        {
            return UiText.Get(language, "ValidationRequired");
        }

        return message;
    }

    private string BuildWifiPayload(QrCodeViewModel model)
    {
        var ssid = Require(model, model.WifiSsid, nameof(model.WifiSsid), "ValidationWifiSsidRequired");
        var encryption = model.WifiEncryption is "WEP" or "nopass" ? model.WifiEncryption : "WPA";
        var password = encryption == "nopass" ? string.Empty : EscapeWifi(model.WifiPassword ?? string.Empty);

        return $"WIFI:T:{encryption};S:{EscapeWifi(ssid)};P:{password};H:{model.WifiHidden.ToString().ToLowerInvariant()};;";
    }

    private string BuildVCardPayload(QrCodeViewModel model)
    {
        var name = Require(model, model.VCardName, nameof(model.VCardName), "ValidationVCardNameRequired");

        return string.Join("\n", new[]
        {
            "BEGIN:VCARD",
            "VERSION:3.0",
            $"FN:{EscapeVCard(name)}",
            OptionalLine("ORG", model.VCardCompany),
            OptionalLine("TITLE", model.VCardJobTitle),
            OptionalLine("TEL", model.VCardPhone),
            OptionalLine("EMAIL", model.VCardEmail),
            "END:VCARD"
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private string BuildEmailPayload(QrCodeViewModel model)
    {
        var address = Require(model, model.EmailAddress, nameof(model.EmailAddress), "ValidationEmailRequired");
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
        var phone = Require(model, model.PhoneNumber, nameof(model.PhoneNumber), "ValidationPhoneRequired");

        return string.IsNullOrWhiteSpace(model.SmsMessage)
            ? $"SMSTO:{phone}:"
            : $"SMSTO:{phone}:{model.SmsMessage.Trim()}";
    }

    private string BuildGeoPayload(QrCodeViewModel model)
    {
        var latitude = Require(model, model.GeoLatitude, nameof(model.GeoLatitude), "ValidationLatitudeRequired");
        var longitude = Require(model, model.GeoLongitude, nameof(model.GeoLongitude), "ValidationLongitudeRequired");

        return $"geo:{latitude},{longitude}";
    }

    private string BuildEventPayload(QrCodeViewModel model)
    {
        var title = Require(model, model.EventTitle, nameof(model.EventTitle), "ValidationEventTitleRequired");

        if (model.EventStart is null)
        {
            ModelState.AddModelError(nameof(model.EventStart), T(model, "ValidationEventStartRequired"));
        }

        if (model.EventEnd is null)
        {
            ModelState.AddModelError(nameof(model.EventEnd), T(model, "ValidationEventEndRequired"));
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
        var address = Require(model, model.CryptoAddress, nameof(model.CryptoAddress), "ValidationCryptoAddressRequired");
        var scheme = model.CryptoNetwork.Equals("ethereum", StringComparison.OrdinalIgnoreCase) ? "ethereum" : "bitcoin";
        var amount = string.IsNullOrWhiteSpace(model.CryptoAmount) ? string.Empty : $"?amount={Uri.EscapeDataString(model.CryptoAmount)}";

        return $"{scheme}:{address}{amount}";
    }

    private string BuildMultiLinkPayload(QrCodeViewModel model)
    {
        var links = Require(model, model.MultiLinks, nameof(model.MultiLinks), "ValidationMultiLinksRequired");

        return string.Join("\n", links
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(10));
    }

    private string BuildSocialPayload(QrCodeViewModel model)
    {
        var url = Require(model, model.SocialUrl, nameof(model.SocialUrl), "ValidationSocialUrlRequired");
        var platform = NormalizeSocialPlatform(model.SocialPlatform);

        if (!IsValidSocialUrl(url, platform))
        {
            ModelState.AddModelError(nameof(model.SocialUrl), T(model, "ValidationSocialUrlMismatch"));
        }

        model.DesignEnabled = true;
        model.SocialLogoDataUri = GetSocialLogoDataUri(platform);
        model.BrandingLogoDataUri ??= model.SocialLogoDataUri;

        return url;
    }

    private static string NormalizeSocialPlatform(string? platform)
    {
        return platform?.ToLowerInvariant() switch
        {
            "facebook" => "facebook",
            "linkedin" => "linkedin",
            "x" => "x",
            "youtube" => "youtube",
            "tiktok" => "tiktok",
            _ => "instagram"
        };
    }

    private static bool IsValidSocialUrl(string url, string platform)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();

        return platform switch
        {
            "instagram" => IsHost(host, "instagram.com"),
            "facebook" => IsHost(host, "facebook.com") || IsHost(host, "fb.com"),
            "linkedin" => IsHost(host, "linkedin.com"),
            "x" => IsHost(host, "x.com") || IsHost(host, "twitter.com"),
            "youtube" => IsHost(host, "youtube.com") || IsHost(host, "youtu.be"),
            "tiktok" => IsHost(host, "tiktok.com"),
            _ => false
        };
    }

    private static bool IsHost(string host, string expectedDomain)
    {
        return host == expectedDomain || host.EndsWith($".{expectedDomain}", StringComparison.Ordinal);
    }

    private static string GetSocialLogoDataUri(string platform)
    {
        var (label, fill) = platform switch
        {
            "facebook" => ("f", "#1877F2"),
            "linkedin" => ("in", "#0A66C2"),
            "x" => ("X", "#111111"),
            "youtube" => ("▶", "#FF0000"),
            "tiktok" => ("♪", "#111111"),
            _ => ("◎", "#E4405F")
        };
        var fontSize = label.Length > 1 ? 34 : 44;
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96">
              <rect width="96" height="96" rx="24" fill="{fill}"/>
              <text x="48" y="57" text-anchor="middle" font-family="Arial, Helvetica, sans-serif" font-size="{fontSize}" font-weight="800" fill="#fff">{System.Net.WebUtility.HtmlEncode(label)}</text>
            </svg>
            """;

        return $"data:image/svg+xml;base64,{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg))}";
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
        var verdict = isReadable
            ? UiText.Get(model.Language, "Readable")
            : UiText.Get(model.Language, "Risky");
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
            ModelState.AddModelError(nameof(model.PrintImage), T(model, "ValidationPrintImageType"));
            return;
        }

        if (model.PrintImage.Length > 3 * 1024 * 1024)
        {
            ModelState.AddModelError(nameof(model.PrintImage), T(model, "ValidationPrintImageSize"));
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
            ModelState.AddModelError(nameof(model.BrandingLogo), T(model, "ValidationBrandingLogoType"));
            return;
        }

        if (model.BrandingLogo.Length > 2 * 1024 * 1024)
        {
            ModelState.AddModelError(nameof(model.BrandingLogo), T(model, "ValidationBrandingLogoSize"));
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
            ? UiText.Get(model.Language, "BrandingWarning")
            : (model.Language == "en"
                ? "Branding is light, but it is still worth running QR Validation before print or publishing."
                : "Branding jest lekki, ale warto sprawdzic QR Validation przed drukiem lub publikacja.");
        var recommendation = riskScore switch
        {
            >= 35 => model.Language == "en" ? "Reduce the logo, disable the strong gradient, or raise error correction to H." : "Zmniejsz logo, wylacz mocny gradient albo podnies korekte bledu do H.",
            >= 20 => model.Language == "en" ? "Test the code on a weaker camera and in low-light mode." : "Przetestuj kod na slabszej kamerze i w trybie low-light.",
            >= 10 => model.Language == "en" ? "Check the Readability score after visual changes." : "Sprawdz wynik Readability po zmianach wizualnych.",
            _ => model.Language == "en" ? "The styling has low impact on readability." : "Stylizacja ma niski wplyw na czytelnosc."
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
            return model.Language == "en"
                ? $"Increase DPI or reduce QR print size. Current value is about {effectiveDpi} DPI."
                : $"Zwieksz DPI lub zmniejsz QR na wydruku. Aktualnie ok. {effectiveDpi} DPI.";
        }

        if (!isBleedSafe)
        {
            return model.Language == "en" ? "Set bleed to at least 3 mm for safe trimming." : "Ustaw bleed na minimum 3 mm dla bezpiecznego ciecia.";
        }

        if (!isCmykSafe)
        {
            return model.Language == "en"
                ? "For CMYK-safe export choose CMYK-safe, Graphite, or Mint and keep CMYK-safe enabled."
                : "Dla eksportu CMYK-safe wybierz gradient CMYK-safe, Graphite albo Mint i pozostaw wlaczony tryb CMYK-safe.";
        }

        if (model.PrintMarginMm < 8)
        {
            return model.Language == "en" ? "The margin is low. For print shops, at least 8 mm is safer." : "Margines jest niski. Dla drukarni bezpieczniej zostawic co najmniej 8 mm.";
        }

        return model.Language == "en"
            ? "The layout is ready for print with safe DPI, bleed, and a CMYK-safe palette."
            : "Uklad jest gotowy do wydruku z bezpiecznym DPI, bleed i paleta CMYK-safe.";
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
            return model.Language == "en" ? "The code has a strong readability buffer." : "Kod ma dobry zapas czytelnosci.";
        }

        if (model.BlurLevel >= 5)
        {
            return model.Language == "en" ? "Reduce blur or increase the physical QR size." : "Zmniejsz rozmycie lub zwieksz fizyczny rozmiar kodu.";
        }

        if (model.LowLight)
        {
            return model.Language == "en" ? "Improve lighting or use a higher error correction level." : "Popraw oswietlenie albo uzyj wyzszego poziomu korekty bledu.";
        }

        if (moduleCount >= 37)
        {
            return model.Language == "en" ? "Shorten the content or print the code larger." : "Skroc tekst lub drukuj kod w wiekszym rozmiarze.";
        }

        if (errorCorrectionLevel is QrGenerator.ECCLevel.L or QrGenerator.ECCLevel.M)
        {
            return model.Language == "en" ? "Raise error correction to Q or H." : "Podnies korekte bledu do Q albo H.";
        }

        return model.Language == "en"
            ? "Increase contrast and avoid scanning from too far away."
            : "Zwieksz kontrast i unikaj skanowania ze zbyt duzej odleglosci.";
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

    private UserPreferences ReadPreferences()
    {
        if (!Request.Cookies.TryGetValue(PreferencesCookieName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return new UserPreferences();
        }

        try
        {
            return JsonSerializer.Deserialize<UserPreferences>(value) ?? new UserPreferences();
        }
        catch (JsonException)
        {
            return new UserPreferences();
        }
    }

    private void SavePreferences(UserPreferences preferences)
    {
        preferences.Language = UiText.NormalizeLanguage(preferences.Language);
        preferences.Theme = preferences.Theme == "dark" ? "dark" : "light";
        preferences.Recent = preferences.Recent.Take(15).ToList();

        Response.Cookies.Append(PreferencesCookieName, JsonSerializer.Serialize(preferences), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddMonths(6),
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps
        });
    }

    private void SetPreferenceViewData(UserPreferences preferences)
    {
        ViewData["Language"] = preferences.Language;
        ViewData["Theme"] = preferences.Theme;
    }

    private static UserPreferences UpdateRecentPreferences(UserPreferences preferences, QrCodeViewModel model)
    {
        preferences.Recent.Insert(0, new RecentPreferenceSnapshot
        {
            Action = "generate",
            ContentType = model.ContentType,
            ErrorCorrectionLevel = model.ErrorCorrectionLevel,
            CameraProfile = model.CameraProfile,
            PrintFormat = model.PrintFormat,
            PrintGradient = model.PrintGradient,
            DesignTemplate = model.DesignTemplate,
            DesignEnabled = model.DesignEnabled,
            DarkModePreference = model.DarkMode
        });

        preferences.Recent = preferences.Recent.Take(15).ToList();

        return preferences;
    }

    private static QrCodeViewModel ApplyPreferenceDefaults(QrCodeViewModel model, UserPreferences preferences)
    {
        model.Language = preferences.Language;
        model.ThemePreference = preferences.Theme;

        if (preferences.Recent.Count == 0)
        {
            return model;
        }

        model.ContentType = MostCommon(preferences.Recent.Select(item => item.ContentType), model.ContentType);
        model.ErrorCorrectionLevel = MostCommon(preferences.Recent.Select(item => item.ErrorCorrectionLevel), model.ErrorCorrectionLevel);
        model.CameraProfile = MostCommon(preferences.Recent.Select(item => item.CameraProfile), model.CameraProfile);
        model.PrintFormat = MostCommon(preferences.Recent.Select(item => item.PrintFormat), model.PrintFormat);
        model.PrintGradient = MostCommon(preferences.Recent.Select(item => item.PrintGradient), model.PrintGradient);
        model.DesignTemplate = MostCommon(preferences.Recent.Select(item => item.DesignTemplate), model.DesignTemplate);
        model.DesignEnabled = preferences.Recent.Count(item => item.DesignEnabled) > preferences.Recent.Count / 2;
        model.DarkMode = preferences.Recent.Count(item => item.DarkModePreference) > preferences.Recent.Count / 2;

        return model;
    }

    private static string MostCommon(IEnumerable<string> values, string fallback)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault() ?? fallback;
    }
}
