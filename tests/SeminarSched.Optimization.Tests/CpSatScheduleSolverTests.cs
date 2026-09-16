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

    [Fact]
    public async Task SolveAsync_TeacherCanTeachTwoStudentsUnlessOneToOneIsRequired()
    {
        var pairable = new ScheduleProblem(
            [new LessonDemand(1, 10, 1, 0), new LessonDemand(2, 20, 1, 0)],
            [new PlacementCandidate(1, 10, 100, 1, 1), new PlacementCandidate(2, 20, 100, 1, 1)]);
        var paired = await new CpSatScheduleSolver().SolveAsync(pairable, TimeSpan.FromSeconds(2));
        Assert.Equal(2, paired.Placements.Count);

        var oneToOne = new ScheduleProblem(
            [new LessonDemand(1, 10, 1, 0, OneToOneRequired: true), new LessonDemand(2, 20, 1, 0)],
            [new PlacementCandidate(1, 10, 100, 1, 1, OneToOneRequired: true), new PlacementCandidate(2, 20, 100, 1, 1)]);
        var separated = await new CpSatScheduleSolver().SolveAsync(oneToOne, TimeSpan.FromSeconds(2));
        Assert.Single(separated.Placements);
        Assert.Equal(1, separated.UnassignedLessons);
    }

    [Fact]
    public async Task SolveAsync_PreservesRegularTeacherMinimumAndPrefersBetterTeacher()
    {
        var demand = new LessonDemand(1, 10, 4, 0, RegularTeacherId: 100, RegularTeacherPriority: 3);
        var candidates = Enumerable.Range(1, 4)
            .SelectMany(day => new[]
            {
                new PlacementCandidate(1, 10, 100, day, 1, day, 1, PreferencePenalty: 0),
                new PlacementCandidate(1, 10, 200, day, 1, day, 1, PreferencePenalty: 5),
            })
            .ToArray();

        var solution = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem([demand], candidates), TimeSpan.FromSeconds(2));

        Assert.Equal(4, solution.Placements.Count);
        Assert.True(solution.Placements.Count(item => item.TeacherId == 100) >= 2);
        Assert.All(solution.Placements, item => Assert.Equal(100, item.TeacherId));
    }

    [Fact]
    public async Task SolveAsync_RespectsMaximumConsecutiveSlots()
    {
        var problem = new ScheduleProblem(
            [new LessonDemand(1, 10, 3, 0, MaxConsecutiveSlots: 2)],
            [
                new PlacementCandidate(1, 10, 100, 1, 1, SlotOrder: 1),
                new PlacementCandidate(1, 10, 100, 1, 2, SlotOrder: 2),
                new PlacementCandidate(1, 10, 100, 1, 3, SlotOrder: 3),
            ]);

        var solution = await new CpSatScheduleSolver().SolveAsync(problem, TimeSpan.FromSeconds(2));

        Assert.Equal(2, solution.Placements.Count);
        Assert.Equal(1, solution.UnassignedLessons);
    }

    [Fact]
    public async Task SolveAsync_FixedPairableLesson_LeavesOneTeacherSeat()
    {
        var problem = new ScheduleProblem(
            [new LessonDemand(1,10,1,1),new LessonDemand(2,20,1,0),new LessonDemand(3,30,1,0)],
            [new PlacementCandidate(2,20,100,1,1),new PlacementCandidate(3,30,100,1,1)],
            [new ScheduleSlot(1,1,0,1)],
            [new FixedPlacement(1,10,100,1,1,0,1)]);

        var solution = await new CpSatScheduleSolver().SolveAsync(problem,TimeSpan.FromSeconds(2));

        Assert.Single(solution.Placements);
        Assert.Equal(1,solution.UnassignedLessons);
    }

    [Fact]
    public async Task SolveAsync_NoGap_UsesCompleteOpenSlotSequence()
    {
        var candidates = new[]
        {
            new PlacementCandidate(1,10,100,1,1,SlotOrder:1),
            new PlacementCandidate(1,10,100,1,3,SlotOrder:3),
        };
        var slots = new[] { new ScheduleSlot(1,1,0,1),new ScheduleSlot(1,2,0,2),new ScheduleSlot(1,3,0,3) };
        var noGap = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem([new LessonDemand(1,10,2,0,AllowGap:false)],candidates,slots),TimeSpan.FromSeconds(2));
        var gapAllowed = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem([new LessonDemand(1,10,2,0,AllowGap:true)],candidates,slots),TimeSpan.FromSeconds(2));

        Assert.Single(noGap.Placements);
        Assert.Equal(2,gapAllowed.Placements.Count);
    }

    [Fact]
    public void Validator_RejectsUnsatisfiedRegularTeacherMinimum()
    {
        var problem = new ScheduleProblem(
            [new LessonDemand(1,10,1,0,RegularTeacherId:100,RegularTeacherPriority:5)],
            [new PlacementCandidate(1,10,100,1,1),new PlacementCandidate(1,10,200,1,1)]);
        var invalid = new ScheduleSolution([new SchedulePlacement(1,10,200,1,1)],0,0,TimeSpan.Zero);

        Assert.Throws<InvalidDataException>(()=>ScheduleSolutionValidator.Validate(problem,invalid));
    }
}
