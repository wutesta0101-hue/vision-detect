namespace VisionApi.Core.Abstractions;

// 待處理作業的佇列。
//
// 為什麼只放 Guid 而不是整筆紀錄：
//   佇列只需要知道「哪一筆要處理」，實際資料在資料庫裡。
//   這樣即使佇列中的項目過時，工作者讀到的也一定是最新狀態。
//
// 目前的實作是行程內佇列（Channel），不能在重啟後存活，
// 也無法跨副本分派 —— 這個限制寫在 README 的「已知限制」。
// 換成持久化訊息代理時，只要替換實作，上層不用改。
public interface IJobQueue
{
    // 把作業排入佇列
    ValueTask EnqueueAsync(Guid jobId, CancellationToken ct = default);

    // 取出下一個作業。佇列為空時會等待，直到有項目或被取消。
    ValueTask<Guid> DequeueAsync(CancellationToken ct = default);
}
