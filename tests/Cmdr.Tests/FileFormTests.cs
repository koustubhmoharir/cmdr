using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;

namespace Cmdr.Tests;

/// <summary>The file dialog's values: env lines, base env names (only with a base file) and secret names, and what is built from them.</summary>
public sealed class FileFormTests
{
    private static FileFormValues Values(
        string env = "A=1",
        bool hasBase = true,
        string baseEnv = "",
        string secret = "",
        IReadOnlyDictionary<string, string?>? keptEnv = null,
        IReadOnlyList<string>? keptBaseEnv = null,
        IReadOnlyList<string>? keptSecret = null) =>
        new(env, keptEnv, hasBase, baseEnv, keptBaseEnv, secret, keptSecret, null);

    [Fact]
    public void Valid_values_have_no_errors()
    {
        Assert.Empty(FileForm.Validate(Values(baseEnv: "IMPORTED", secret: "TOKEN, OTHER")));
    }

    [Fact]
    public void A_bad_env_line_is_an_error_with_its_line_number()
    {
        IReadOnlyList<string> errors = FileForm.Validate(Values(env: "A=1\n=oops"));

        Assert.Single(errors);
        Assert.StartsWith("Env: Line 2", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_bad_secret_name_is_an_error()
    {
        IReadOnlyList<string> errors = FileForm.Validate(Values(secret: "GOOD, A=B"));

        Assert.Single(errors);
        Assert.StartsWith("Secret:", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Base_env_names_without_a_base_file_are_an_error()
    {
        IReadOnlyList<string> errors = FileForm.Validate(Values(hasBase: false, baseEnv: "IMPORTED"));

        Assert.Single(errors);
        Assert.Contains("needs a base file", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Base_env_names_are_allowed_with_a_base_file_and_checked_by_the_name_rules()
    {
        Assert.Empty(FileForm.Validate(Values(hasBase: true, baseEnv: "IMPORTED")));
        Assert.Single(FileForm.Validate(Values(hasBase: true, baseEnv: "BAD=NAME")));
    }

    [Fact]
    public void Kept_values_are_not_checked()
    {
        var keptEnv = new Dictionary<string, string?> { ["A"] = "first\nB=second" };

        Assert.Empty(FileForm.Validate(Values(env: "=broken", keptEnv: keptEnv, hasBase: false, baseEnv: "X", keptBaseEnv: new[] { "X" }, secret: "A=B", keptSecret: new[] { "A=B" })));
    }

    [Fact]
    public void Build_writes_empty_settings_as_null_and_names_as_typed()
    {
        FileSettings settings = FileForm.Build(Values(env: "  ", baseEnv: " , ", secret: "TOKEN, OTHER"));

        Assert.Null(settings.Env);
        Assert.Null(settings.BaseEnv);
        Assert.Equal(new[] { "TOKEN", "OTHER" }, settings.Secret);
        Assert.Null(settings.EnvScript);
    }

    [Fact]
    public void Build_keeps_the_env_and_names_that_are_read_only_exactly()
    {
        var keptEnv = new Dictionary<string, string?> { ["A"] = "first\nB=second", ["C"] = null };

        FileSettings settings = FileForm.Build(Values(env: "ignored", keptEnv: keptEnv, keptSecret: new[] { "A,B" }, keptBaseEnv: new[] { " X" }));

        Assert.Equal(keptEnv, settings.Env);
        Assert.Equal(new[] { "A,B" }, settings.Secret);
        Assert.Equal(new[] { " X" }, settings.BaseEnv);
    }

    [Fact]
    public void Build_carries_the_environment_script_as_given()
    {
        var script = new EnvScriptSpec { Program = "sh" };
        var values = new FileFormValues("", null, true, "", null, "", null, script);

        Assert.Same(script, FileForm.Build(values).EnvScript);
    }
}
