using VisionApi.Infrastructure.Queue;
using Xunit;

namespace VisionApi.Infrastructure.Tests;

// 作業佇列測試。
//
// 重點不是「能存能取」，而是併發與背壓行為 —— 
// 那才是換掉實作時最容易出錯的地方。
public class ChannelJobQueueTests
{
    [Fact]
    public async Task 排入後應能取出同一個作業()
    {
        var queue = new ChannelJobQueue();
        var jobId = Guid.NewGuid();

        await queue.EnqueueAsync(jobId);

        Assert.Equal(jobId, await queue.DequeueAsync());
    }

    [Fact]
    public async Task 應維持先進先出順序()
    {
        var queue = new ChannelJobQueue();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in ids) await queue.EnqueueAsync(id);

        foreach (var expected in ids)
            Assert.Equal(expected, await queue.DequeueAsync());
    }

    [Fact]
    public async Task 佇列為空時應等待而非立即返回()
    {
        // 工作者靠這個行為避免空轉輪詢
        var queue = new ChannelJobQueue();
        using var cts = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await queue.DequeueAsync(cts.Token));
    }

    [Fact]
    public async Task 取消時應正常結束而非卡住()
    {
        // 應用關閉時工作者要能乾淨退出
        var queue = new ChannelJobQueue();
        using var cts = new CancellationTokenSource();

        var dequeueTask = queue.DequeueAsync(cts.Token).AsTask();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dequeueTask);
    }

    [Fact]
    public async Task 多執行緒同時排入不應遺失作業()
    {
        var queue = new ChannelJobQueue(capacity: 200);
        var ids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();

        await Task.WhenAll(ids.Select(id => queue.EnqueueAsync(id).AsTask()));

        var received = new List<Guid>();
        for (var i = 0; i < ids.Count; i++) received.Add(await queue.DequeueAsync());

        Assert.Equal(ids.Count, received.Distinct().Count());
    }
}
