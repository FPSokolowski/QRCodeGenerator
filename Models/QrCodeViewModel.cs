using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace QRCodeGenerator.Models;

public class QrCodeViewModel
{
    [StringLength(1000, ErrorMessage = "Tekst moze miec maksymalnie 1000 znakow.")]
    [Display(Name = "Tekst")]
    public string? Text { get; set; }

    [Required]
    [RegularExpression("general|url|wifi|vcard|email|sms|phone|geo|event|crypto|app|pdf|social|multilink|plain", ErrorMessage = "Wybierz poprawny typ QR.")]
    public string ContentType { get; set; } = "general";

    public string? GeneratedPayload { get; set; }

    [Display(Name = "URL")]
    public string? Url { get; set; }

    [Display(Name = "Nazwa sieci")]
    public string? WifiSsid { get; set; }

    [Display(Name = "Haslo WiFi")]
    public string? WifiPassword { get; set; }

    [Display(Name = "Szyfrowanie")]
    public string WifiEncryption { get; set; } = "WPA";

    [Display(Name = "Siec ukryta")]
    public bool WifiHidden { get; set; }

    [Display(Name = "Imie i nazwisko")]
    public string? VCardName { get; set; }

    [Display(Name = "Firma")]
    public string? VCardCompany { get; set; }

    [Display(Name = "Stanowisko")]
    public string? VCardJobTitle { get; set; }

    [Display(Name = "Telefon")]
    public string? VCardPhone { get; set; }

    [Display(Name = "Email")]
    public string? VCardEmail { get; set; }

    [Display(Name = "Adres email")]
    public string? EmailAddress { get; set; }

    [Display(Name = "Temat")]
    public string? EmailSubject { get; set; }

    [Display(Name = "Tresc")]
    public string? EmailBody { get; set; }

