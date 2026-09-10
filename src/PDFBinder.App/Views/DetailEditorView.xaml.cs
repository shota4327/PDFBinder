using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using PDFBinder.App.Controls;
using PDFBinder.App.ViewModels;

namespace PDFBinder.App.Views;

/// <summary>
/// 手書き編集を行う詳細ビューのコードビハインド
/// </summary>
public partial class DetailEditorView : UserControl
{
    public DetailEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private DetailEditorViewModel? ViewModel => DataContext as DetailEditorViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is DetailEditorViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is DetailEditorViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            BindPageStrokes(newVm);
            ApplyDrawingAttributes(newVm);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel == null) return;

        if (e.PropertyName == nameof(DetailEditorViewModel.CurrentPage))
        {
            BindPageStrokes(ViewModel);
        }
        else if (e.PropertyName is nameof(DetailEditorViewModel.SelectedColor) or
                                   nameof(DetailEditorViewModel.StrokeThickness) or
                                   nameof(DetailEditorViewModel.SelectedTool))
        {
            ApplyDrawingAttributes(ViewModel);
        }
    }

    private void BindPageStrokes(DetailEditorViewModel vm)
    {
        InkCanvas.Strokes = vm.CurrentPage.InkStrokes;
    }

    private void ApplyDrawingAttributes(DetailEditorViewModel vm)
    {
        var attr = new DrawingAttributes
        {
            Color = vm.SelectedColor,
            Width = vm.StrokeThickness,
            Height = vm.StrokeThickness,
            FitToCurve = true,
            IsHighlighter = vm.SelectedTool == EditorToolMode.Highlighter
        };

        InkCanvas.DefaultDrawingAttributes = attr;
    }
}
