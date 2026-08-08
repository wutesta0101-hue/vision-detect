namespace VisionCapture.Controls;

// 圓形準心疊層。
//
// 用途是取景引導 —— 提示使用者把物件對準中央再拍。
// 這不是即時偵測框（那需要端上推論），而是構圖輔助：
// 置中的物件在後續辨識時通常有更好的結果。
//
// 用 GraphicsView 自繪而非圖片，好處是任何解析度都清晰，
// 也方便之後調整顏色或加上狀態變化。
public class CrosshairDrawable : IDrawable
{
    // 圓的直徑佔畫面較短邊的比例
    private const float CircleRatio = 0.62f;

    // 十字線長度佔半徑的比例
    private const float TickRatio = 0.18f;

    public void Draw(ICanvas canvas, RectF rect)
    {
        var centerX = rect.Center.X;
        var centerY = rect.Center.Y;
        var radius = Math.Min(rect.Width, rect.Height) * CircleRatio / 2f;

        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 2.5f;
        canvas.Alpha = 0.85f;

        // 外圈
        canvas.DrawCircle(centerX, centerY, radius);

        // 中央十字。四段短線而非完整十字，
        // 中間留空才不會擋住要拍的物件。
        var tick = radius * TickRatio;
        var gap = tick * 0.6f;

        canvas.DrawLine(centerX - gap - tick, centerY, centerX - gap, centerY);  // 左
        canvas.DrawLine(centerX + gap, centerY, centerX + gap + tick, centerY);  // 右
        canvas.DrawLine(centerX, centerY - gap - tick, centerX, centerY - gap);  // 上
        canvas.DrawLine(centerX, centerY + gap, centerX, centerY + gap + tick);  // 下

        // 四角的短弧，強化「這是取景框」的視覺提示
        canvas.StrokeSize = 4f;
        var arcSize = radius * 0.5f;
        var box = new RectF(centerX - radius, centerY - radius, radius * 2, radius * 2);

        canvas.DrawArc(box, 45, 30, false, false);     // 右上
        canvas.DrawArc(box, 135, 30, false, false);    // 左上
        canvas.DrawArc(box, 225, 30, false, false);    // 左下
        canvas.DrawArc(box, 315, 30, false, false);    // 右下
    }
}
