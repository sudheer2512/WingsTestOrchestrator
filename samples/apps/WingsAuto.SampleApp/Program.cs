namespace WingsAuto.SampleApp;

/// <summary>
/// SAMPLE application under test. It mimics a Wings Auto MDI desktop screen ("Create Enquiry") with stable control
/// names, which WinForms exposes to UI Automation as AutomationIds — the recommended way to make Wings forms testable.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    public MainForm()
    {
        Name = "frmMain";
        Text = "Wings Auto (SAMPLE)";
        IsMdiContainer = true;
        Width = 1000;
        Height = 700;

        var menu = new MenuStrip { Name = "mnuMain" };
        var sales = new ToolStripMenuItem("Sales") { Name = "mnuSales" };
        sales.DropDownItems.Add(new ToolStripMenuItem("New Enquiry", null, (_, _) => OpenEnquiry()) { Name = "mnuNewEnquiry", ShortcutKeys = Keys.Control | Keys.E });
        menu.Items.Add(sales);
        MainMenuStrip = menu;

        // A plain button has its own window handle, so it is the most reliable automation entry point.
        var bar = new Panel { Dock = DockStyle.Top, Height = 40, Name = "pnlActions" };
        var newEnquiry = new Button { Name = "btnNewEnquiry", Text = "New Enquiry", Left = 8, Top = 6, Width = 120 };
        newEnquiry.Click += (_, _) => OpenEnquiry();
        bar.Controls.Add(newEnquiry);

        Controls.Add(bar);
        Controls.Add(menu);
    }

    private void OpenEnquiry() => new EnquiryForm { MdiParent = this }.Show();
}

internal sealed class EnquiryForm : Form
{
    private static int _next = 1001;
    private readonly TextBox _name = new() { Name = "txtCustomerName", Left = 140, Top = 20, Width = 260 };
    private readonly TextBox _mobile = new() { Name = "txtMobile", Left = 140, Top = 55, Width = 260 };
    private readonly ComboBox _model = new() { Name = "cboModel", Left = 140, Top = 90, Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _number = new() { Name = "lblEnquiryNo", Left = 140, Top = 170, Width = 260, Text = "" };

    public EnquiryForm()
    {
        Name = "frmEnquiry";
        Text = "Create Enquiry";
        Width = 460;
        Height = 260;
        _model.Items.AddRange(["Sedan LX", "Hatchback ZX", "SUV Pro"]);
        var save = new Button { Name = "btnSave", Text = "Save", Left = 140, Top = 130, Width = 100 };
        save.Click += (_, _) => Save();
        Controls.AddRange([
            new Label { Text = "Customer name", Left = 20, Top = 23, Width = 110 }, _name,
            new Label { Text = "Mobile number", Left = 20, Top = 58, Width = 110 }, _mobile,
            new Label { Text = "Model", Left = 20, Top = 93, Width = 110 }, _model,
            save, _number,
        ]);
    }

    private void Save()
    {
        string? error = string.IsNullOrWhiteSpace(_name.Text) ? "Customer name is required"
            : string.IsNullOrWhiteSpace(_mobile.Text) ? "Mobile number is required"
            : _mobile.Text.Length != 10 || !_mobile.Text.All(char.IsDigit) ? "Mobile number must be 10 digits"
            : _model.SelectedIndex < 0 ? "Select a model" : null;
        if (error is not null) { MessageBox.Show(this, error, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        _number.Text = $"ENQ-{_next++}";
    }
}
