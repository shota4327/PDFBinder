using PDFBinder.Core.Models;

namespace PDFBinder.Core.Services;

/// <summary>
/// アンドゥ・リドゥ履歴を管理するサービス実装
/// </summary>
public class UndoRedoService : IUndoRedoService
{
    private readonly Stack<IUndoableCommand> _undoStack = new();
    private readonly Stack<IUndoableCommand> _redoStack = new();
    private const int MaxHistoryCount = 100;

    /// <inheritdoc/>
    public bool CanUndo => _undoStack.Count > 0;

    /// <inheritdoc/>
    public bool CanRedo => _redoStack.Count > 0;

    /// <inheritdoc/>
    public event EventHandler? StateChanged;

    /// <inheritdoc/>
    public void Execute(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Execute();

        _undoStack.Push(command);
        _redoStack.Clear();

        if (_undoStack.Count > MaxHistoryCount)
        {
            var items = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = items.Length - 2; i >= 0; i--)
            {
                _undoStack.Push(items[i]);
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Undo()
    {
        if (!CanUndo) return;

        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Redo()
    {
        if (!CanRedo) return;

        var command = _redoStack.Pop();
        command.Execute();
        _undoStack.Push(command);

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// ページの回転を元に戻す/やり直すコマンド
/// </summary>
public class RotatePageCommand : IUndoableCommand
{
    private readonly PdfPageModel _page;
    private readonly PageRotation _oldRotation;
    private readonly PageRotation _newRotation;

    public string Description => "ページの回転";

    public RotatePageCommand(PdfPageModel page, PageRotation oldRotation, PageRotation newRotation)
    {
        _page = page;
        _oldRotation = oldRotation;
        _newRotation = newRotation;
    }

    public void Execute() => _page.RotateTo(_newRotation);
    public void Undo() => _page.RotateTo(_oldRotation);
}

/// <summary>
/// ページの並び替えを元に戻す/やり直すコマンド
/// </summary>
public class MovePageCommand : IUndoableCommand
{
    private readonly PdfDocumentModel _doc;
    private readonly int _oldIndex;
    private readonly int _newIndex;

    public string Description => "ページの並び替え";

    public MovePageCommand(PdfDocumentModel doc, int oldIndex, int newIndex)
    {
        _doc = doc;
        _oldIndex = oldIndex;
        _newIndex = newIndex;
    }

    public void Execute() => _doc.MovePage(_oldIndex, _newIndex);
    public void Undo() => _doc.MovePage(_newIndex, _oldIndex);
}

/// <summary>
/// ページの削除を元に戻す/やり直すコマンド
/// </summary>
public class RemovePageCommand : IUndoableCommand
{
    private readonly PdfDocumentModel _doc;
    private readonly PdfPageModel _page;
    private readonly int _index;

    public string Description => "ページの削除";

    public RemovePageCommand(PdfDocumentModel doc, PdfPageModel page, int index)
    {
        _doc = doc;
        _page = page;
        _index = index;
    }

    public void Execute() => _doc.RemovePage(_page);
    public void Undo() => _doc.InsertPage(_index, _page);
}

/// <summary>
/// ページの挿入を元に戻す/やり直すコマンド
/// </summary>
public class InsertPageCommand : IUndoableCommand
{
    private readonly PdfDocumentModel _doc;
    private readonly PdfPageModel _page;
    private readonly int _index;

    public string Description => "ページの追加";

    public InsertPageCommand(PdfDocumentModel doc, PdfPageModel page, int index)
    {
        _doc = doc;
        _page = page;
        _index = index;
    }

    public void Execute() => _doc.InsertPage(_index, _page);
    public void Undo() => _doc.RemovePage(_page);
}

/// <summary>
/// 複数のコマンドを一括してアンドゥ・リドゥする複合コマンド
/// </summary>
public class CompositeUndoableCommand : IUndoableCommand
{
    private readonly List<IUndoableCommand> _commands;
    public string Description { get; }

    public CompositeUndoableCommand(IEnumerable<IUndoableCommand> commands, string description)
    {
        _commands = commands.ToList();
        Description = description;
    }

    public void Execute()
    {
        foreach (var cmd in _commands)
        {
            cmd.Execute();
        }
    }

    public void Undo()
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
        {
            _commands[i].Undo();
        }
    }
}

/// <summary>
/// 複数ページの並び替えを元に戻す/やり直すコマンド
/// </summary>
public class ReorderPagesCommand : IUndoableCommand
{
    private readonly PdfDocumentModel _doc;
    private readonly List<PdfPageModel> _oldOrder;
    private readonly List<PdfPageModel> _newOrder;

    public string Description => "ページの並び替え";

    public ReorderPagesCommand(PdfDocumentModel doc, IEnumerable<PdfPageModel> oldOrder, IEnumerable<PdfPageModel> newOrder)
    {
        _doc = doc;
        _oldOrder = oldOrder.ToList();
        _newOrder = newOrder.ToList();
    }

    public void Execute() => ApplyOrder(_newOrder);
    public void Undo() => ApplyOrder(_oldOrder);

    private void ApplyOrder(List<PdfPageModel> targetOrder)
    {
        for (int i = 0; i < targetOrder.Count; i++)
        {
            var item = targetOrder[i];
            int currentIdx = _doc.Pages.IndexOf(item);
            if (currentIdx >= 0 && currentIdx != i)
            {
                _doc.Pages.Move(currentIdx, i);
            }
        }
    }
}

/// <summary>
/// 複数ページの挿入を一括して元に戻す/やり直すコマンド
/// </summary>
public class InsertPagesCommand : IUndoableCommand
{
    private readonly PdfDocumentModel _doc;
    private readonly List<PdfPageModel> _pages;
    private readonly int _startIndex;

    public string Description => "ページの挿入";

    public InsertPagesCommand(PdfDocumentModel doc, IEnumerable<PdfPageModel> pages, int startIndex)
    {
        _doc = doc;
        _pages = pages.ToList();
        _startIndex = startIndex;
    }

    public void Execute()
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            _doc.InsertPage(_startIndex + i, _pages[i]);
        }
    }

    public void Undo()
    {
        foreach (var page in _pages)
        {
            _doc.RemovePage(page);
        }
    }
}

