using Microsoft.EntityFrameworkCore;
using ReleaseTicketing.Core.Data;
using ReleaseTicketing.Core.Domain;
using Xunit;

namespace ReleaseTicketing.Tests;

/// <summary>
/// Needs a REAL SQL Server (row locks behave differently in SQLite). Not run by default:
///   set RT_TEST_SQL="Server=...;Database=ReleaseTicketing_Tests;..."   then   dotnet test --filter Category=SqlServer
/// Normal runs: dotnet test --filter "Category!=SqlServer"
/// </summary>
[Trait("Category", "SqlServer")]
public class ConcurrencyTests
{
    [Fact]
    public async Task Simultaneous_interest_from_two_clusters_keeps_both_allocation_tickets_independent()
    {
        var cs = Environment.GetEnvironmentVariable("RT_TEST_SQL")
                 ?? throw new InvalidOperationException("Set RT_TEST_SQL to a SQL Server connection string to run this test.");
        using var h = new Harness(() => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options));
        var t = await h.CreateTicketAsync();
        int ev2, ev3;
        await using (var db = h.DbFactory())
        {
            ev2 = (await db.ClusterEvaluations.SingleAsync(e => e.ReleaseRequestId == t.Id && e.ClusterId == h.Ey2)).EvaluationId;
            ev3 = (await db.ClusterEvaluations.SingleAsync(e => e.ReleaseRequestId == t.Id && e.ClusterId == h.Ey3)).EvaluationId;
        }

        var gate = new TaskCompletionSource();
        async Task Go(int cluster, int ev) { await gate.Task; await h.As(h.Manager(cluster)).Eval.RespondAsync(t.Id, ev, ClusterResponse.YesInterested, default); }
        var tasks = new[] { Task.Run(() => Go(h.Ey2, ev2)), Task.Run(() => Go(h.Ey3, ev3)) };
        gate.SetResult();
        await Task.WhenAll(tasks);

        await using var check = h.DbFactory();
        var evals = await check.ClusterEvaluations.Where(e => e.ReleaseRequestId == t.Id && e.Response != null).ToListAsync();
        Assert.Equal([1, 2], evals.Select(e => e.ResponseSequence!.Value).OrderBy(x => x).ToArray());
        Assert.Equal(2, evals.Count(e => e.Status == EvaluationStatus.Interested));
        Assert.DoesNotContain(evals, e => e.Status == EvaluationStatus.Assigned);
    }

    [Fact]
    public async Task Simultaneous_selection_by_two_clusters_has_exactly_one_winner()
    {
        var cs = Environment.GetEnvironmentVariable("RT_TEST_SQL")
                 ?? throw new InvalidOperationException("Set RT_TEST_SQL to a SQL Server connection string to run this test.");
        using var h = new Harness(() => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options));
        var ticket = await h.CreateTicketAsync();

        async Task<ClusterEvaluation> Evaluation(int cluster)
        {
            await using var db = h.DbFactory();
            return await db.ClusterEvaluations.SingleAsync(e => e.ReleaseRequestId == ticket.Id && e.ClusterId == cluster);
        }

        var e2 = await Evaluation(h.Ey2);
        var e3 = await Evaluation(h.Ey3);
        await h.As(h.Manager(h.Ey2)).Eval.RespondAsync(ticket.Id, e2.EvaluationId, ClusterResponse.YesInterested, default);
        await h.As(h.Manager(h.Ey3)).Eval.RespondAsync(ticket.Id, e3.EvaluationId, ClusterResponse.YesInterested, default);
        foreach (var (cluster, evaluation) in new[] { (h.Ey2, e2), (h.Ey3, e3) })
        {
            var service = h.As(h.Manager(cluster)).Eval;
            await service.UpdateAllocationStatusAsync(ticket.Id, evaluation.EvaluationId,
                new ReleaseTicketing.Core.Dtos.UpdateAllocationStatusDto { Status = ReleaseTicketing.Core.Domain.AllocationStatus.InterviewScheduled }, default);
            await service.UpdateAllocationStatusAsync(ticket.Id, evaluation.EvaluationId,
                new ReleaseTicketing.Core.Dtos.UpdateAllocationStatusDto { Status = ReleaseTicketing.Core.Domain.AllocationStatus.WaitingForResults }, default);
        }

        var gate = new TaskCompletionSource();
        async Task<bool> TrySelect(int cluster, int evaluationId)
        {
            await gate.Task;
            try
            {
                await h.As(h.Manager(cluster)).Eval.UpdateAllocationStatusAsync(ticket.Id, evaluationId,
                    new ReleaseTicketing.Core.Dtos.UpdateAllocationStatusDto { Status = ReleaseTicketing.Core.Domain.AllocationStatus.Selected }, default);
                return true;
            }
            catch (Exception ex) when (ex is ReleaseTicketing.Core.Exceptions.BusinessRuleException or ReleaseTicketing.Core.Exceptions.ConflictException)
            {
                return false;
            }
        }

        var selections = new[] { Task.Run(() => TrySelect(h.Ey2, e2.EvaluationId)), Task.Run(() => TrySelect(h.Ey3, e3.EvaluationId)) };
        gate.SetResult();
        Assert.Single(await Task.WhenAll(selections), selected => selected);

        await using var result = h.DbFactory();
        var allocations = await result.ClusterEvaluations.Where(e => e.ReleaseRequestId == ticket.Id).ToListAsync();
        Assert.Single(allocations, e => e.Status == EvaluationStatus.Selected);
        Assert.Equal(3, allocations.Count(e => e.Status == EvaluationStatus.Closed));
    }
}
