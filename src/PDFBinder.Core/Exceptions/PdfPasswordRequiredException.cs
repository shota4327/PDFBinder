namespace PDFBinder.Core.Exceptions;

/// <summary>
/// パスワードで保護されたPDFを開くためにパスワードが必要、または入力されたパスワードが誤っている場合にスローされる例外
/// </summary>
public class PdfPasswordRequiredException : Exception
{
    /// <summary>
    /// 対象のPDFファイルパスを取得します。
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// 入力されたパスワードが誤っていたか（再入力要求か）どうかを取得します。
    /// </summary>
    public bool IsInvalidPassword { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="filePath">対象のファイルパス</param>
    /// <param name="isInvalidPassword">パスワード誤りによる再試行フラグ</param>
    /// <param name="innerException">内部例外</param>
    public PdfPasswordRequiredException(
        string message,
        string? filePath = null,
        bool isInvalidPassword = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FilePath = filePath;
        IsInvalidPassword = isInvalidPassword;
    }
}
