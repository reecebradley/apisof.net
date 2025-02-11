﻿namespace Terrajobst.ApiCatalog;

public readonly struct ApiDeclarationModel : IEquatable<ApiDeclarationModel>
{
    private readonly ApiModel _api;
    private readonly int _offset;

    internal ApiDeclarationModel(ApiModel api, int offset)
    {
        _api = api;
        _offset = offset;
    }

    public ApiCatalogModel Catalog => _api.Catalog;

    public ApiModel Api => _api;

    public AssemblyModel Assembly
    {
        get
        {
            var assemblyOffset = _api.Catalog.ApiTable.ReadInt32(_offset);
            return new AssemblyModel(_api.Catalog, assemblyOffset);
        }
    }

    public ObsoletionModel? Obsoletion
    {
        get
        {
            return _api.Catalog.GetObsoletion(_api.Id, Assembly.Id);
        }
    }

    public IEnumerable<PlatformSupportModel> PlatformSupport
    {
        get
        {
            return _api.Catalog.GetPlatformSupport(_api.Id, Assembly.Id);
        }
    }

    public PreviewRequirementModel? PreviewRequirement
    {
        get
        {
            return _api.Catalog.GetPreviewRequirement(_api.Id, Assembly.Id);
        }
    }

    public Markup GetMyMarkup()
    {
        var markupOffset = _api.Catalog.ApiTable.ReadInt32(_offset + 4);
        return _api.Catalog.GetMarkup(markupOffset);
    }

    public Markup GetMarkup()
    {
        var assembly = Assembly;
        var markups = Api.AncestorsAndSelf()
                         .Select(a => a.Declarations.Single(d => d.Assembly == assembly))
                         .Select(d => d.GetMyMarkup())
                         .ToList();
        markups.Reverse();

        var parts = new List<MarkupPart>();

        var indent = 0;

        foreach (var markup in markups)
        {
            if (indent > 0)
            {
                if (indent - 1 > 0)
                    parts.Add(new MarkupPart(MarkupPartKind.Whitespace, new string(' ', 4 * (indent - 1))));
                parts.Add(new MarkupPart(MarkupPartKind.Punctuation, "{"));
                parts.Add(new MarkupPart(MarkupPartKind.Whitespace, Environment.NewLine));
            }

            var needsIndent = true;

            foreach (var part in markup.Parts)
            {
                if (needsIndent)
                {
                    // Add indentation
                    parts.Add(new MarkupPart(MarkupPartKind.Whitespace, new string(' ', 4 * indent)));
                    needsIndent = false;
                }

                parts.Add(part);

                if (part.Kind == MarkupPartKind.Whitespace && part.Text is "\n" or "\r" or "\r\n")
                    needsIndent = true;
            }

            parts.Add(new MarkupPart(MarkupPartKind.Whitespace, Environment.NewLine));

            indent++;
        }

        for (var i = markups.Count - 1 - 1; i >= 0; i--)
        {
            if (i > 0)
                parts.Add(new MarkupPart(MarkupPartKind.Whitespace, new string(' ', 4 * i)));
            parts.Add(new MarkupPart(MarkupPartKind.Punctuation, "}"));
            parts.Add(new MarkupPart(MarkupPartKind.Whitespace, Environment.NewLine));
        }

        return new Markup(parts);
    }

    public bool IsOverride()
    {
        var api = Api;

        // Finalizers are overrides

        if (api.Kind == ApiKind.Destructor)
            return true;

        var canBeOverriden = api.Kind is ApiKind.Property or
                                         ApiKind.PropertyGetter or
                                         ApiKind.PropertySetter or
                                         ApiKind.Method or
                                         ApiKind.Event or
                                         ApiKind.EventAdder or
                                         ApiKind.EventRemover or
                                         ApiKind.EventRaiser;

        if (canBeOverriden)
        {
            var markup = GetMyMarkup();
            return markup.Parts.Any(p => p.Kind == MarkupPartKind.Keyword &&
                                         p.Text == "override");
        }

        return false;
    }

    public override bool Equals(object obj)
    {
        return obj is ApiDeclarationModel model && Equals(model);
    }

    public bool Equals(ApiDeclarationModel other)
    {
        return _api == other._api &&
               _offset == other._offset;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_api, _offset);
    }

    public static bool operator ==(ApiDeclarationModel left, ApiDeclarationModel right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ApiDeclarationModel left, ApiDeclarationModel right)
    {
        return !(left == right);
    }
}