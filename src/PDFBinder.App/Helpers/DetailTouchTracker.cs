using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PDFBinder.App.ViewModels;

namespace PDFBinder.App.Helpers;

/// <summary>
/// タッチジェスチャーの種別
/// </summary>
public enum TouchGestureType
{
    /// <summary>ジェスチャーなし／待機中</summary>
    None,
    /// <summary>1本指ドラッグによるパン（スクロール）</summary>
    Pan,
    /// <summary>2本指ピンチによるズームおよび中心追従</summary>
    Zoom
}

/// <summary>
/// タッチ移動処理の計算結果
/// </summary>
/// <param name="GestureType">検出されたジェスチャー種別</param>
/// <param name="DeltaX">水平スクロール変化量（ピクセル）</param>
/// <param name="DeltaY">垂直スクロール変化量（ピクセル）</param>
/// <param name="NewZoom">目標ズーム倍率</param>
/// <param name="TargetHorizontalOffset">目標水平スクロール位置</param>
/// <param name="TargetVerticalOffset">目標垂直スクロール位置</param>
public readonly record struct TouchMoveResult(
    TouchGestureType GestureType,
    double DeltaX,
    double DeltaY,
    double NewZoom,
    double TargetHorizontalOffset,
    double TargetVerticalOffset)
{
    /// <summary>何もしない空の結果</summary>
    public static TouchMoveResult None => default;

    /// <summary>パンスクロール結果</summary>
    public static TouchMoveResult Pan(double dx, double dy) =>
        new(TouchGestureType.Pan, dx, dy, 0, 0, 0);

    /// <summary>ピンチズーム結果</summary>
    public static TouchMoveResult Zoom(double newZoom, double hOffset, double vOffset) =>
        new(TouchGestureType.Zoom, 0, 0, newZoom, hOffset, vOffset);
}

/// <summary>
/// 詳細ビューポート全域（ページおよび周囲余白）におけるタッチパン・ピンチズーム・パームリジェクションを統括するトラッカー
/// </summary>
public class DetailTouchTracker
{
    /// <summary>微小な手の震えや接地ブレによる誤スクロールを防ぐ閾値（ピクセル）</summary>
    public const double TouchSlopThreshold = 8.0;

    /// <summary>スタイラス離脱後のタッチ抑止クールダウン時間（ミリ秒）</summary>
    public const int StylusSuppressionCooldownMs = 150;

    private readonly Dictionary<int, Point> _activeTouchPoints = new();
    private readonly Dictionary<int, Point> _touchStartPoints = new();

    private bool _isPanningStarted;
    private double? _initialPinchDistance;
    private Point? _lastPinchCenter;

    private bool _isStylusTouching;
    private bool _isStylusInRange;
    private DateTime _lastStylusActivityTime = DateTime.MinValue;

    /// <summary>現在追跡中のアクティブなタッチ点数</summary>
    public int ActiveTouchCount => _activeTouchPoints.Count;

    /// <summary>パン（スクロール）状態が開始されているか</summary>
    public bool IsPanningStarted => _isPanningStarted;

    /// <summary>
    /// 指定されたタッチデバイスIDが現在追跡対象か返します。
    /// </summary>
    public bool IsActiveTouch(int touchId) => _activeTouchPoints.ContainsKey(touchId);

    #region スタイラス・パームリジェクション制御

    /// <summary>
    /// スタイラスペンが接地・近接（ホバー）中、またはクールダウン中か判定し、タッチ操作を抑止すべきか返します。
    /// </summary>
    public bool IsStylusSuppressed()
    {
        if (_isStylusTouching || _isStylusInRange) return true;
        return (DateTime.UtcNow - _lastStylusActivityTime).TotalMilliseconds < StylusSuppressionCooldownMs;
    }

    /// <summary>
    /// スタイラスの接地・近接状態を更新します。
    /// </summary>
    public void NotifyStylusActivity(bool isTouching, bool isInRange)
    {
        _isStylusTouching = isTouching;
        _isStylusInRange = isInRange;
        _lastStylusActivityTime = DateTime.UtcNow;

        if (isTouching || isInRange)
        {
            PurgeActiveTouches();
        }
    }

    /// <summary>
    /// スタイラス検知時などに、登録されているすべてのタッチ状態を即座に破棄します。
    /// </summary>
    public void PurgeActiveTouches()
    {
        _activeTouchPoints.Clear();
        _touchStartPoints.Clear();
        _isPanningStarted = false;
        _initialPinchDistance = null;
        _lastPinchCenter = null;
    }

