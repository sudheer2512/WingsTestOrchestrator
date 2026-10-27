using Wings.Application.Common;
using Wings.Application.Execution;
using Wings.Application.Orchestration;
using Wings.Domain.Orchestration;
using Xunit;

namespace Wings.Tests;

public sealed class PlannerUnitTests
{
    [Fact]
    public void Planner_AssignsWaves_FromTestCaseAndFeatureDependencies()
    {
        Guid customerTc = Guid.NewGuid(), orderTc = Guid.NewGuid(), invoiceFeature = Guid.NewGuid(), orderFeature = Guid.NewGuid();
        var customer = new PlannerNode(Guid.NewGuid(), customerTc, null, "Create customer");
        var order = new PlannerNode(Guid.NewGuid(), orderTc, orderFeature, "Create sales order");
        var invoice = new PlannerNode(Guid.NewGuid(), null, invoiceFeature, "Generate invoice");
        var independent = new PlannerNode(Guid.NewGuid(), null, null, "Login page loads");
        var plan = DependencyPlanner.Plan([customer, order, invoice, independent], [(orderTc, customerTc)], [(invoiceFeature, orderFeature)]);
        Assert.Equal(0, plan.Levels[customer.Id]);
        Assert.Equal(1, plan.Levels[order.Id]);
        Assert.Equal(2, plan.Levels[invoice.Id]);
        Assert.Equal(0, plan.Levels[independent.Id]);
        Assert.Equal([order.Id], plan.Prerequisites[invoice.Id]);
    }

    [Fact]
    public void Planner_RejectsCycles()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var ex = Assert.Throws<ValidationException>(() => DependencyPlanner.Plan([new(Guid.NewGuid(), a, null, "A"), new(Guid.NewGuid(), b, null, "B")], [(a, b), (b, a)], []));
        Assert.Contains("Circular", string.Join(" ", ex.Errors.Values.SelectMany(v => v)));
    }

    [Theory]
    [InlineData(TriggerType.Hourly, null, 15, "15 * * * *")]
    [InlineData(TriggerType.Daily, "21:05", 0, "5 21 * * *")]
    public void Cron_IsBuiltFromSimpleTriggers(TriggerType trigger, string? time, int minute, string expected)
        => Assert.Equal(expected, ScheduleService.BuildCron(new ScheduleUpsert { Trigger = trigger, Time = time, Minute = minute }));

    [Fact]
    public void DataRequirements_Parse()
    {
        var reqs = TestDataService.Parse("customers:2, VEHICLES, customers:1");
        Assert.Equal([("CUSTOMERS", 2), ("VEHICLES", 1)], reqs.Select(r => (r.Code, r.Count)).OrderBy(r => r.Code));
        Assert.Throws<ValidationException>(() => TestDataService.Parse("bad code:x"));
    }
}
