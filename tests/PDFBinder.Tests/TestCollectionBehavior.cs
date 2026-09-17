using Xunit;

// Docnet / PDFium のネイティブCライブラリによる並列実行時のリソース競合・クラッシュを防止するため、並列実行を無効化
[assembly: CollectionBehavior(DisableTestParallelization = true)]
