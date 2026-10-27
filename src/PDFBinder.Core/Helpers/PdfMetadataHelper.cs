using System.Reflection;
using PDFBinder.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace PDFBinder.Core.Helpers;

/// <summary>
/// PDFの再構築・保存時に、元ドキュメントのメタデータ（レイヤー設定、しおり、文書情報、表示設定など）の
/// 継承および統合（マージ）を支援するヘルパークラス
/// </summary>
internal static class PdfMetadataHelper
{
    private const BindingFlags PrivateFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>
    /// 保存対象ページ群および元ドキュメント群からメタデータを抽出し、出力ドキュメントへ適用します。
    /// </summary>
    public static void TransferMetadata(
        PdfDocument outputDoc,
        IReadOnlyList<PdfPageModel> pages,
        Dictionary<string, PdfDocument> sourceCache)
    {
        ArgumentNullException.ThrowIfNull(outputDoc);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(sourceCache);

        var distinctSourcePaths = pages
            .Where(p => !p.IsBlankPage && !string.IsNullOrEmpty(p.SourceFilePath))
            .Select(p => p.SourceFilePath!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctSourcePaths.Count == 0) return;

        bool isSingleSource = distinctSourcePaths.Count == 1;
        var firstDoc = sourceCache.Values.FirstOrDefault();

        // 1. 文書情報（/Info）の引き継ぎ（単一ファイル時のみ）
        if (isSingleSource && firstDoc != null)
        {
            TransferDocumentInfo(outputDoc, firstDoc);
        }

        // 2. 初期表示設定（/ViewerPreferences 等）の引き継ぎ
        if (firstDoc != null)
        {
            TransferViewerPreferences(outputDoc, firstDoc);
        }

        // 3. レイヤー設定（/OCProperties）の引き継ぎ・マージ
        TransferLayerProperties(outputDoc, pages, sourceCache, isSingleSource);

        // 4. しおり・目次（/Outlines）の追従・再構築
        TransferOutlines(outputDoc, pages, sourceCache);
    }

    /// <summary>
    /// 単一元ドキュメントの文書情報（/Info）を出力ドキュメントへコピーします。
    /// </summary>
    private static void TransferDocumentInfo(PdfDocument outputDoc, PdfDocument sourceDoc)
    {
        var srcInfo = sourceDoc.Info;
        var dstInfo = outputDoc.Info;

        if (!string.IsNullOrEmpty(srcInfo.Title)) dstInfo.Title = srcInfo.Title;
        if (!string.IsNullOrEmpty(srcInfo.Author)) dstInfo.Author = srcInfo.Author;
        if (!string.IsNullOrEmpty(srcInfo.Subject)) dstInfo.Subject = srcInfo.Subject;
        if (!string.IsNullOrEmpty(srcInfo.Keywords)) dstInfo.Keywords = srcInfo.Keywords;
        if (!string.IsNullOrEmpty(srcInfo.Creator)) dstInfo.Creator = srcInfo.Creator;

        if (srcInfo.CreationDate != DateTime.MinValue)
        {
            dstInfo.CreationDate = srcInfo.CreationDate;
        }
        dstInfo.ModificationDate = DateTime.Now;
    }

    /// <summary>
    /// ビューワー環境設定や初期表示形式（見開き、右開き、言語等）を出力ドキュメントへコピーします。
    /// </summary>
    private static void TransferViewerPreferences(PdfDocument outputDoc, PdfDocument sourceDoc)
    {
        var srcCatalog = sourceDoc.Internals.Catalog;
        var dstCatalog = outputDoc.Internals.Catalog;

        CopyCatalogElementIfPresent(srcCatalog, dstCatalog, "/ViewerPreferences");
        CopyCatalogElementIfPresent(srcCatalog, dstCatalog, "/PageLayout");
        CopyCatalogElementIfPresent(srcCatalog, dstCatalog, "/PageMode");
        CopyCatalogElementIfPresent(srcCatalog, dstCatalog, "/Lang");
    }

