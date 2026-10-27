using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace WingsAuto.WinForms.UiTests;

/// <summary>
/// SAMPLE FlaUI tests for the "Create Enquiry" form of the sample Wings Auto WinForms application
/// (samples/apps/WingsAuto.SampleApp). Traits link each test to a Wings feature (creation sequence) so the orchestrator
/// maps them on upload. The agent supplies WINGS_APP_PATH (the installed app) and WINGS_ARTIFACTS_DIR (evidence folder).
/// </summary>
public sealed class CreateEnquiryTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);
    private readonly UIA3Automation _automation = new();
    private readonly Application _app;
    private readonly Window _main;

    public CreateEnquiryTests()
    {
        var exe = Environment.GetEnvironmentVariable("WINGS_APP_PATH")
                  ?? throw new InvalidOperationException("WINGS_APP_PATH is not set on this agent.");
        _app = Application.Launch(exe);
        _main = _app.GetMainWindow(_automation, Wait) ?? throw new InvalidOperationException("The application window did not appear.");
    }

    [Fact(DisplayName = "Create enquiry with valid mandatory fields")]
    [Trait("WingsFeature", "00003")]
    public void CreateEnquiry_WithMandatoryFields_SavesAndShowsNumber()
    {
        var form = OpenEnquiryForm();
        Find(form, "txtCustomerName").AsTextBox().Text = "Test Customer";
        Find(form, "txtMobile").AsTextBox().Text = "9876543210";
        Find(form, "cboModel").AsComboBox().Select(0);
        Find(form, "btnSave").AsButton().Invoke();

        var number = Retry.WhileEmpty(() => Find(form, "lblEnquiryNo").Name, Wait).Result;
        Screenshot(nameof(CreateEnquiry_WithMandatoryFields_SavesAndShowsNumber) + "-saved");
        Assert.Matches(@"^ENQ-\d+$", number);
    }

    [Fact(DisplayName = "Reject enquiry without mobile number")]
    [Trait("WingsFeature", "00003")]
    public void CreateEnquiry_WithoutMobile_ShowsValidationMessage()
    {
        var form = OpenEnquiryForm();
        Find(form, "txtCustomerName").AsTextBox().Text = "Test Customer";
        Find(form, "btnSave").AsButton().Invoke();

        var dialog = Retry.WhileNull(() => _main.ModalWindows.FirstOrDefault(), Wait).Result
                     ?? throw new InvalidOperationException("No validation dialog appeared.");
        Screenshot(nameof(CreateEnquiry_WithoutMobile_ShowsValidationMessage) + "-dialog");
        var texts = dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Select(t => t.Name);
        Assert.Contains(texts, t => t.Contains("Mobile number is required"));
        dialog.FindFirstDescendant(cf => cf.ByName("OK")).AsButton().Invoke();
    }

    private Window OpenEnquiryForm()
    {
        Find(_main, "btnNewEnquiry").AsButton().Invoke();
        return Retry.WhileNull(() => _main.FindFirstDescendant(cf => cf.ByAutomationId("frmEnquiry")), Wait).Result?.AsWindow()
               ?? throw new InvalidOperationException("The Create Enquiry form did not open.");
    }

    private static AutomationElement Find(AutomationElement parent, string automationId)
        => Retry.WhileNull(() => parent.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), Wait).Result
           ?? throw new InvalidOperationException($"Control '{automationId}' was not found.");

    /// <summary>Saves a desktop screenshot as evidence; files named after the test method are attached to that test.</summary>
    private static void Screenshot(string name)
    {
        if (Environment.GetEnvironmentVariable("WINGS_ARTIFACTS_DIR") is not { Length: > 0 } dir) return;
        Capture.Screen().ToFile(Path.Combine(dir, $"{name}.png"));
    }

    public void Dispose()
    {
        _app.Close();
        _app.Dispose();
        _automation.Dispose();
    }
}
