using VisionApi.Core.Enums;
using VisionApi.Core.Jobs;
using Xunit;

namespace VisionApi.Core.Tests;

// 狀態機測試。
//
// 這是整個系統唯一能用純函式表達的規則，也是最划算的測試 —— 
// 不需要資料庫、不需要 HTTP，毫秒級跑完，卻守住了核心不變式：
// 作業不會從終態回頭，也不會跳過中間狀態。
public class JobStateMachineTests
{
    [Theory]
    [InlineData(JobStatus.Pending, JobStatus.Processing)]     // 開始處理
    [InlineData(JobStatus.Processing, JobStatus.Done)]        // 成功
    [InlineData(JobStatus.Processing, JobStatus.Failed)]      // 失敗
    [InlineData(JobStatus.Processing, JobStatus.Pending)]     // 重試放回佇列
    public void 合法轉移應被允許(JobStatus from, JobStatus to)
    {
        Assert.True(JobStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(JobStatus.Pending, JobStatus.Done)]           // 不可跳過 Processing
    [InlineData(JobStatus.Pending, JobStatus.Failed)]
    [InlineData(JobStatus.Done, JobStatus.Processing)]        // 終態不可回頭
    [InlineData(JobStatus.Done, JobStatus.Failed)]
    [InlineData(JobStatus.Failed, JobStatus.Processing)]
    [InlineData(JobStatus.Failed, JobStatus.Done)]
    public void 非法轉移應被拒絕(JobStatus from, JobStatus to)
    {
        Assert.False(JobStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(JobStatus.Pending, JobStatus.Pending)]
    [InlineData(JobStatus.Processing, JobStatus.Processing)]
    [InlineData(JobStatus.Done, JobStatus.Done)]
    public void 原地轉移應被拒絕(JobStatus status, JobStatus same)
    {
        Assert.False(JobStateMachine.CanTransition(status, same));
    }

    [Theory]
    [InlineData(JobStatus.Done, true)]
    [InlineData(JobStatus.Failed, true)]
    [InlineData(JobStatus.Pending, false)]
    [InlineData(JobStatus.Processing, false)]
    public void 終態判定(JobStatus status, bool expected)
    {
        Assert.Equal(expected, JobStateMachine.IsTerminal(status));
    }
}
