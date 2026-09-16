using System.Diagnostics;
using Google.OrTools.Sat;

namespace SeminarSched.Optimization.Core;

public sealed class CpSatScheduleSolver
{
    public async Task<ScheduleSolution> SolveAsync(ScheduleProblem problem, TimeSpan maximumDuration, int randomSeed=1, CancellationToken cancellationToken=default)
    {
        if(maximumDuration<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        return await Task.Run(()=>Solve(problem,maximumDuration,randomSeed,cancellationToken),cancellationToken);
    }
    private static ScheduleSolution Solve(ScheduleProblem problem,TimeSpan maximum,int seed,CancellationToken token)
    {
        var watch=Stopwatch.StartNew();var model=new CpModel();var variables=problem.Candidates.Select((candidate,index)=>(candidate,variable:model.NewBoolVar($"x_{index}"))).ToArray();
        foreach(var group in variables.GroupBy(x=>x.candidate.RequestId)){var demand=problem.Demands.Single(d=>d.RequestId==group.Key);model.Add(LinearExpr.Sum(group.Select(x=>x.variable))<=Math.Max(0,demand.RequiredSessions-demand.AlreadyFixedSessions));}
        foreach(var group in variables.GroupBy(x=>(x.candidate.StudentId,x.candidate.OpenDateId,x.candidate.TimeSlotId)))model.Add(LinearExpr.Sum(group.Select(x=>x.variable))<=1);
        foreach(var group in variables.GroupBy(x=>(x.candidate.TeacherId,x.candidate.OpenDateId,x.candidate.TimeSlotId)))model.Add(LinearExpr.Sum(group.Select(x=>x.variable))<=1);
        model.Maximize(LinearExpr.Sum(variables.Select(x=>x.variable)));
        var solver=new CpSolver{StringParameters=$"max_time_in_seconds:{maximum.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} random_seed:{seed} num_search_workers:0"};
        using var registration=token.Register(solver.StopSearch);var status=solver.Solve(model);token.ThrowIfCancellationRequested();if(status is not CpSolverStatus.Optimal and not CpSolverStatus.Feasible)throw new InvalidOperationException($"時間割を作成できませんでした: {status}");
        var placements=variables.Where(x=>solver.BooleanValue(x.variable)).Select(x=>new SchedulePlacement(x.candidate.RequestId,x.candidate.StudentId,x.candidate.TeacherId,x.candidate.OpenDateId,x.candidate.TimeSlotId)).ToArray();
        var unassigned=problem.Demands.Sum(d=>Math.Max(0,d.RequiredSessions-d.AlreadyFixedSessions-placements.Count(x=>x.RequestId==d.RequestId)));var solution=new ScheduleSolution(placements,unassigned,-placements.Length,watch.Elapsed);ScheduleSolutionValidator.Validate(problem,solution);return solution;
    }
}
