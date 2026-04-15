using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MultiSessionHost.Core.Constants;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.TestDesktopApp;

public sealed class MainForm : Form
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TestDesktopAppOptions _options;
    private readonly Label _sessionIdLabel;
    private readonly Label _statusLabel;
    private readonly Label _selectedLabel;
    private readonly Label _alertLabel;
    private readonly ProgressBar _progressBar;
    private readonly ProgressBar _resourceProgressBar;
    private readonly TextBox _notesTextBox;
    private readonly ListBox _itemsListBox;
    private readonly ListBox _presenceListBox;
    private readonly CheckBox _enabledCheckBox;
    private readonly CheckBox _capabilityCheckBox;
    private readonly Button _startButton;
    private readonly Button _pauseButton;
    private readonly Button _resumeButton;
    private readonly Button _stopButton;
    private readonly Button _tickButton;
    private readonly Label _tickCountLabel;
    private int _tickCount;
    private int _artificialDelayMs;

    public MainForm(TestDesktopAppOptions options)
    {
        _options = options;

        Text = $"MultiSessionHost.TestDesktopApp [SessionId: {_options.SessionId}]";
        Width = 760;
        Height = 520;
        StartPosition = FormStartPosition.CenterScreen;

        var rootPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 12,
            Padding = new Padding(16)
        };

        rootPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        rootPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

        _sessionIdLabel = new Label
        {
            Name = "sessionIdLabel",
            AutoSize = true,
            Text = _options.SessionId
        };

        _statusLabel = new Label
        {
            Name = "statusLabel",
            AutoSize = true,
            Text = "Stopped"
        };

        _notesTextBox = new TextBox
        {
            Name = "notesTextBox",
            Multiline = true,
            Height = 90,
            Dock = DockStyle.Fill,
            Text = $"Notes for {_options.SessionId}"
        };

        _selectedLabel = new Label
        {
            Name = "selectedLabel",
            AutoSize = true,
            Text = "Selected: none"
        };

        _alertLabel = new Label
        {
            Name = "alertLabel",
            AutoSize = true,
            Text = "Alert: none"
        };

        _progressBar = new ProgressBar
        {
            Name = "progressBar",
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };

        _resourceProgressBar = new ProgressBar
        {
            Name = "resourceProgressBar",
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 100,
            Value = 80
        };

        _itemsListBox = new ListBox
        {
            Name = "itemsListBox",
            Dock = DockStyle.Fill,
            Height = 110
        };

        _itemsListBox.Items.AddRange(
        [
            $"{_options.SessionId}-item-1",
            $"{_options.SessionId}-item-2",
            $"{_options.SessionId}-item-3"
        ]);
        _itemsListBox.SelectedIndexChanged += (_, _) => UpdateSelectedLabelUnsafe();

        _presenceListBox = new ListBox
        {
            Name = "presenceListBox",
            Dock = DockStyle.Fill,
            Height = 80
        };

        _presenceListBox.Items.AddRange(
        [
            $"{_options.SessionId}-presence-1",
            $"{_options.SessionId}-presence-2"
        ]);

        _enabledCheckBox = new CheckBox
        {
            Name = "enabledCheckBox",
            AutoSize = true,
            Checked = true,
            Text = "Enabled"
        };

        _capabilityCheckBox = new CheckBox
        {
            Name = "capabilityCheckBox",
            AutoSize = true,
            Checked = true,
            Text = "Capabilities enabled"
        };

        _startButton = CreateActionButton("startButton", "Start", StartSessionUnsafe);
        _pauseButton = CreateActionButton("pauseButton", "Pause", PauseSessionUnsafe);
        _resumeButton = CreateActionButton("resumeButton", "Resume", ResumeSessionUnsafe);
        _stopButton = CreateActionButton("stopButton", "Stop", StopSessionUnsafe);
        _tickButton = CreateActionButton("tickButton", "Tick", TickUnsafe);

        _tickCountLabel = new Label
        {
            Name = "tickCountLabel",
            AutoSize = true,
            Text = "0"
        };

        rootPanel.Controls.Add(CreateCaption("SessionId"), 0, 0);
        rootPanel.Controls.Add(_sessionIdLabel, 1, 0);
        rootPanel.Controls.Add(CreateCaption("Status"), 0, 1);
        rootPanel.Controls.Add(_statusLabel, 1, 1);
        rootPanel.Controls.Add(CreateCaption("Notes"), 0, 2);
        rootPanel.Controls.Add(_notesTextBox, 1, 2);
        rootPanel.Controls.Add(CreateCaption("Items"), 0, 3);
        rootPanel.Controls.Add(_itemsListBox, 1, 3);
        rootPanel.Controls.Add(CreateCaption("Selected"), 0, 4);
        rootPanel.Controls.Add(_selectedLabel, 1, 4);
        rootPanel.Controls.Add(CreateCaption("Alert"), 0, 5);
        rootPanel.Controls.Add(_alertLabel, 1, 5);
        rootPanel.Controls.Add(CreateCaption("Progress"), 0, 6);
        rootPanel.Controls.Add(_progressBar, 1, 6);
        rootPanel.Controls.Add(CreateCaption("Resource"), 0, 7);
        rootPanel.Controls.Add(_resourceProgressBar, 1, 7);
        rootPanel.Controls.Add(CreateCaption("Capabilities"), 0, 8);
        rootPanel.Controls.Add(CreateCapabilitiesPanel(), 1, 8);
        rootPanel.Controls.Add(CreateCaption("Presence"), 0, 9);
        rootPanel.Controls.Add(_presenceListBox, 1, 9);
        rootPanel.Controls.Add(CreateButtonsPanel(), 1, 10);
        rootPanel.Controls.Add(CreateTickPanel(), 1, 11);

        Controls.Add(rootPanel);
    }

    public async Task<TestDesktopAppState> CaptureStateAsync()
    {
        await DelayIfConfiguredAsync().ConfigureAwait(false);
        return await InvokeOnUiThreadAsync(CaptureStateUnsafe).ConfigureAwait(false);
    }

    public async Task<UiSnapshotEnvelope> CaptureUiSnapshotAsync()
    {
        await DelayIfConfiguredAsync().ConfigureAwait(false);
        return await InvokeOnUiThreadAsync(CaptureUiSnapshotUnsafe).ConfigureAwait(false);
    }

    public async Task<TestDesktopAppState> StartSessionAsync()
    {
        await DelayIfConfiguredAsync().ConfigureAwait(false);
        return await InvokeOnUiThreadAsync(StartSessionUnsafe).ConfigureAwait(false);
    }

    public async Task<TestDesktopAppState> PauseSessionAsync()
    {
        await DelayIfConfiguredAsync().ConfigureAwait(false);
        return await InvokeOnUiThreadAsync(PauseSessionUnsafe).ConfigureAwait(false);
    }

    public async Task<TestDesktopAppState> ResumeSessionAsync()
    {
        await DelayIfConfiguredAsync().ConfigureAwait(false);
        return await InvokeOnUiThreadAsync(ResumeSessionUnsafe).ConfigureAwait(false);
    }

    public async Task<TestDesktopAppState> StopSessionAsync()
    {
        await DelayIfConfiguredAsync().ConfigureAwait(false);
        return await InvokeOnUiThreadAsync(StopSessionUnsafe).ConfigureAwait(false);
    }

    public async Task<TestDesktopAppState> TickAsync()
    {
        await DelayIfConfiguredAsync().ConfigureAwait(false);
        return await InvokeOnUiThreadAsync(TickUnsafe).ConfigureAwait(false);
    }

    public async Task<UiInteractionResult> ClickNodeAsync(string nodeId)
    {
        await DelayIfConfiguredAsync().ConfigureAwait(false);
        return await InvokeOnUiThreadAsync(
            () =>
            {
                if (!TryFindControl(nodeId, out var control))
                {
                    return Failure(UiCommandFailureCodes.NodeNotFound, $"Node '{nodeId}' was not found.");
                }

                return control switch
                {
                    Button button => ClickButton(button),
                    CheckBox checkBox => ToggleCheckBox(checkBox, requestedValue: null, operationName: "click"),
                    _ => Failure(UiCommandFailureCodes.UnsupportedCommand, $"Node '{nodeId}' does not support click.")
                };
            }).ConfigureAwait(false);
    }

    public async Task<UiInteractionResult> InvokeNodeActionAsync(string nodeId, string? actionName)
    {
        await DelayIfConfiguredAsync().ConfigureAwait(false);
        return await InvokeOnUiThreadAsync(
            () =>
            {
                if (!TryFindControl(nodeId, out var control))
                {
                    return Failure(UiCommandFailureCodes.NodeNotFound, $"Node '{nodeId}' was not found.");
                }

                if (control is not Button button)
                {
                    return Failure(UiCommandFailureCodes.UnsupportedCommand, $"Node '{nodeId}' does not support invoke.");
                }

                if (!string.IsNullOrWhiteSpace(actionName) &&
                    !string.Equals(actionName, button.Text, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(actionName, button.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return Failure(
                        UiCommandFailureCodes.InvalidCommandPayload,
                        $"Node '{nodeId}' does not expose action '{actionName}'.");
                }

                button.PerformClick();
                return Success($"Invoked action '{actionName ?? button.Text}' on node '{nodeId}'.");
            }).ConfigureAwait(false);
    }

    public async Task<UiInteractionResult> SetNodeTextAsync(string nodeId, string? textValue)
    {
        await DelayIfConfiguredAsync().ConfigureAwait(false);
        return await InvokeOnUiThreadAsync(
            () =>
            {
                if (!TryFindControl(nodeId, out var control))
                {
                    return Failure(UiCommandFailureCodes.NodeNotFound, $"Node '{nodeId}' was not found.");
                }

                if (control is not TextBox textBox)
                {
                    return Failure(UiCommandFailureCodes.UnsupportedCommand, $"Node '{nodeId}' does not support text input.");
                }

                textBox.Text = textValue ?? string.Empty;
                return Success($"Updated text for node '{nodeId}'.");
            }).ConfigureAwait(false);
    }

    public async Task<UiInteractionResult> ToggleNodeAsync(string nodeId, bool? boolValue)
    {
        await DelayIfConfiguredAsync().ConfigureAwait(false);
        return await InvokeOnUiThreadAsync(
            () =>
            {
                if (!TryFindControl(nodeId, out var control))
                {
                    return Failure(UiCommandFailureCodes.NodeNotFound, $"Node '{nodeId}' was not found.");
                }

                return control is CheckBox checkBox
                    ? ToggleCheckBox(checkBox, boolValue, operationName: "toggle")
                    : Failure(UiCommandFailureCodes.UnsupportedCommand, $"Node '{nodeId}' does not support toggle.");
            }).ConfigureAwait(false);
    }

    public async Task<UiInteractionResult> SelectItemAsync(string nodeId, string? selectedValue)
    {
        await DelayIfConfiguredAsync().ConfigureAwait(false);
        return await InvokeOnUiThreadAsync(
            () =>
            {
                if (!TryFindControl(nodeId, out var control))
                {
                    return Failure(UiCommandFailureCodes.NodeNotFound, $"Node '{nodeId}' was not found.");
                }

                if (control is not ListBox listBox)
                {
                    return Failure(UiCommandFailureCodes.UnsupportedCommand, $"Node '{nodeId}' does not support select.");
                }

                if (string.IsNullOrWhiteSpace(selectedValue))
                {
                    return Failure(UiCommandFailureCodes.InvalidCommandPayload, "selectedValue is required.");
                }

                var match = listBox.Items.Cast<object>().FirstOrDefault(item => string.Equals(item?.ToString(), selectedValue, StringComparison.Ordinal));

                if (match is null)
                {
                    return Failure(
                        UiCommandFailureCodes.InvalidCommandPayload,
                        $"Node '{nodeId}' does not contain item '{selectedValue}'.");
                }

                listBox.SelectedItem = match;
                return Success($"Selected '{selectedValue}' on node '{nodeId}'.");
            }).ConfigureAwait(false);
    }

    public object SetArtificialDelay(int milliseconds)
    {
        var normalized = Math.Max(0, milliseconds);
        Interlocked.Exchange(ref _artificialDelayMs, normalized);
        return new { delayMs = normalized };
    }

    private Label CreateCaption(string text) =>
        new()
        {
            AutoSize = true,
            Text = text,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 8, 8, 8)
        };

    private Button CreateActionButton(string name, string text, Func<TestDesktopAppState> callback)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            AutoSize = true
        };

        button.Click += (_, _) => callback();
        return button;
    }

    private Control CreateButtonsPanel()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill
        };

        panel.Controls.AddRange([_startButton, _pauseButton, _resumeButton, _stopButton, _tickButton]);
        return panel;
    }

    private Control CreateCapabilitiesPanel()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill
        };

        panel.Controls.Add(_enabledCheckBox);
        panel.Controls.Add(_capabilityCheckBox);
        return panel;
    }

    private Control CreateTickPanel()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill
        };

        panel.Controls.Add(CreateCaption("Tick Count"));
        panel.Controls.Add(_tickCountLabel);
        return panel;
    }

    private TestDesktopAppState CaptureStateUnsafe() =>
        new(
            _options.SessionId,
            _statusLabel.Text,
            _notesTextBox.Text,
            _enabledCheckBox.Checked,
            _itemsListBox.SelectedItem?.ToString(),
            _itemsListBox.Items.Cast<object>().Select(static item => item.ToString() ?? string.Empty).ToArray(),
            _tickCount,
            _options.Port,
            Environment.ProcessId,
            Handle.ToInt64(),
            Text,
            DateTimeOffset.UtcNow);

    private TestDesktopAppState StartSessionUnsafe()
    {
        _statusLabel.Text = "Running";
        return CaptureStateUnsafe();
    }

    private TestDesktopAppState PauseSessionUnsafe()
    {
        _statusLabel.Text = "Paused";
        return CaptureStateUnsafe();
    }

    private TestDesktopAppState ResumeSessionUnsafe()
    {
        _statusLabel.Text = "Running";
        return CaptureStateUnsafe();
    }

    private TestDesktopAppState StopSessionUnsafe()
    {
        _statusLabel.Text = "Stopped";
        return CaptureStateUnsafe();
    }

    private TestDesktopAppState TickUnsafe()
    {
        _tickCount++;
        _tickCountLabel.Text = _tickCount.ToString(CultureInfo.InvariantCulture);
        _progressBar.Value = Math.Min(100, (_tickCount * 10) % 110);
        _resourceProgressBar.Value = Math.Max(0, 80 - (_tickCount * 5));
        _alertLabel.Text = _tickCount % 2 == 0 ? "Alert: none" : "Alert: warning";
        return CaptureStateUnsafe();
    }

    private void UpdateSelectedLabelUnsafe()
    {
        _selectedLabel.Text = _itemsListBox.SelectedItem is null
            ? "Selected: none"
            : $"Selected: {_itemsListBox.SelectedItem}";
    }

    private UiSnapshotEnvelope CaptureUiSnapshotUnsafe()
    {
        using var process = Process.GetCurrentProcess();
        var processInfo = new DesktopProcessInfo(process.Id, process.ProcessName, Environment.CommandLine, Handle.ToInt64());
        var windowInfo = new DesktopWindowInfo(Handle.ToInt64(), process.Id, Text, Visible);
        var root = CaptureControlSnapshot(this);
        var metadata = new Dictionary<string, string?>
        {
            ["status"] = _statusLabel.Text,
            ["port"] = _options.Port.ToString(CultureInfo.InvariantCulture),
            ["tickCount"] = _tickCount.ToString(CultureInfo.InvariantCulture),
            ["enabled"] = _enabledCheckBox.Checked.ToString(),
            ["progress"] = _progressBar.Value.ToString(CultureInfo.InvariantCulture),
            ["resource"] = _resourceProgressBar.Value.ToString(CultureInfo.InvariantCulture)
        };

        return new UiSnapshotEnvelope(
            _options.SessionId,
            DateTimeOffset.UtcNow,
            processInfo,
            windowInfo,
            JsonSerializer.SerializeToElement(root, SnapshotJsonOptions),
            metadata);
    }

    private ControlSnapshotNode CaptureControlSnapshot(Control control)
    {
        var attributes = new Dictionary<string, string?>
        {
            ["controlType"] = control.GetType().Name,
            ["tabIndex"] = control.TabIndex.ToString(CultureInfo.InvariantCulture)
        };

        switch (control)
        {
            case TextBox textBox:
                attributes["multiline"] = textBox.Multiline.ToString();
                attributes["textLength"] = textBox.TextLength.ToString(CultureInfo.InvariantCulture);
                attributes["acceptsText"] = bool.TrueString;
                attributes["semanticActions"] = "setText";
                break;

            case ListBox listBox:
                attributes["itemCount"] = listBox.Items.Count.ToString(CultureInfo.InvariantCulture);
                attributes["selectedItem"] = listBox.SelectedItem?.ToString();
                attributes["items"] = JsonSerializer.Serialize(listBox.Items.Cast<object>().Select(static item => item.ToString() ?? string.Empty).ToArray(), SnapshotJsonOptions);
                attributes["semanticActions"] = "select";
                attributes["scrollable"] = bool.TrueString;
                if (string.Equals(listBox.Name, "presenceListBox", StringComparison.Ordinal))
                {
                    attributes["semanticRole"] = "presence";
                    attributes["entityCount"] = listBox.Items.Count.ToString(CultureInfo.InvariantCulture);
                }

                break;

            case CheckBox checkBox:
                attributes["checked"] = checkBox.Checked.ToString();
                attributes["clickable"] = bool.TrueString;
                attributes["semanticActions"] = "click,toggle";
                attributes["semanticRole"] = string.Equals(checkBox.Name, "capabilityCheckBox", StringComparison.Ordinal)
                    ? "capability"
                    : "toggle";
                break;

            case Button button:
                attributes["command"] = control.Text;
                attributes["actionNames"] = string.Join(',', button.Text, button.Name);
                attributes["clickable"] = bool.TrueString;
                attributes["invokable"] = bool.TrueString;
                attributes["semanticActions"] = "click,invoke";
                break;

            case Label label when string.Equals(label.Name, "alertLabel", StringComparison.Ordinal):
                attributes["semanticRole"] = "alert";
                attributes["severity"] = label.Text.Contains("warning", StringComparison.OrdinalIgnoreCase) ? "Warning" : "Info";
                attributes["source"] = "Alert";
                break;

            case Label label when string.Equals(label.Name, "selectedLabel", StringComparison.Ordinal):
                attributes["semanticRole"] = "target";
                attributes["active"] = (!label.Text.EndsWith("none", StringComparison.OrdinalIgnoreCase)).ToString();
                break;

            case ProgressBar progressBar:
                attributes["value"] = progressBar.Value.ToString(CultureInfo.InvariantCulture);
                attributes["maximum"] = progressBar.Maximum.ToString(CultureInfo.InvariantCulture);
                attributes["valuePercent"] = progressBar.Value.ToString(CultureInfo.InvariantCulture);
                attributes["semanticRole"] = string.Equals(progressBar.Name, "resourceProgressBar", StringComparison.Ordinal)
                    ? "resource"
                    : "transit";
                attributes["status"] = progressBar.Value > 0 && progressBar.Value < progressBar.Maximum ? "InProgress" : "Idle";
                break;
        }

        if (ReferenceEquals(control, this))
        {
            attributes["sessionId"] = _options.SessionId;
            attributes["port"] = _options.Port.ToString(CultureInfo.InvariantCulture);
        }

        return new ControlSnapshotNode(
            Id: GetControlNodeId(control),
            Role: control.GetType().Name.Replace("Control", string.Empty, StringComparison.Ordinal),
            Name: string.IsNullOrWhiteSpace(control.Name) ? null : control.Name,
            Text: control.Text,
            Bounds: new ControlSnapshotBounds(control.Bounds.X, control.Bounds.Y, control.Bounds.Width, control.Bounds.Height),
            Visible: control.Visible,
            Enabled: control.Enabled,
            Selected: GetSelectedState(control),
            Attributes: attributes,
            Children: control.Controls.Cast<Control>().Select(CaptureControlSnapshot).ToArray());
    }

    private static bool GetSelectedState(Control control) =>
        control switch
        {
            CheckBox checkBox => checkBox.Checked,
            ListBox listBox => listBox.SelectedIndex >= 0,
            _ => false
        };

    private UiInteractionResult ClickButton(Button button)
    {
        button.PerformClick();
        return Success($"Clicked node '{button.Name}'.");
    }

    private UiInteractionResult ToggleCheckBox(CheckBox checkBox, bool? requestedValue, string operationName)
    {
        checkBox.Checked = requestedValue ?? !checkBox.Checked;
        return Success($"{operationName} completed for node '{checkBox.Name}'.");
    }

    private bool TryFindControl(string nodeId, out Control? control)
    {
        control = FindControlRecursive(this, nodeId);
        return control is not null;
    }

    private static Control? FindControlRecursive(Control current, string nodeId)
    {
        if (string.Equals(GetControlNodeId(current), nodeId, StringComparison.Ordinal))
        {
            return current;
        }

        foreach (Control child in current.Controls)
        {
            var match = FindControlRecursive(child, nodeId);

            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string GetControlNodeId(Control control) =>
        string.IsNullOrWhiteSpace(control.Name) ? $"{control.GetType().Name}-{control.Handle}" : control.Name;

    private static UiInteractionResult Success(string message) =>
        UiInteractionResult.Success(message, DateTimeOffset.UtcNow);

    private static UiInteractionResult Failure(string failureCode, string message) =>
        UiInteractionResult.Failure(message, failureCode, DateTimeOffset.UtcNow);

    private async Task DelayIfConfiguredAsync()
    {
        var delayMs = Volatile.Read(ref _artificialDelayMs);

        if (delayMs > 0)
        {
            await Task.Delay(delayMs).ConfigureAwait(false);
        }
    }

    private Task<T> InvokeOnUiThreadAsync<T>(Func<T> callback)
    {
        if (!InvokeRequired)
        {
            return Task.FromResult(callback());
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(
            new Action(
                () =>
                {
                    try
                    {
                        completion.SetResult(callback());
                    }
                    catch (Exception exception)
                    {
                        completion.SetException(exception);
                    }
                }));

        return completion.Task;
    }

    private sealed record ControlSnapshotBounds(
        int X,
        int Y,
        int Width,
        int Height);

    private sealed record ControlSnapshotNode(
        string Id,
        string Role,
        string? Name,
        string? Text,
        ControlSnapshotBounds? Bounds,
        bool Visible,
        bool Enabled,
        bool Selected,
        IReadOnlyDictionary<string, string?> Attributes,
        IReadOnlyList<ControlSnapshotNode> Children);
}
