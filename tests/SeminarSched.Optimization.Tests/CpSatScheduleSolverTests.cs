using SeminarSched.Optimization.Core;

namespace SeminarSched.Optimization.Tests;

public sealed class CpSatScheduleSolverTests
{
    [Fact]
    public async Task SolveAsync_AssignsAllLessonsWithoutStudentOrTeacherCollision()
    {
        var problem=new ScheduleProblem(
            [new LessonDemand(1,10,1,0),new LessonDemand(2,20,1,0)],
            [new PlacementCandidate(1,10,100,1,1),new PlacementCandidate(1,10,100,1,2),new PlacementCandidate(2,20,100,1,1),new PlacementCandidate(2,20,100,1,2)]);
        var solution=await new CpSatScheduleSolver().SolveAsync(problem,TimeSpan.FromSeconds(5));
        Assert.Equal(2,solution.Placements.Count);Assert.Equal(0,solution.UnassignedLessons);ScheduleSolutionValidator.Validate(problem,solution);
    }
    [Fact]
    public async Task SolveAsync_NoCandidates_ReportsUnassigned()
    {
        var problem=new ScheduleProblem([new LessonDemand(1,10,3,1)],[]);
        var solution=await new CpSatScheduleSolver().SolveAsync(problem,TimeSpan.FromSeconds(1));
        Assert.Equal(2,solution.UnassignedLessons);Assert.Empty(solution.Placements);
    }
    [Fact]
    public void Validator_RejectsCandidateOutsideInput()
    {
        var problem=new ScheduleProblem([new LessonDemand(1,10,1,0)],[]);var solution=new ScheduleSolution([new SchedulePlacement(1,10,1,1,1)],0,0,TimeSpan.Zero);
        Assert.Throws<InvalidDataException>(()=>ScheduleSolutionValidator.Validate(problem,solution));
    }
}
