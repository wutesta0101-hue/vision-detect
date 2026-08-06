using System.Threading.Channels;
using VisionApi.Core.Abstractions;

namespace VisionApi.Infrastructure.Queue;

// 用 System.Threading.Channels 實作的行程內佇列。
//
// 為什麼用 Channel 而不是 ConcurrentQueue：
//   Channel 內建「佇列為空時非同步等待」的能力，
//   工作者不需要自己輪詢，也不會空轉吃 CPU。
//
// 容量上限的用途是背壓（back-pressure）：
//   佇列滿時 EnqueueAsync 會等待而非無限堆積，
//   避免上傳速度遠快於推論速度時把記憶體吃光。
public class ChannelJobQueue : IJobQueue
{
    private readonly Channel<Guid> _channel;

    public ChannelJobQueue(int capacity = 100)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait   // 滿了就等，不丟棄作業
        };
        _channel = Channel.CreateBounded<Guid>(options);
    }

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(jobId, ct);

    public ValueTask<Guid> DequeueAsync(CancellationToken ct = default) =>
        _channel.Reader.ReadAsync(ct);
}
