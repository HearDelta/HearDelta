using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HearDelta.App.Services;
using HearDelta.App.ViewModels;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class FlowDocumentReportPrinterTests
{
    private static readonly Size A4 = new(793.7d, 1122.5d);

    [Fact]
    public void ReportIsPaginatedOnA4WithTheChartDrawnOnTheFirstPage()
    {
        RunOnStaThread(() =>
        {
            var rows = new[]
            {
                new HearingThresholdResultRow(250d, -62m, true, "250 Hz", "−62 dBFS"),
                new HearingThresholdResultRow(500d, -70m, true, "500 Hz", "−70 dBFS"),
                new HearingThresholdResultRow(1_000d, -66m, true, "1 kHz", "−66 dBFS"),
                new HearingThresholdResultRow(4_000d, null, false, "4 kHz", "nicht gehört bis −6 dBFS")
            };
            var report = new PrintReport(
                "Ohne Hörgerät",
                "Hörschwellentest vom 03.09.2026 · 10:00",
                [
                    new PrintFacts([new("Person", "Testperson"), new("Ohr", "Rechtes Ohr"), new("Kommentar", null), new("Messaufbau", "Test-DAC + Testkopfhörer · Lautsprecher (TOPPING USB DAC) · 48.000 Hz · 24 bit")]),
                    new PrintThresholdChart(TestedEar.Right, rows, [], -6d),
                    new PrintHeading("Hörschwellen je Frequenz"),
                    new PrintTable(["Frequenz", "Hörschwelle"], rows.Select(row => (IReadOnlyList<string>)[row.Frequency, row.Threshold]).ToArray()),
                    new PrintParagraph(PrintReport.Disclaimer, PrintTextStyle.Muted)
                ]);

            var document = FlowDocumentReportPrinter.CreateDocument(report, A4);
            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
            paginator.ComputePageCount();

            Assert.Equal(1, paginator.PageCount);
            var page = paginator.GetPage(0);
            Assert.Equal(A4, page.Size);

            var bitmap = new RenderTargetBitmap((int)A4.Width, (int)A4.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(page.Visual);
            SavePreview(bitmap);

            // Die Kurve des rechten Ohrs ist rot; ohne gezeichnetes Diagramm gäbe es keine roten Pixel.
            var pixels = new byte[(int)A4.Width * (int)A4.Height * 4];
            bitmap.CopyPixels(pixels, (int)A4.Width * 4, 0);
            var redPixels = 0;
            for (var index = 0; index < pixels.Length; index += 4)
            {
                if (pixels[index + 2] > 150 && pixels[index + 1] < 110 && pixels[index] < 110)
                    redPixels++;
            }
            Assert.True(redPixels > 200, $"Nur {redPixels} rote Pixel gefunden.");
        });
    }

    [Fact]
    public void EmptyFactsAreLeftOut()
    {
        RunOnStaThread(() =>
        {
            var report = new PrintReport("Titel", "Untertitel", [new PrintFacts([new("Kommentar", " "), new("Ohr", "Linkes Ohr")])]);

            var document = FlowDocumentReportPrinter.CreateDocument(report, A4);

            var facts = Assert.IsType<Table>(document.Blocks.ElementAt(2));
            Assert.Single(facts.RowGroups[0].Rows);
        });
    }

    /// <summary>Speichert die erste Seite als PNG, wenn <c>WV_PRINT_PREVIEW_DIR</c> gesetzt ist.</summary>
    private static void SavePreview(BitmapSource bitmap)
    {
        var directory = Environment.GetEnvironmentVariable("WV_PRINT_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return;
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, "print-preview.png"));
        encoder.Save(stream);
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
