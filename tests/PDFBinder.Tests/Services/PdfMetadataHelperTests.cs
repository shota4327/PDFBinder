using System.IO;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PDFBinder.Tests.Services;

/// <summary>
/// <see cref="PDFBinder.Core.Helpers.PdfMetadataHelper"/> および PDF保存時のメタデータ・レイヤー・しおり保持の単体テスト
/// </summary>
public class PdfMetadataHelperTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly PdfService _service;

    public PdfMetadataHelperTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"PDFBinder_MetadataTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _service = new PdfService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    public static string FindRepoRoot()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "PDFBinder.sln")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir)!;
        }
        return dir;
    }

    [Fact]
    public async Task SaveDocumentAsync_SingleFile_PreservesDocumentInfoAndViewerPreferences()
    {
        // Arrange
        string sourcePdf = Path.Combine(_testDirectory, "source_info.pdf");
        using (var doc = new PdfDocument())
        {
            doc.Info.Title = "Original Title";
            doc.Info.Author = "Original Author";
            doc.Info.Subject = "Original Subject";
            doc.Info.Keywords = "PDF, Binder, Test";
            doc.Info.Creator = "Test Creator App";

            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(500);
            page.Height = XUnit.FromPoint(700);

            var vpref = new PdfDictionary(doc);
            vpref.Elements.SetName("/Direction", "/R2L");
            doc.Internals.Catalog.Elements["/ViewerPreferences"] = vpref;
            doc.Internals.Catalog.Elements.SetName("/PageLayout", "/OneColumn");

            doc.Save(sourcePdf);
        }

        string outputPdf = Path.Combine(_testDirectory, "output_info.pdf");
        var model = await _service.LoadDocumentAsync(sourcePdf);

        // Act
        await _service.SaveDocumentAsync(model, outputPdf);

        // Assert
        using var reloaded = PdfReader.Open(outputPdf, PdfDocumentOpenMode.Import);
        Assert.Equal("Original Title", reloaded.Info.Title);
        Assert.Equal("Original Author", reloaded.Info.Author);
        Assert.Equal("Original Subject", reloaded.Info.Subject);
        Assert.Equal("PDF, Binder, Test", reloaded.Info.Keywords);
        Assert.Equal("Test Creator App", reloaded.Info.Creator);

        var vprefReloaded = reloaded.Internals.Catalog.Elements.GetDictionary("/ViewerPreferences");
        Assert.NotNull(vprefReloaded);
        Assert.Equal("/R2L", vprefReloaded.Elements.GetName("/Direction"));
        Assert.Equal("/OneColumn", reloaded.Internals.Catalog.Elements.GetName("/PageLayout"));
    }

    [Fact]
    public async Task SaveDocumentAsync_MultiFile_ClearsDocumentInfo_PreservesFirstViewerPreferences()
    {
        // Arrange
        string pdf1 = Path.Combine(_testDirectory, "doc1.pdf");
        using (var doc = new PdfDocument())
        {
            doc.Info.Title = "Doc 1 Title";
            doc.Info.Author = "Doc 1 Author";
            var page = doc.AddPage();
            var vpref = new PdfDictionary(doc);
            vpref.Elements.SetName("/Direction", "/R2L");
            doc.Internals.Catalog.Elements["/ViewerPreferences"] = vpref;
            doc.Save(pdf1);
        }

        string pdf2 = Path.Combine(_testDirectory, "doc2.pdf");
        using (var doc = new PdfDocument())
        {
            doc.Info.Title = "Doc 2 Title";
            doc.Info.Author = "Doc 2 Author";
            doc.AddPage();
            doc.Save(pdf2);
        }

        var model = await _service.LoadDocumentAsync(pdf1);
        await _service.AppendDocumentAsync(model, pdf2);

        string outputPdf = Path.Combine(_testDirectory, "output_multi.pdf");

        // Act
        await _service.SaveDocumentAsync(model, outputPdf);

        // Assert
        using var reloaded = PdfReader.Open(outputPdf, PdfDocumentOpenMode.Import);
        // Multi-file: Title and Author are cleared to avoid conflicting metadata
        Assert.Empty(reloaded.Info.Title);
        Assert.Empty(reloaded.Info.Author);

        // First document's ViewerPreferences (R2L) are preserved
        var vprefReloaded = reloaded.Internals.Catalog.Elements.GetDictionary("/ViewerPreferences");
        Assert.NotNull(vprefReloaded);
        Assert.Equal("/R2L", vprefReloaded.Elements.GetName("/Direction"));
    }

    [Fact]
    public async Task SaveDocumentAsync_WithOutlines_PreservesAndRemapsHierarchy()
    {
        // Arrange
        string sourcePdf = Path.Combine(_testDirectory, "source_outlines.pdf");
        using (var doc = new PdfDocument())
        {
            var p1 = doc.AddPage();
            var p2 = doc.AddPage();
            var p3 = doc.AddPage();

            var ch1 = doc.Outlines.Add("Chapter 1", p1, true, PdfOutlineStyle.Bold);
            ch1.Outlines.Add("Section 1.1", p2);
            doc.Outlines.Add("Chapter 2", p3);

            doc.Save(sourcePdf);
        }

        var model = await _service.LoadDocumentAsync(sourcePdf);
        // Swap page 0 and page 1 (p2 becomes first, p1 becomes second)
        model.MovePage(0, 1);

        string outputPdf = Path.Combine(_testDirectory, "output_outlines.pdf");

        // Act
        await _service.SaveDocumentAsync(model, outputPdf);

        // Assert
        using var reloaded = PdfReader.Open(outputPdf, PdfDocumentOpenMode.Import);
        Assert.Equal(2, reloaded.Outlines.Count);

        var outCh1 = reloaded.Outlines[0];
        Assert.Equal("Chapter 1", outCh1.Title);
        Assert.Equal(PdfOutlineStyle.Bold, outCh1.Style);
        // p1 was moved to index 1
        Assert.Same(reloaded.Pages[1], outCh1.DestinationPage);

        // Section 1.1 was p2, which moved to index 0
        Assert.Single(outCh1.Outlines);
        Assert.Equal("Section 1.1", outCh1.Outlines[0].Title);
        Assert.Same(reloaded.Pages[0], outCh1.Outlines[0].DestinationPage);

        // Chapter 2 was p3, still at index 2
        var outCh2 = reloaded.Outlines[1];
        Assert.Equal("Chapter 2", outCh2.Title);
        Assert.Same(reloaded.Pages[2], outCh2.DestinationPage);
    }

    [Fact]
    public async Task SaveDocumentAsync_SyntheticPdf_PreservesOCPropertiesOffLayer()
    {
        string samplePdf = Path.Combine(_testDirectory, "synthetic_layer.pdf");
        using (var doc = new PdfDocument())
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(500);
            page.Height = XUnit.FromPoint(700);

            var ocg = new PdfDictionary(doc);
            ocg.Elements.SetName("/Type", "/OCG");
            ocg.Elements.SetString("/Name", "DraftLayer");
            doc.Internals.AddObject(ocg);

            var ocProps = new PdfDictionary(doc);
            var defaultD = new PdfDictionary(doc);
            var offArray = new PdfArray(doc);
            var ocgRef = ocg.Reference!;
            offArray.Elements.Add(ocgRef);
            defaultD.Elements["/OFF"] = offArray;
            ocProps.Elements["/D"] = defaultD;

            var ocgsArray = new PdfArray(doc);
            ocgsArray.Elements.Add(ocgRef);
            ocProps.Elements["/OCGs"] = ocgsArray;
            doc.Internals.Catalog.Elements["/OCProperties"] = ocProps;

            var res = new PdfDictionary(doc);
            var props = new PdfDictionary(doc);
            props.Elements["/MC0"] = ocgRef;
            res.Elements["/Properties"] = props;
            page.Elements["/Resources"] = res;

            doc.Save(samplePdf);
        }

        var model = await _service.LoadDocumentAsync(samplePdf);
        string outputPdf = Path.Combine(_testDirectory, "saved_synthetic_layer.pdf");

        // Act
        await _service.SaveDocumentAsync(model, outputPdf);

        // Assert
        using var reloaded = PdfReader.Open(outputPdf, PdfDocumentOpenMode.Import);
        var catalog = reloaded.Internals.Catalog;
        Assert.True(catalog.Elements.ContainsKey("/OCProperties"));

        var reloadedOcProps = catalog.Elements.GetDictionary("/OCProperties");
        Assert.NotNull(reloadedOcProps);
        var defaultDict = reloadedOcProps.Elements.GetDictionary("/D");
        Assert.NotNull(defaultDict);

        var reloadedOffArray = defaultDict.Elements.GetArray("/OFF");
        Assert.NotNull(reloadedOffArray);
        Assert.NotEmpty(reloadedOffArray.Elements);

        var page1 = reloaded.Pages[0];
        var resources = page1.Elements.GetDictionary("/Resources");
        Assert.NotNull(resources);
        var propsDict = resources.Elements.GetDictionary("/Properties");
        Assert.NotNull(propsDict);
        var mc0Ref = propsDict.Elements["/MC0"] as PdfSharp.Pdf.Advanced.PdfReference;
        Assert.NotNull(mc0Ref);

        bool isMc0Off = reloadedOffArray.Elements.OfType<PdfSharp.Pdf.Advanced.PdfReference>()
            .Any(r => r.ObjectID == mc0Ref.ObjectID);
        Assert.True(isMc0Off, "Page 1's /MC0 layer must be included in /OFF array");
    }
}