    [Display(Name = "Numer telefonu")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "Tresc SMS")]
    public string? SmsMessage { get; set; }

    [Display(Name = "Szerokosc geograficzna")]
    public string? GeoLatitude { get; set; }

    [Display(Name = "Dlugosc geograficzna")]
    public string? GeoLongitude { get; set; }

    [Display(Name = "Nazwa wydarzenia")]
    public string? EventTitle { get; set; }

    [Display(Name = "Start")]
    public DateTime? EventStart { get; set; }

    [Display(Name = "Koniec")]
    public DateTime? EventEnd { get; set; }

    [Display(Name = "Miejsce")]
    public string? EventLocation { get; set; }

    [Display(Name = "Siec")]
    public string CryptoNetwork { get; set; } = "bitcoin";

    [Display(Name = "Adres portfela")]
    public string? CryptoAddress { get; set; }

    [Display(Name = "Kwota")]
    public string? CryptoAmount { get; set; }

    [Display(Name = "Link do aplikacji")]
    public string? AppStoreUrl { get; set; }

    [Display(Name = "Link do PDF")]
    public string? PdfUrl { get; set; }

    [Display(Name = "Platforma")]
    public string SocialPlatform { get; set; } = "instagram";

    [Display(Name = "Profil social")]
    public string? SocialUrl { get; set; }

    [Display(Name = "Linki menu")]
    public string? MultiLinks { get; set; }

    [Required]
    [RegularExpression("L|M|Q|H", ErrorMessage = "Wybierz poprawny poziom korekty bledu.")]
    [Display(Name = "Korekta bledu")]
    public string ErrorCorrectionLevel { get; set; } = "Q";

    [Required]
    [RegularExpression("ideal|standard|budget", ErrorMessage = "Wybierz poprawny profil kamery.")]
    [Display(Name = "Symulator kamery")]
    public string CameraProfile { get; set; } = "standard";

    [Range(10, 200, ErrorMessage = "Odleglosc musi byc w zakresie 10-200 cm.")]
    [Display(Name = "Odleglosc skanowania (cm)")]
    public int ScanDistanceCm { get; set; } = 35;

    [Display(Name = "Tryb low-light")]
    public bool LowLight { get; set; }

    [Range(0, 10, ErrorMessage = "Rozmycie musi byc w zakresie 0-10.")]
    [Display(Name = "Rozmycie")]
    public int BlurLevel { get; set; } = 1;

    [Required]
    [RegularExpression("a4|a5|letter|dl|square", ErrorMessage = "Wybierz poprawny format arkusza.")]
    [Display(Name = "Format arkusza")]
    public string PrintFormat { get; set; } = "a4";

    [StringLength(80, ErrorMessage = "Tytul moze miec maksymalnie 80 znakow.")]
    [Display(Name = "Tytul wydruku")]
    public string? PrintTitle { get; set; }

    [StringLength(220, ErrorMessage = "Opis moze miec maksymalnie 220 znakow.")]
    [Display(Name = "Opis")]
    public string? PrintDescription { get; set; }

    [StringLength(40, ErrorMessage = "Etykieta moze miec maksymalnie 40 znakow.")]
    [Display(Name = "Etykieta")]
    public string? PrintLabel { get; set; }

    [Display(Name = "Obrazek")]
    public IFormFile? PrintImage { get; set; }

    public string? PrintImageDataUri { get; set; }

    [Required]
    [RegularExpression("aurora|sunset|mint|graphite|cmyk", ErrorMessage = "Wybierz poprawny gradient.")]
    [Display(Name = "Tlo wydruku")]
    public string PrintGradient { get; set; } = "aurora";

    [Range(0, 40, ErrorMessage = "Margines musi byc w zakresie 0-40 mm.")]
    [Display(Name = "Margines drukarski (mm)")]
    public int PrintMarginMm { get; set; } = 12;

    [Range(0, 8, ErrorMessage = "Bleed musi byc w zakresie 0-8 mm.")]
    [Display(Name = "Bleed (mm)")]
    public int PrintBleedMm { get; set; } = 3;

    [Range(20, 180, ErrorMessage = "Rozmiar QR musi byc w zakresie 20-180 mm.")]
    [Display(Name = "Rozmiar QR na wydruku (mm)")]
    public int QrPrintSizeMm { get; set; } = 55;

    [Range(1, 24, ErrorMessage = "Liczba etykiet musi byc w zakresie 1-24.")]
    [Display(Name = "Liczba etykiet")]
    public int LabelCount { get; set; } = 1;

    [Display(Name = "CMYK-safe export")]
    public bool CmykSafeExport { get; set; } = true;

    [Display(Name = "Wlacz Design / Branding")]
    public bool DesignEnabled { get; set; }

    [RegularExpression("^#([0-9a-fA-F]{6})$", ErrorMessage = "Podaj kolor w formacie HEX.")]
    [Display(Name = "Kolor QR")]
    public string ForegroundColor { get; set; } = "#111827";

    [RegularExpression("^#([0-9a-fA-F]{6})$", ErrorMessage = "Podaj kolor w formacie HEX.")]
    [Display(Name = "Kolor tla QR")]
    public string BackgroundColor { get; set; } = "#ffffff";

    [Required]
    [RegularExpression("none|ocean|sunrise|violet|forest|mono", ErrorMessage = "Wybierz poprawny gradient QR.")]
    [Display(Name = "Gradient QR")]
    public string QrDesignGradient { get; set; } = "none";

    [Display(Name = "Rounded modules")]
    public bool RoundedModules { get; set; }

    [Required]
    [RegularExpression("standard|rounded|bold|outline", ErrorMessage = "Wybierz poprawny styl oczu.")]
    [Display(Name = "Custom eyes")]
    public string EyeStyle { get; set; } = "standard";

    [Display(Name = "Logo w srodku")]
    public IFormFile? BrandingLogo { get; set; }

    public string? BrandingLogoDataUri { get; set; }

    public string? SocialLogoDataUri { get; set; }

    [Required]
    [RegularExpression("none|soft|ticket|poster|badge", ErrorMessage = "Wybierz poprawna ramke.")]
    [Display(Name = "Ramka")]
    public string FrameStyle { get; set; } = "none";

    [StringLength(40, ErrorMessage = "CTA moze miec maksymalnie 40 znakow.")]
    [Display(Name = "CTA")]
    public string? CtaText { get; set; } = "Scan me";

    [Display(Name = "Dark mode")]
    public bool DarkMode { get; set; }

    [Required]
    [RegularExpression("clean|brand|event|premium|sticker", ErrorMessage = "Wybierz poprawny template.")]
    [Display(Name = "Template")]
    public string DesignTemplate { get; set; } = "clean";

    public string? PngDataUri { get; set; }

    public QrValidationResult? ValidationResult { get; set; }

    public PrintWorkflowResult? PrintWorkflowResult { get; set; }

    public BrandingWorkflowResult? BrandingWorkflowResult { get; set; }

    public string Language { get; set; } = "pl";

    public string ThemePreference { get; set; } = "light";

    public bool ShowResult => !string.IsNullOrWhiteSpace(PngDataUri);

    public IReadOnlyList<QrDownloadFormat> Formats { get; } =
    [
        new("png", "PNG"),
        new("svg", "SVG"),
        new("txt", "TXT")
    ];

    public IReadOnlyList<QrErrorCorrectionOption> ErrorCorrectionOptions { get; } =
    [
        new("L", "L - niska (ok. 7%)"),
        new("M", "M - srednia (ok. 15%)"),
        new("Q", "Q - podwyzszona (ok. 25%)"),
        new("H", "H - wysoka (ok. 30%)")
    ];

    public IReadOnlyList<QrCameraProfileOption> CameraProfileOptions { get; } =
    [
        new("ideal", "Kamera flagowa"),
        new("standard", "Typowy telefon"),
        new("budget", "Slaba kamera")
    ];

    public IReadOnlyList<QrPrintFormatOption> PrintFormatOptions { get; } =
    [
        new("a4", "A4"),
        new("a5", "A5"),
        new("letter", "US Letter"),
        new("dl", "DL"),
        new("square", "Kwadrat 210 mm")
    ];

    public IReadOnlyList<QrGradientOption> PrintGradientOptions { get; } =
    [
        new("aurora", "Aurora"),
        new("sunset", "Sunset"),
        new("mint", "Mint"),
        new("graphite", "Graphite"),
        new("cmyk", "CMYK-safe")
    ];

    public IReadOnlyList<QrContentTypeOption> ContentTypeOptions { get; } =
    [
        new("general", "Ogolne"),
        new("url", "URL"),
        new("wifi", "WiFi"),
        new("vcard", "vCard"),
        new("email", "Email"),
        new("sms", "SMS"),
        new("phone", "Phone"),
        new("geo", "GEO"),
        new("event", "Event"),
        new("crypto", "Crypto Wallet"),
        new("app", "App Store"),
        new("pdf", "PDF"),
        new("social", "Social Media"),
        new("multilink", "Multi-link"),
        new("plain", "Plain Text")
    ];

    public IReadOnlyList<QrDesignGradientOption> QrDesignGradientOptions { get; } =
    [
        new("none", "Brak"),
        new("ocean", "Ocean"),
        new("sunrise", "Sunrise"),
        new("violet", "Violet"),
        new("forest", "Forest"),
        new("mono", "Mono")
    ];

    public IReadOnlyList<QrEyeStyleOption> EyeStyleOptions { get; } =
    [
        new("standard", "Standard"),
        new("rounded", "Rounded"),
        new("bold", "Bold"),
        new("outline", "Outline")
    ];

    public IReadOnlyList<QrFrameStyleOption> FrameStyleOptions { get; } =
    [
        new("none", "Brak"),
        new("soft", "Soft card"),
        new("ticket", "Ticket"),
        new("poster", "Poster"),
        new("badge", "Badge")
    ];

    public IReadOnlyList<QrDesignTemplateOption> DesignTemplateOptions { get; } =
    [
        new("clean", "Clean"),
        new("brand", "Brand"),
        new("event", "Event"),
        new("premium", "Premium"),
        new("sticker", "Sticker")
    ];
}

public record QrDownloadFormat(string Value, string Label);

public record QrErrorCorrectionOption(string Value, string Label);

public record QrCameraProfileOption(string Value, string Label);

public record QrPrintFormatOption(string Value, string Label);

public record QrGradientOption(string Value, string Label);

public record QrContentTypeOption(string Value, string Label);

public record QrDesignGradientOption(string Value, string Label);

public record QrEyeStyleOption(string Value, string Label);

public record QrFrameStyleOption(string Value, string Label);

public record QrDesignTemplateOption(string Value, string Label);

public record QrValidationResult(
    int ReadabilityScore,
    int PrintSafetyScore,
    bool IsReadable,
    string Verdict,
    int ModuleCount,
    string Recommendation);

public record PrintWorkflowResult(
    int EffectiveDpi,
    bool IsDpiSafe,
    bool IsCmykSafe,
    bool IsBleedSafe,
    string FormatLabel,
    string Recommendation);

public record BrandingWorkflowResult(
    int RiskScore,
    bool ShouldRunValidation,
    string Warning,
    string Recommendation);
