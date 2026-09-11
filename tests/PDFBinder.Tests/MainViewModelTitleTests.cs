using System.ComponentModel;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="MainViewModel"/> のタイトルバー表示（DisplayFileName）に関する単体テスト
/// </summary>
public class MainViewModelTitleTests
{
    [Fact]
    public void DisplayFileName_WhenInitialState_ReturnsEmpty()
    {
        // Arrange & Act
        var vm = new MainViewModel();

        // Assert
        Assert.Equal(string.Empty, vm.DisplayFileName);
    }

    [Fact]
    public void DisplayFileName_WhenDocumentWithFilePathAssigned_ReturnsFileName()
    {
        // Arrange
        var vm = new MainViewModel();
        var doc = new PdfDocumentModel { FilePath = @"C:\TestFolder\SpecialReport.pdf" };

        // Act
        vm.Document = doc;

        // Assert
        Assert.Equal("SpecialReport.pdf", vm.DisplayFileName);
    }

    [Fact]
    public void DisplayFileName_WhenFilePathUpdated_NotifiesPropertyChanged()
    {
        // Arrange
        var vm = new MainViewModel();
        var propertyChangedList = new List<string?>();
        vm.PropertyChanged += (s, e) => propertyChangedList.Add(e.PropertyName);

        // Act
        vm.Document.FilePath = @"D:\Work\AnnualReview.pdf";

        // Assert
        Assert.Equal("AnnualReview.pdf", vm.DisplayFileName);
        Assert.Contains(nameof(MainViewModel.DisplayFileName), propertyChangedList);
    }

    [Fact]
    public void DisplayFileName_WhenDocumentReplaced_OldDocumentDoesNotTriggerEvent()
    {
        // Arrange
        var vm = new MainViewModel();
        var oldDoc = vm.Document;
        var newDoc = new PdfDocumentModel { FilePath = @"C:\Docs\NewFile.pdf" };
        vm.Document = newDoc;

        var notifiedProperties = new List<string?>();
        vm.PropertyChanged += (s, e) => notifiedProperties.Add(e.PropertyName);

        // Act: 旧ドキュメントの FilePath を変更
        oldDoc.FilePath = @"C:\Docs\OldFile.pdf";

        // Assert: 新ドキュメントの値が保持され、通知も発生しない
        Assert.Equal("NewFile.pdf", vm.DisplayFileName);
        Assert.DoesNotContain(nameof(MainViewModel.DisplayFileName), notifiedProperties);
    }
}
