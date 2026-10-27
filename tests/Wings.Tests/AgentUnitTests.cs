extern alias agent;
using agent::Wings.Agent.Executors;
using Wings.Agent.Contracts;
using Xunit;

namespace Wings.Tests;

public sealed class AgentUnitTests
{
    [Fact]
    public void Trx_MapsFullyQualifiedNames_AggregatesTheoryRows_AndReadsErrors()
    {
        var trx = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.trx");
        File.WriteAllText(trx, """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="a" testName="Create enquiry" outcome="Passed" duration="00:00:01.5000000" startTime="2026-10-27T10:00:00+05:30" endTime="2026-10-27T10:00:01+05:30" />
                <UnitTestResult testId="b" testName="Rounding(1)" outcome="Passed" duration="00:00:00.1000000" />
                <UnitTestResult testId="c" testName="Rounding(2)" outcome="Failed" duration="00:00:00.2000000">
                  <Output><ErrorInfo><Message>Expected 3 but was 2</Message><StackTrace>at Api.Rounding</StackTrace></ErrorInfo></Output>
                </UnitTestResult>
                <UnitTestResult testId="d" testName="Other" outcome="NotExecuted" duration="00:00:00" />
              </Results>
              <TestDefinitions>
                <UnitTest id="a" name="Create enquiry"><TestMethod className="Wings.UiTests.CreateEnquiryTests" name="CreateEnquiry_Saves" /></UnitTest>
                <UnitTest id="b" name="Rounding(1)"><TestMethod className="Api.InvoiceTests" name="Rounding(1)" /></UnitTest>
                <UnitTest id="c" name="Rounding(2)"><TestMethod className="Api.InvoiceTests" name="Rounding(2)" /></UnitTest>
                <UnitTest id="d" name="Other"><TestMethod className="Api.InvoiceTests+Nested" name="Other" /></UnitTest>
              </TestDefinitions>
            </TestRun>
            """);
        var (results, _) = DotNetTestExecutor.ParseTrx(trx, ["Wings.UiTests.CreateEnquiryTests.CreateEnquiry_Saves", "Api.InvoiceTests.Rounding", "Api.InvoiceTests.Nested.Other", "Not.Requested"]);
        File.Delete(trx);

        Assert.Equal(3, results.Count);
        Assert.Equal(TestOutcomes.Passed, results.Single(r => r.Identifier.EndsWith("CreateEnquiry_Saves")).Outcome);
        var rounding = results.Single(r => r.Identifier == "Api.InvoiceTests.Rounding");
        Assert.Equal(TestOutcomes.Failed, rounding.Outcome);
        Assert.Equal(300, rounding.DurationMs);
        Assert.Equal("Expected 3 but was 2", rounding.ErrorMessage);
        Assert.Equal(TestOutcomes.Skipped, results.Single(r => r.Identifier.EndsWith("Nested.Other")).Outcome);
    }
}
