using VisionApi.Core.Enums;

namespace VisionApi.Core.Jobs;

// 作業狀態轉移的唯一定義。
//
// 為什麼要集中：狀態轉移若散落在 controller、worker、repository，
// 就無法寫出「非法轉移應被拒絕」的測試，也很難確認終態不會被覆寫。
//
// 這是純函式，不依賴任何框架，測試跑得極快。
public static class JobStateMachine
{
    // 判斷某個狀態轉移是否合法
    public static bool CanTransition(JobStatus from, JobStatus to) => (from, to) switch
    {
        (JobStatus.Pending, JobStatus.Processing) => true,
        (JobStatus.Processing, JobStatus.Done) => true,
        (JobStatus.Processing, JobStatus.Failed) => true,
        (JobStatus.Processing, JobStatus.Pending) => true,  // 重試：放回佇列
        _ => false
    };

    // 終態不可再轉移。用於防止已完成的作業被後續事件覆寫。
    public static bool IsTerminal(JobStatus status) =>
        status is JobStatus.Done or JobStatus.Failed;
}
