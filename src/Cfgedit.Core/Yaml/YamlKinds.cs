using Cfgedit.Core.Model;

namespace Cfgedit.Core.Yaml;

/// <summary>
/// The kind of a YAML scalar. A plain scalar with no tag has its YAML 1.2 core-schema kind. A quoted or block scalar with no tag is a
/// string. A core tag gives its type: the value must read as that type, or the text is refused (a <c>!!int</c> that is not an integer is a
/// broken document, not a string). Any other tag, and the non-specific tag <c>!</c>, gives a string.
/// </summary>
internal static class YamlKinds
{
    /// <summary>The kind of a scalar with the given style, resolved tag and decoded value.</summary>
    /// <param name="plain">True for a plain scalar.</param>
    /// <param name="tag">The resolved tag, or null.</param>
    /// <param name="value">The decoded value.</param>
    /// <exception cref="CfgeditException">The value does not read as the type that its core tag names.</exception>
    internal static YamlValueKind KindOf(bool plain, string? tag, string value)
    {
        if (tag is null)
        {
            return plain ? FromCore(YamlEncoder.CoreSchemaKind(value)) : YamlValueKind.String;
        }

        switch (tag)
        {
            case YamlTags.CorePrefix + "str":
                return YamlValueKind.String;
            case YamlTags.CorePrefix + "null":
                return Require(value, tag, YamlCoreKind.Null, YamlValueKind.Null);
            case YamlTags.CorePrefix + "bool":
                return Require(value, tag, YamlCoreKind.Boolean, YamlValueKind.Boolean);
            case YamlTags.CorePrefix + "int":
                return Require(value, tag, YamlCoreKind.Integer, YamlValueKind.Integer);
            case YamlTags.CorePrefix + "float":
                if (YamlEncoder.CoreSchemaKind(value) is YamlCoreKind.Integer or YamlCoreKind.Float)
                {
                    return YamlValueKind.Float;
                }

                throw Broken(value, tag);
            default:
                return YamlValueKind.String;
        }
    }

    private static YamlValueKind Require(string value, string tag, YamlCoreKind expected, YamlValueKind kind)
    {
        if (YamlEncoder.CoreSchemaKind(value) == expected)
        {
            return kind;
        }

        throw Broken(value, tag);
    }

    /// <summary>The error for a value that does not read as its core tag's type. The tag is named by its shorthand, such as <c>!!int</c>.</summary>
    private static CfgeditException Broken(string value, string tag)
    {
        string shorthand = "!!" + tag[YamlTags.CorePrefix.Length..];
        return new CfgeditException($"the value {DisplayText.Quote(value)} does not read as {shorthand}");
    }

    private static YamlValueKind FromCore(YamlCoreKind kind)
    {
        return kind switch
        {
            YamlCoreKind.Null => YamlValueKind.Null,
            YamlCoreKind.Boolean => YamlValueKind.Boolean,
            YamlCoreKind.Integer => YamlValueKind.Integer,
            YamlCoreKind.Float => YamlValueKind.Float,
            _ => YamlValueKind.String,
        };
    }
}