    /// <summary>
    /// カタログ内の指定キーの要素をディープコピーしてコピー先カタログへ設定します。
    /// </summary>
    private static void CopyCatalogElementIfPresent(PdfDictionary srcCatalog, PdfDictionary dstCatalog, string key)
    {
        if (!srcCatalog.Elements.ContainsKey(key)) return;

        var element = srcCatalog.Elements[key];
        if (element is PdfReference pref && pref.Value is PdfObject obj)
        {
            dstCatalog.Elements[key] = obj.Clone();
        }
        else if (element != null)
        {
            dstCatalog.Elements[key] = element.Clone();
        }
    }

    /// <summary>
    /// レイヤー設定（/OCProperties）を出力ドキュメントへ適用（単一時は完全複製、複数時はマージ）します。
    /// </summary>
    private static void TransferLayerProperties(
        PdfDocument outputDoc,
        IReadOnlyList<PdfPageModel> pages,
        Dictionary<string, PdfDocument> sourceCache,
        bool isSingleSource)
    {
        if (isSingleSource)
        {
            var firstDoc = sourceCache.Values.FirstOrDefault();
            if (firstDoc == null) return;

            var srcOcProps = firstDoc.Internals.Catalog.Elements.GetDictionary("/OCProperties");
            if (srcOcProps == null) return;

            var lookup = GetLookupFunc(outputDoc, firstDoc);
            var cloned = CloneAndRemapItem(srcOcProps, outputDoc, lookup);
            if (cloned != null)
            {
                outputDoc.Internals.Catalog.Elements["/OCProperties"] = cloned;
            }
        }
        else
        {
            MergeMultiSourceLayers(outputDoc, sourceCache);
        }
    }

    /// <summary>
    /// 複数ドキュメントのレイヤー設定（OCGおよび非表示リスト）をマージして適用します。
    /// </summary>
    private static void MergeMultiSourceLayers(PdfDocument outputDoc, Dictionary<string, PdfDocument> sourceCache)
    {
        var mergedOcgs = new PdfArray(outputDoc);
        var mergedOff = new PdfArray(outputDoc);
        var mergedOrder = new PdfArray(outputDoc);
        bool hasAnyLayer = false;

        foreach (var sourceDoc in sourceCache.Values)
        {
            var srcOcProps = sourceDoc.Internals.Catalog.Elements.GetDictionary("/OCProperties");
            if (srcOcProps == null) continue;

            hasAnyLayer = true;
            var lookup = GetLookupFunc(outputDoc, sourceDoc);

            MergeArrayElements(srcOcProps.Elements.GetArray("/OCGs"), mergedOcgs, outputDoc, lookup);

            var defaultDict = srcOcProps.Elements.GetDictionary("/D");
            if (defaultDict != null)
            {
                MergeArrayElements(defaultDict.Elements.GetArray("/OFF"), mergedOff, outputDoc, lookup);
                MergeArrayElements(defaultDict.Elements.GetArray("/Order"), mergedOrder, outputDoc, lookup);
            }
        }

        if (!hasAnyLayer) return;

        var ocProps = new PdfDictionary(outputDoc);
        var defaultD = new PdfDictionary(outputDoc);
        if (mergedOff.Elements.Count > 0) defaultD.Elements["/OFF"] = mergedOff;
        if (mergedOrder.Elements.Count > 0) defaultD.Elements["/Order"] = mergedOrder;

        ocProps.Elements["/D"] = defaultD;
        if (mergedOcgs.Elements.Count > 0) ocProps.Elements["/OCGs"] = mergedOcgs;

        outputDoc.Internals.Catalog.Elements["/OCProperties"] = ocProps;
    }