    /// <summary>
    /// テスト用にスタイラス状態を直接設定します。
    /// </summary>
    internal void SetStylusStateForTesting(bool isTouching, bool isInRange, DateTime? lastActivityTime = null)
    {
        _isStylusTouching = isTouching;
        _isStylusInRange = isInRange;
        _lastStylusActivityTime = lastActivityTime ?? DateTime.UtcNow;
    }

    #endregion

    #region タッチイベント処理

    /// <summary>
    /// 新規タッチ接触を登録します。スタイラス抑止中の場合は登録を拒否します。
    /// </summary>
    /// <returns>登録に成功した場合は true、スタイラス抑止などで無視された場合は false</returns>
    public bool OnTouchDown(int touchId, Point pos)
    {
        if (IsStylusSuppressed())
        {
            return false;
        }

        _activeTouchPoints[touchId] = pos;
        _touchStartPoints[touchId] = pos;

        // タッチ点数の変化時はピンチ基準点およびパンスロップ状態をリセット
        _isPanningStarted = false;
        _initialPinchDistance = null;
        _lastPinchCenter = null;

        return true;
    }

    /// <summary>
    /// タッチ座標移動時のパンまたはピンチズーム計算を実行します。
    /// </summary>
    public TouchMoveResult OnTouchMove(
        int touchId,
        Point newPos,
        double currentZoom,
        double minZoom,
        double maxZoom,
        double horizontalOffset,
        double verticalOffset)
    {
        if (IsStylusSuppressed())
        {
            PurgeActiveTouches();
            return TouchMoveResult.None;
        }

        if (!_activeTouchPoints.ContainsKey(touchId))
        {
            return TouchMoveResult.None;
        }

        if (_activeTouchPoints.Count == 1)
        {
            return ProcessOneFingerPan(touchId, newPos);
        }

        if (_activeTouchPoints.Count >= 2)
        {
            _activeTouchPoints[touchId] = newPos;
            return ProcessTwoFingerPinchZoom(currentZoom, minZoom, maxZoom, horizontalOffset, verticalOffset);
        }

        return TouchMoveResult.None;
    }

    /// <summary>
    /// 1本指ドラッグ時のスロップ判定とパンスクロール変位計算
    /// </summary>
    private TouchMoveResult ProcessOneFingerPan(int touchId, Point newPos)
    {
        if (!_isPanningStarted)
        {
            if (_touchStartPoints.TryGetValue(touchId, out Point startPos))
            {
                Vector diff = newPos - startPos;
                if (diff.Length < TouchSlopThreshold)
                {
                    return TouchMoveResult.None;
                }

                _isPanningStarted = true;
                _activeTouchPoints[touchId] = newPos;
            }
            return TouchMoveResult.None;
        }

        if (_activeTouchPoints.TryGetValue(touchId, out Point oldPos))
        {
            double deltaX = oldPos.X - newPos.X;
            double deltaY = oldPos.Y - newPos.Y;
            _activeTouchPoints[touchId] = newPos;
            return TouchMoveResult.Pan(deltaX, deltaY);
        }

        return TouchMoveResult.None;
    }

    /// <summary>
    /// 2本指ピンチ時のズーム倍率およびオフセット計算
    /// </summary>
    private TouchMoveResult ProcessTwoFingerPinchZoom(
        double currentZoom,
        double minZoom,
        double maxZoom,
        double horizontalOffset,
        double verticalOffset)
    {
        var points = _activeTouchPoints.Values.Take(2).ToArray();
        Point p1 = points[0];
        Point p2 = points[1];

        double currentDistance = (p1 - p2).Length;
        Point currentCenter = new((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0);

        if (_initialPinchDistance == null || _lastPinchCenter == null)
        {
            _initialPinchDistance = currentDistance;
            _lastPinchCenter = currentCenter;
            return TouchMoveResult.None;
        }

        var result = PinchZoomHelper.Calculate(
            currentZoom,
            _initialPinchDistance.Value,
            currentDistance,
            _lastPinchCenter.Value,
            currentCenter,
            horizontalOffset,
            verticalOffset,
            minZoom,
            maxZoom);

        _initialPinchDistance = currentDistance;
        _lastPinchCenter = currentCenter;

        return TouchMoveResult.Zoom(result.NewZoom, result.TargetHorizontalOffset, result.TargetVerticalOffset);
    }

    /// <summary>
    /// タッチ離脱時の状態解除
    /// </summary>
    public void OnTouchUp(int touchId)
    {
        _activeTouchPoints.Remove(touchId);
        _touchStartPoints.Remove(touchId);

        if (_activeTouchPoints.Count < 2)
        {
            _initialPinchDistance = null;
            _lastPinchCenter = null;
        }

        if (_activeTouchPoints.Count == 0)
        {
            _isPanningStarted = false;
        }
    }

    #endregion
}
