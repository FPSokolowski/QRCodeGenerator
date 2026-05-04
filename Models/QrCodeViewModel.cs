using System.ComponentModel.DataAnnotations;

namespace QRCodeGenerator.Models;

public class QrCodeViewModel
{
    [Required(ErrorMessage = "Wpisz tekst do zakodowania.")]
    [StringLength(1000, ErrorMessage = "Tekst moze miec maksymalnie 1000 znakow.")]
    [Display(Name = "Tekst")]
    public string? Text { get; set; }

    public string? PngDataUri { get; set; }

    public bool ShowResult => !string.IsNullOrWhiteSpace(PngDataUri);

    public IReadOnlyList<QrDownloadFormat> Formats { get; } =
    [
        new("png", "PNG"),
        new("svg", "SVG"),
        new("txt", "TXT")
    ];
}

public record QrDownloadFormat(string Value, string Label);