    /// <summary>
    /// ソース配列の要素をリマップしながらターゲット配列へ追加します（重複排除付き）。
    /// </summary>
    private static void MergeArrayElements(
        PdfArray? sourceArray,
        PdfArray targetArray,
        PdfDocument targetDoc,
        Func<PdfObjectID, PdfReference?> lookup)
    {
        if (sourceArray == null) return;

        for (int i = 0; i < sourceArray.Elements.Count; i++)
        {
            var remapped = CloneAndRemapItem(sourceArray.Elements[i], targetDoc, lookup);
            if (remapped is PdfReference pref)
            {
                bool exists = targetArray.Elements.OfType<PdfReference>().Any(r => r.ObjectID == pref.ObjectID);
                if (!exists) targetArray.Elements.Add(pref);
            }
            else if (remapped != null)
            {
                targetArray.Elements.Add(remapped);
            }
        }
    }

    /// <summary>
    /// 元ドキュメントのしおり（/Outlines）構造を解析し、新ドキュメントのページ位置に追従して再構築します。
    /// </summary>
    private static void TransferOutlines(
        PdfDocument outputDoc,
        IReadOnlyList<PdfPageModel> pages,
        Dictionary<string, PdfDocument> sourceCache)
    {
        // (SourceFilePath, OriginalPageIndex) -> 新ドキュメントのページインデックス
        var pageMapping = new Dictionary<(string FilePath, int PageIndex), int>();
        for (int newIdx = 0; newIdx < pages.Count; newIdx++)
        {
            var page = pages[newIdx];
            if (page.IsBlankPage || string.IsNullOrEmpty(page.SourceFilePath)) continue;

            var key = (page.SourceFilePath, page.OriginalPageIndex);
            pageMapping.TryAdd(key, newIdx);
        }

        foreach (var sourceDoc in sourceCache.Values)
        {
            if (sourceDoc.Outlines == null || sourceDoc.Outlines.Count == 0) continue;

            string? srcPath = GetSourcePathForDoc(sourceDoc, pages);
            if (string.IsNullOrEmpty(srcPath)) continue;

            foreach (PdfOutline srcOutline in sourceDoc.Outlines)
            {
                CloneOutlineRecursive(srcOutline, outputDoc.Outlines, outputDoc, sourceDoc, srcPath, pageMapping);
            }
        }
    }

    /// <summary>
    /// しおり項目を再帰的に複製し、ジャンプ先ページを新ドキュメントのページへリマッピングします。
    /// </summary>
    private static void CloneOutlineRecursive(
        PdfOutline srcOutline,
        PdfOutlineCollection targetCollection,
        PdfDocument outputDoc,
        PdfDocument sourceDoc,
        string srcPath,
        Dictionary<(string FilePath, int PageIndex), int> pageMapping)
    {
        int targetPageIndex = ResolveOutlineTargetPageIndex(srcOutline, sourceDoc, srcPath, pageMapping);
        PdfPage? targetPage = (targetPageIndex >= 0 && targetPageIndex < outputDoc.PageCount)
            ? outputDoc.Pages[targetPageIndex]
            : null;

        bool hasChildren = srcOutline.Outlines != null && srcOutline.Outlines.Count > 0;
        if (targetPage == null && !hasChildren) return;

        bool isOpened = hasChildren;
        PdfOutline newOutline = (targetPage != null)
            ? targetCollection.Add(srcOutline.Title, targetPage, isOpened, srcOutline.Style)
            : targetCollection.Add(srcOutline.Title, outputDoc.Pages[0], isOpened, srcOutline.Style);

        if (srcOutline.TextColor != XColor.Empty)
        {
            newOutline.TextColor = srcOutline.TextColor;
        }

        if (hasChildren)
        {
            foreach (PdfOutline child in srcOutline.Outlines!)
            {
                CloneOutlineRecursive(child, newOutline.Outlines, outputDoc, sourceDoc, srcPath, pageMapping);
            }
        }
    }

