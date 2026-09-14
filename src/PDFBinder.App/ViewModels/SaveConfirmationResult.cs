namespace PDFBinder.App.ViewModels;

/// <summary>
/// 未保存の変更が存在する場合の保存確認ダイアログでのユーザー選択結果
/// </summary>
public enum SaveConfirmationResult
{
    /// <summary>変更内容を保存して処理を続行</summary>
    Save,

    /// <summary>変更内容を破棄して処理を続行</summary>
    Discard,

    /// <summary>処理を中断し、現在の状態を維持</summary>
    Cancel
}
