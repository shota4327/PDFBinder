namespace PDFBinder.Core.Services;

/// <summary>
/// アンドゥ（取り消し）可能なコマンドのインターフェース
/// </summary>
public interface IUndoableCommand
{
    /// <summary>コマンドの説明</summary>
    string Description { get; }

    /// <summary>コマンドを実行します。</summary>
    void Execute();

    /// <summary>コマンドを取り消します。</summary>
    void Undo();
}

/// <summary>
/// アンドゥ・リドゥ履歴を管理するサービスインターフェース
/// </summary>
public interface IUndoRedoService
{
    /// <summary>アンドゥ可能かどうか</summary>
    bool CanUndo { get; }

    /// <summary>リドゥ可能かどうか</summary>
    bool CanRedo { get; }

    /// <summary>アンドゥスタックが変更された時のイベント</summary>
    event EventHandler? StateChanged;

    /// <summary>コマンドを実行し、アンドゥスタックに登録します。</summary>
    void Execute(IUndoableCommand command);

    /// <summary>直前の操作を取り消します。</summary>
    void Undo();

    /// <summary>取り消した操作をやり直します。</summary>
    void Redo();

    /// <summary>履歴をすべてクリアします。</summary>
    void Clear();
}
