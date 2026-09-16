namespace SeminarSched.Optimization.Core;

public static class ScheduleSolutionValidator
{
    public static void Validate(ScheduleProblem problem, ScheduleSolution solution)
    {
        var allowed=problem.Candidates.ToHashSet();
        foreach(var p in solution.Placements)
            if(!allowed.Contains(new PlacementCandidate(p.RequestId,p.StudentId,p.TeacherId,p.OpenDateId,p.TimeSlotId))) throw new InvalidDataException("Solver returned a placement outside the candidate set.");
        if(solution.Placements.GroupBy(x=>(x.StudentId,x.OpenDateId,x.TimeSlotId)).Any(g=>g.Count()>1))throw new InvalidDataException("Student collision detected.");
        if(solution.Placements.GroupBy(x=>(x.TeacherId,x.OpenDateId,x.TimeSlotId)).Any(g=>g.Count()>1))throw new InvalidDataException("Teacher collision detected.");
        foreach(var demand in problem.Demands){var count=solution.Placements.Count(x=>x.RequestId==demand.RequestId)+demand.AlreadyFixedSessions;if(count>demand.RequiredSessions)throw new InvalidDataException("A lesson request was over-assigned.");}
        var expected=problem.Demands.Sum(d=>Math.Max(0,d.RequiredSessions-d.AlreadyFixedSessions-solution.Placements.Count(x=>x.RequestId==d.RequestId)));if(expected!=solution.UnassignedLessons)throw new InvalidDataException("Unassigned lesson count is inconsistent.");
    }
}
