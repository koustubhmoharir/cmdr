using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using CmdrSession = Cmdr.Core.Session.Session;
using Shared.Processes;

namespace Cmdr.Core.Tests.Session;

/// <summary>
/// The secret names that apply to a file, a group and a command (<see cref="CmdrSession.SecretNamesFor(ConfigSource)"/> and
/// <see cref="CmdrSession.SecretNamesFor(ItemEntry)"/>), and the placeholder names that <see cref="Placeholders.EnvNames"/> finds.
/// </summary>
public class SecretNamesTests
{
    private static readonly string BaseRel = Path.Combine("shared", "base.json");
    private static readonly string DeepRel = Path.Combine("shared", "deep.json");

    private static CommandSpec Command(string name, params string[] secret) => new() { Name = name, Program = "tool", Secret = secret.ToList() };

    private static GroupSpec Group(string name, string[]? secret, params ItemSpec[] items) =>
        new() { Name = name, Secret = secret?.ToList(), Items = items.ToList() };

    [Fact]
    public void A_file_has_its_own_top_level_secret_names()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { Secret = new() { "API_KEY" } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        IReadOnlySet<string> names = session.SecretNamesFor(session.MainSource);

        Assert.Equal(new[] { "API_KEY" }, names.ToArray());
    }

    [Fact]
    public void A_file_without_secret_names_has_none()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig(), dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        Assert.Empty(session.SecretNamesFor(session.MainSource));
    }

    [Fact]
    public void A_file_gets_a_base_secret_name_only_when_its_base_env_imports_it()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { Secret = new() { "TOKEN", "OTHER" }, Env = new() { ["PLAIN"] = "x" } }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel, BaseEnv = new() { "TOKEN" } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        IReadOnlySet<string> names = session.SecretNamesFor(session.MainSource);

        Assert.Contains("TOKEN", names);
        Assert.DoesNotContain("OTHER", names);
    }

    [Fact]
    public void A_base_secret_name_that_the_file_does_not_import_is_not_secret_in_the_file()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { Secret = new() { "TOKEN" } }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel, Env = new() { ["TOKEN"] = "own" } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        Assert.DoesNotContain("TOKEN", session.SecretNamesFor(session.MainSource));
        Assert.Contains("TOKEN", session.SecretNamesFor(session.Sources[0]));
    }

    [Fact]
    public void A_base_import_is_followed_through_the_files_that_the_base_imports()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { Secret = new() { "DEEP" } }, dir.File(DeepRel));
        ConfigStore.Save(new CmdrConfig { Base = "deep.json", BaseEnv = new() { "DEEP" } }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel, BaseEnv = new() { "DEEP" } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        Assert.Contains("DEEP", session.SecretNamesFor(session.MainSource));
    }

    [Fact]
    public void A_group_has_its_file_names_and_the_secret_names_of_every_group_above_it()
    {
        using var dir = new TempDir();
        ConfigStore.Save(
            new CmdrConfig
            {
                Secret = new() { "FILE" },
                Groups =
                {
                    Group("outer", new[] { "OUTER" }, new GroupSpec
                    {
                        Name = "inner",
                        Secret = new() { "INNER" },
                        Items = { Command("run") },
                    }, Command("sibling")),
                },
            },
            dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);
        GroupEntry inner = session.AllGroups.Single(group => group.Name == "inner");
        GroupEntry outer = session.AllGroups.Single(group => group.Name == "outer");

        Assert.Equal(new[] { "FILE", "INNER", "OUTER" }, session.SecretNamesFor(inner).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(new[] { "FILE", "OUTER" }, session.SecretNamesFor(outer).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void A_command_has_its_group_names_and_its_own_secret_names_but_not_those_of_its_siblings()
    {
        using var dir = new TempDir();
        ConfigStore.Save(
            new CmdrConfig
            {
                Secret = new() { "FILE" },
                Groups =
                {
                    Group("outer", new[] { "OUTER" }, new GroupSpec
                    {
                        Name = "inner",
                        Secret = new() { "INNER" },
                        Items = { Command("run", "OWN"), Command("plain") },
                    }),
                },
            },
            dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);
        CommandEntry run = session.AllEntries.Single(entry => entry.Name == "run");
        CommandEntry plain = session.AllEntries.Single(entry => entry.Name == "plain");

        Assert.Equal(new[] { "FILE", "INNER", "OUTER", "OWN" }, session.SecretNamesFor(run).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(new[] { "FILE", "INNER", "OUTER" }, session.SecretNamesFor(plain).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void A_command_in_an_imported_base_file_gets_the_base_file_names_and_its_base_import_rule()
    {
        using var dir = new TempDir();
        ConfigStore.Save(
            new CmdrConfig
            {
                Secret = new() { "BASE_SECRET" },
                Groups = { Group("shared", new[] { "BASE_GROUP" }, Command("tool", "BASE_OWN")) },
            },
            dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel, BaseEnv = new() { "BASE_SECRET" } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);
        CommandEntry tool = session.AllEntries.Single(entry => entry.Name == "tool");

        Assert.Equal(new[] { "BASE_GROUP", "BASE_OWN", "BASE_SECRET" }, session.SecretNamesFor(tool).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Names_match_case_insensitively_only_on_Windows()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { Secret = new() { "Token" } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        Assert.Equal(OperatingSystem.IsWindows(), session.SecretNamesFor(session.MainSource).Contains("TOKEN"));
        Assert.True(session.SecretNamesFor(session.MainSource).Contains("Token"));
    }

    [Fact]
    public void Env_names_are_the_names_of_the_well_formed_env_placeholders_in_order()
    {
        Assert.Equal(new[] { "A", "B", "A" }, Placeholders.EnvNames("${env:A} x ${env:B}${env:A}"));
        Assert.Equal(new[] { "ProgramFiles(x86)" }, Placeholders.EnvNames("${env:ProgramFiles(x86)}"));
    }

    [Fact]
    public void Env_names_leave_out_escapes_inputs_built_ins_and_malformed_placeholders()
    {
        Assert.Empty(Placeholders.EnvNames("$${env:X}"));
        Assert.Empty(Placeholders.EnvNames("${input:Y}${cwd}${configDir}${pathsep}"));
        Assert.Empty(Placeholders.EnvNames("${NAME}${env:}${env:D=x}"));
        Assert.Empty(Placeholders.EnvNames("${env:C"));
        Assert.Empty(Placeholders.EnvNames("plain text"));
    }

    private static EnvLevel Level(params (string Key, string? Value)[] entries) =>
        new(entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal), "/d");

    private static IReadOnlySet<string> Declared(params string[] names) => new HashSet<string>(names, StringComparer.Ordinal);

    [Fact]
    public void Extend_adds_a_key_whose_value_references_a_secret_and_leaves_the_others()
    {
        IReadOnlySet<string> names = SecretNames.Extend(
            new[] { Level(("X", "${env:S}/bin"), ("PLAIN", "visible")) },
            Declared("S"),
            Declared());

        Assert.Equal(new[] { "S", "X" }, names.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Extend_is_transitive_through_later_levels_in_order()
    {
        IReadOnlySet<string> names = SecretNames.Extend(
            new[] { Level(("X", "${env:S}")), Level(("Y", "${env:X}")) },
            Declared("S"),
            Declared());

        Assert.Equal(new[] { "S", "X", "Y" }, names.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Extend_does_not_mark_a_reference_to_a_name_that_becomes_secret_only_later()
    {
        // Y is expanded before X is set, so it holds X's earlier value, which is not secret.
        IReadOnlySet<string> names = SecretNames.Extend(
            new[] { Level(("Y", "${env:X}")), Level(("X", "${env:S}")) },
            Declared("S"),
            Declared());

        Assert.Equal(new[] { "S", "X" }, names.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Extend_judges_a_reference_in_the_same_level_against_the_names_before_that_level()
    {
        IReadOnlySet<string> names = SecretNames.Extend(
            new[] { Level(("X", "${env:S}"), ("Y", "${env:X}")) },
            Declared("S"),
            Declared());

        Assert.DoesNotContain("Y", names);
        Assert.Contains("X", names);
    }

    [Fact]
    public void Extend_adds_nothing_for_a_literal_level_or_a_removal()
    {
        var literal = new EnvLevel(new Dictionary<string, string?> { ["X"] = "${env:S}" }, "/d") { Literal = true };

        IReadOnlySet<string> names = SecretNames.Extend(new[] { literal, Level(("Z", null)) }, Declared("S"), Declared());

        Assert.Equal(new[] { "S" }, names.ToArray());
    }

    [Fact]
    public void Extend_marks_an_imported_key_that_is_secret_in_the_base()
    {
        var import = new EnvLevel(new Dictionary<string, string?> { ["IMP"] = null, ["OTHER"] = null }, "/base")
        {
            ImportFrom = new[] { Level(("IMP", "x")) },
        };

        IReadOnlySet<string> names = SecretNames.Extend(new[] { import }, Declared(), Declared("IMP"));

        Assert.Equal(new[] { "IMP" }, names.ToArray());
    }

    [Fact]
    public void A_base_derived_name_reaches_the_file_that_imports_it()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { Secret = new() { "S" }, Env = new() { ["X"] = "${env:S}/bin", ["PLAIN"] = "p" } }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel, BaseEnv = new() { "X", "PLAIN" } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        IReadOnlySet<string> names = session.SecretNamesFor(session.MainSource);

        Assert.Contains("X", names);
        Assert.DoesNotContain("PLAIN", names);
        Assert.DoesNotContain("S", names);
    }

    [Fact]
    public void A_base_derived_name_that_the_file_does_not_import_is_not_secret_in_the_file()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { Secret = new() { "S" }, Env = new() { ["X"] = "${env:S}/bin" } }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel, BaseEnv = new() { "OTHER" } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        Assert.DoesNotContain("X", session.SecretNamesFor(session.MainSource));
        Assert.Contains("X", session.SecretNamesFor(session.Sources[0]));
    }

    [Fact]
    public void A_group_derives_from_a_file_level_secret_and_a_command_from_a_group_secret()
    {
        using var dir = new TempDir();
        ConfigStore.Save(
            new CmdrConfig
            {
                Secret = new() { "FILE_S" },
                Groups =
                {
                    new GroupSpec
                    {
                        Name = "g",
                        Secret = new() { "GROUP_S" },
                        Env = new() { ["FROM_FILE"] = "${env:FILE_S}", ["PLAIN"] = "p" },
                        Items = { new CommandSpec { Name = "run", Program = "tool", Env = new() { ["FROM_GROUP"] = "${env:GROUP_S}" } } },
                    },
                },
            },
            dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);
        GroupEntry group = session.AllGroups.Single();
        CommandEntry run = session.AllEntries.Single();

        Assert.Contains("FROM_FILE", session.SecretNamesFor(group));
        Assert.DoesNotContain("PLAIN", session.SecretNamesFor(group));
        Assert.Contains("FROM_FILE", session.SecretNamesFor(run));
        Assert.Contains("FROM_GROUP", session.SecretNamesFor(run));
        Assert.DoesNotContain("FROM_GROUP", session.SecretNamesFor(group));
    }
}