    /// <summary>
    /// しおり項目のリンク先ページ番号を元ドキュメントとマッピング情報から解決します。
    /// </summary>
    private static int ResolveOutlineTargetPageIndex(
        PdfOutline outline,
        PdfDocument sourceDoc,
        string srcPath,
        Dictionary<(string FilePath, int PageIndex), int> pageMapping)
    {
        var destPage = outline.DestinationPage;
        if (destPage == null) return -1;

        for (int i = 0; i < sourceDoc.PageCount; i++)
        {
            if (sourceDoc.Pages[i] == destPage)
            {
                if (pageMapping.TryGetValue((srcPath, i), out int newIndex))
                {
                    return newIndex;
                }
                break;
            }
        }
        return -1;
    }

    /// <summary>
    /// ページリストから該当の元ドキュメントのファイルパスを逆引きします。
    /// </summary>
    private static string? GetSourcePathForDoc(PdfDocument doc, IReadOnlyList<PdfPageModel> pages)
    {
        foreach (var page in pages)
        {
            if (!page.IsBlankPage && !string.IsNullOrEmpty(page.SourceFilePath))
            {
                if (doc.PageCount > page.OriginalPageIndex && page.OriginalPageIndex >= 0)
                {
                    return page.SourceFilePath;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 出力ドキュメントのインポート済みオブジェクトテーブルからオブジェクトID解決関数を取得します。
    /// </summary>
    private static Func<PdfObjectID, PdfReference?> GetLookupFunc(PdfDocument outputDoc, PdfDocument sourceDoc)
    {
        var formTableField = typeof(PdfDocument).GetField("_formTable", PrivateFlags);
        var formTable = formTableField?.GetValue(outputDoc);
        if (formTable == null || sourceDoc.PageCount == 0) return _ => null;

        var getTableMethod = formTable.GetType().GetMethod("GetImportedObjectTable", PrivateFlags, null, new[] { typeof(PdfPage) }, null);
        var importedTable = getTableMethod?.Invoke(formTable, new object[] { sourceDoc.Pages[0] });
        if (importedTable == null) return _ => null;

        var containsMethod = importedTable.GetType().GetMethod("Contains", PrivateFlags);
        var itemProp = importedTable.GetType().GetProperty("Item", PrivateFlags);
        if (containsMethod == null || itemProp == null) return _ => null;

        return id =>
        {
            bool has = (bool)containsMethod.Invoke(importedTable, new object[] { id })!;
            return has ? (PdfReference?)itemProp.GetValue(importedTable, new object[] { id }) : null;
        };
    }

    /// <summary>
    /// PDFアイテムをターゲットドキュメント向けに再帰的に複製し、インポート済みIDへリマップします。
    /// </summary>
    private static PdfItem? CloneAndRemapItem(PdfItem? item, PdfDocument targetDoc, Func<PdfObjectID, PdfReference?> lookup)
    {
        if (item == null) return null;

        if (item is PdfReference pref)
        {
            var mapped = lookup(pref.ObjectID);
            if (mapped != null) return mapped;

            if (pref.Value is PdfObject obj)
            {
                var clonedVal = CloneAndRemapItem(obj, targetDoc, lookup);
                if (clonedVal is PdfObject newObj)
                {
                    targetDoc.Internals.AddObject(newObj);
                    return newObj.Reference;
                }
            }
            return pref;
        }

        if (item is PdfDictionary dict)
        {
            var newDict = new PdfDictionary(targetDoc);
            foreach (var key in dict.Elements.Keys)
            {
                var val = CloneAndRemapItem(dict.Elements[key], targetDoc, lookup);
                if (val != null) newDict.Elements[key] = val;
            }
            return newDict;
        }

        if (item is PdfArray arr)
        {
            var newArr = new PdfArray(targetDoc);
            for (int i = 0; i < arr.Elements.Count; i++)
            {
                var val = CloneAndRemapItem(arr.Elements[i], targetDoc, lookup);
                if (val != null) newArr.Elements.Add(val);
            }
            return newArr;
        }

        return item.Clone();
    }
}
