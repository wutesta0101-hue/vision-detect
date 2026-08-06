namespace VisionApi.Core.Enums;

// 一次辨識作業的生命週期狀態。
// 合法的轉移規則定義在 JobStateMachine，不要散落在各處判斷。
public enum JobStatus
{
    Pending,      // 已收到上傳，等待處理
    Processing,   // 正在呼叫模型服務
    Done,         // 成功，終態
    Failed        // 重試耗盡或不可重試的錯誤，終態
}
