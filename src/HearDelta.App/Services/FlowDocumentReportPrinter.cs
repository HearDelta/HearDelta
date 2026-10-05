using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using HearDelta.App.Controls;
using HearDelta.App.ViewModels;

namespace HearDelta.App.Services;

/// <summary>
/// Druckt Berichte über den Windows-Druckdialog (auch „Microsoft Print to PDF“). Diagramme werden als
/// Vektorgrafik mit demselben Steuerelement wie am Bildschirm gezeichnet.
/// </summary>
public sealed class FlowDocumentReportPrinter : IReportPrinter
{
    private const double PagePadding = 56d;
    // Eingefroren, damit die geteilten Brushes von jedem UI-Thread aus nutzbar sind.
    private static readonly Brush Ink = Frozen(Color.FromRgb(26, 26, 26));
    private static readonly Brush Muted = Frozen(Color.FromRgb(90, 90, 90));
    private static readonly Brush Warning = Frozen(Color.FromRgb(138, 90, 0));
    private static readonly Brush Rule = Frozen(Color.FromRgb(190, 190, 190));
    private static readonly FontFamily Font = new("Segoe UI");

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public bool Print(PrintReport report)
    {
        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        if (dialog.ShowDialog() != true)
            return false;

        var document = CreateDocument(report, new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight));
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, report.Title);
        return true;
    }

    public static FlowDocument CreateDocument(PrintReport report, Size pageSize)
    {
        var contentWidth = pageSize.Width - 2 * PagePadding;
        var document = new FlowDocument
        {
            PageWidth = pageSize.Width,
            PageHeight = pageSize.Height,
            PagePadding = new Thickness(PagePadding),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = Font,
            FontSize = 12.5d,
            Foreground = Ink,
            Background = Brushes.White,
            TextAlignment = TextAlignment.Left
        };

        document.Blocks.Add(new Paragraph(new Run(report.Title)) { FontSize = 21d, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0) });
        document.Blocks.Add(new Paragraph(new Run(report.Subtitle)) { Foreground = Muted, Margin = new Thickness(0, 2, 0, 12) });
        foreach (var block in report.Blocks)
            document.Blocks.Add(CreateBlock(block, contentWidth));
        return document;
    }

    private static Block CreateBlock(PrintBlock block, double contentWidth) => block switch
    {
        PrintHeading heading => new Paragraph(new Run(heading.Text))
        {
            FontSize = 15d,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 6),
            KeepWithNext = true
        },
        PrintParagraph paragraph => CreateParagraph(paragraph),
        PrintFacts facts => CreateFacts(facts),
        PrintTable table => CreateTable(table),
        PrintThresholdChart chart => CreateChart(chart, contentWidth),
        _ => throw new NotSupportedException(block.GetType().Name)
    };

    private static Paragraph CreateParagraph(PrintParagraph paragraph) => new(new Run(paragraph.Text))
    {
        Margin = new Thickness(0, 6, 0, 6),
        FontSize = paragraph.Style == PrintTextStyle.Muted ? 10.5d : 12.5d,
        Foreground = paragraph.Style switch
        {
            PrintTextStyle.Muted => Muted,
            PrintTextStyle.Warning => Warning,
            _ => Ink
        },
        FontWeight = paragraph.Style == PrintTextStyle.Emphasis ? FontWeights.SemiBold : FontWeights.Normal
    };

    private static Table CreateFacts(PrintFacts facts)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 6) };
        // Feste Spaltenbreiten verteilt die FlowDocument-Tabelle nicht zuverlässig; daher relative Breiten.
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(3.4, GridUnitType.Star) });
        var group = new TableRowGroup();
        foreach (var fact in facts.Items.Where(item => !string.IsNullOrWhiteSpace(item.Value)))
        {
            var row = new TableRow();
            row.Cells.Add(new TableCell(new Paragraph(new Run(fact.Label)) { Foreground = Muted, Margin = new Thickness(0, 1, 8, 1) }));
            row.Cells.Add(new TableCell(new Paragraph(new Run(fact.Value)) { Margin = new Thickness(0, 1, 0, 1) }));
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        return table;
    }

    private static Table CreateTable(PrintTable source)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 4), FontSize = 11.5d };
        for (var index = 0; index < source.Headers.Count; index++)
            table.Columns.Add(new TableColumn
            {
                Width = new GridLength(source.RelativeWidths?.ElementAtOrDefault(index) is double width and > 0 ? width : 1d, GridUnitType.Star)
            });

        var header = new TableRowGroup();
        var headerRow = new TableRow { FontWeight = FontWeights.SemiBold };
        foreach (var text in source.Headers)
            headerRow.Cells.Add(CreateCell(text, new Thickness(0, 0, 0, 1.2)));
        header.Rows.Add(headerRow);
        table.RowGroups.Add(header);

        var body = new TableRowGroup();
        foreach (var values in source.Rows)
        {
            var row = new TableRow();
            foreach (var text in values)
                row.Cells.Add(CreateCell(text, new Thickness(0, 0, 0, 0.5)));
            body.Rows.Add(row);
        }
        table.RowGroups.Add(body);
        return table;
    }

    private static TableCell CreateCell(string text, Thickness border) => new(new Paragraph(new Run(text)) { Margin = new Thickness(0) })
    {
        BorderBrush = Rule,
        BorderThickness = border,
        Padding = new Thickness(0, 3, 8, 3)
    };

    private static BlockUIContainer CreateChart(PrintThresholdChart chart, double contentWidth)
    {
        var control = new HearingThresholdChart
        {
            Ear = chart.Ear,
            MaximumAttenuationDbfs = chart.MaximumAttenuationDbfs,
            Width = contentWidth,
            Height = Math.Round(contentWidth * 0.56d),
            FontFamily = Font
        };
        if (chart.Series.Count > 0)
            control.Series = chart.Series;
        else
            control.ItemsSource = chart.Rows;
        return new BlockUIContainer(control) { Margin = new Thickness(0, 8, 0, 8) };
    }
}
