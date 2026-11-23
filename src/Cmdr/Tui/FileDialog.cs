using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using static Cmdr.Tui.DialogControls;

namespace Cmdr.Tui;

/// <summary>
/// Edits the top-level settings of the opened file: its env, baseEnv, secret and envScript. OK validates the fields (errors are shown and
/// the dialog stays open), then calls the commit function; a message from commit also keeps it open. Esc or Cancel leaves the session
/// unchanged.
/// </summary>
/// <remarks>
/// Two columns so that the dialog fits an 80x24 terminal. <see cref="Result"/> is true when the settings were committed. The base env field
/// is read-only when the file has no <c>base</c>. A value that cannot be written in its form (see <see cref="EnvText.CanEdit"/> and
/// <see cref="NameListText.CanEdit"/>) is read-only and kept exactly: edit it in the JSON.
/// </remarks>
internal sealed class FileDialog : Dialog<bool>
{
    private const int DialogWidth = 70;
    private const int DialogHeight = 17;
    private const int LeftWidth = 32;
    private const int RightX = 36;
    private const int RightWidth = 32;

    private readonly IApplication _app;
    private readonly Func<FileSettings, string?> _commit;
    private readonly TextArea _env;
    private readonly TextField _baseEnv;
    private readonly TextField _secret;
    private readonly Label _envScriptSummary;

    // The original env, base env and secret names when their field is read-only; null when edited as text.
    private readonly IReadOnlyDictionary<string, string?>? _keptEnv;
    private readonly IReadOnlyList<string>? _keptBaseEnv;
    private readonly IReadOnlyList<string>? _keptSecret;

    private readonly bool _hasBase;
    private EnvScriptSpec? _envScript;

    /// <param name="app">The application, for the message boxes and the environment script dialog.</param>
    /// <param name="file">The opened file (its settings are shown and are the initial values).</param>
    /// <param name="commit">Applies the settings to the session. Returns an error message, or null on success.</param>
    public FileDialog(IApplication app, ConfigSource file, Func<FileSettings, string?> commit)
    {
        ArgumentNullException.ThrowIfNull(file);

        _app = app;
        _commit = commit;
        Title = "File settings";
        Width = DialogWidth;
        Height = DialogHeight;

        _hasBase = file.BaseSetting is not null;
        _envScript = file.EnvScript;

        IReadOnlyDictionary<string, string?> initialEnv = file.Env ?? new Dictionary<string, string?>();
        bool envEditable = EnvText.CanEdit(initialEnv);
        _keptEnv = envEditable ? null : new Dictionary<string, string?>(initialEnv);
        _env = Area(0, 1, LeftWidth, 7, EnvText.Format(initialEnv));
        _env.ReadOnly = !envEditable;

        bool baseEnvEditable = _hasBase && NameListText.CanEdit(file.BaseEnv);
        _keptBaseEnv = baseEnvEditable ? null : file.BaseEnv?.ToList();
        _baseEnv = Field(RightX, 1, RightWidth, NameListText.Format(file.BaseEnv));
        _baseEnv.ReadOnly = !baseEnvEditable;

        bool secretEditable = NameListText.CanEdit(file.Secret);
        _keptSecret = secretEditable ? null : file.Secret?.ToList();
        _secret = Field(RightX, 4, RightWidth, NameListText.Format(file.Secret));
        _secret.ReadOnly = !secretEditable;

        var editEnvScript = new Button { Text = "Env script…", X = 0, Y = 8 };
        editEnvScript.Accepting += (_, e) =>
        {
            e.Handled = true;
            EditEnvScript();
        };
        _envScriptSummary = new Label
        {
            X = 0,
            Y = 9,
            Width = LeftWidth,
            Text = EnvScriptForm.Summary(_envScript, LeftWidth),
        };

        Add(
            Caption(envEditable ? "Env (KEY=VALUE, !KEY removes)" : "Env: read-only, edit the JSON", 0, 0), _env,
            editEnvScript, _envScriptSummary,
            Caption(baseEnvEditable ? "Base env (names, commas)" : "Base env: read-only", RightX, 0), _baseEnv,
            new Label { X = RightX, Y = 2, Width = RightWidth, Text = _hasBase ? string.Empty : "No base: set base in the JSON." },
            Caption(secretEditable ? "Secret (names, commas)" : "Secret: read-only", RightX, 3), _secret);

        var ok = new Button { Text = "_OK" };
        var cancel = new Button { Text = "_Cancel" };
        cancel.Accepting += (_, e) =>
        {
            e.Handled = true;
            _app.RequestStop(this);
        };
        AddButton(ok);
        AddButton(cancel);

        // Terminal.Gui makes the last button added the default; Enter must mean OK.
        ok.IsDefault = true;
        cancel.IsDefault = false;
    }

    /// <summary>OK, or Enter in a field: validate and commit, and close only when that succeeds.</summary>
    protected override bool OnAccepting(CommandEventArgs args)
    {
        args.Handled = true;
        if (TryCommit())
        {
            _app.RequestStop(this);
        }

        return true;
    }

    /// <summary>Opens the environment script dialog. Its result replaces the script of the dialog, when OK or Remove was chosen.</summary>
    private void EditEnvScript()
    {
        using var dialog = new EnvScriptDialog(_app, "Environment script", _envScript);
        _app.Run(dialog);
        if (dialog.Result == true)
        {
            _envScript = dialog.Spec;
            _envScriptSummary.Text = EnvScriptForm.Summary(_envScript, LeftWidth);
        }
    }

    /// <summary>Validates and commits. Returns true when the dialog should close.</summary>
    private bool TryCommit()
    {
        FileFormValues values = new(
            Env: _env.Text,
            KeptEnv: _keptEnv,
            HasBase: _hasBase,
            BaseEnv: _baseEnv.Text,
            KeptBaseEnv: _keptBaseEnv,
            Secret: _secret.Text,
            KeptSecret: _keptSecret,
            EnvScript: _envScript);

        IReadOnlyList<string> errors = FileForm.Validate(values);
        if (errors.Count > 0)
        {
            MessageBox.ErrorQuery(_app, "File settings", string.Join("\n", errors), "_OK");
            return false;
        }

        string? error = _commit(FileForm.Build(values));
        if (error is not null)
        {
            MessageBox.ErrorQuery(_app, "File settings", error, "_OK");
            return false;
        }

        Result = true;
        return true;
    }
}
