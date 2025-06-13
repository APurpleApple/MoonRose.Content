using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Diagnostics;

namespace MoonRose.Content;

[DebuggerDisplay("{AccessType} {Namespace}.{Name}")]
internal readonly record struct ContentClassToGenerate
{
    public ContentClassToGenerate(string name, string loaderName, string @namespace, string loaderNameSpace, Location location, bool disposable, bool hasFileLoadMethod, bool hasOnLoadMethod, AttributeData attribute, List<string> dependencies, string debug = "")
    {
        this.Name = name;
        this.LoaderName = loaderName;
        this.Namespace = @namespace;
        this.Location = location;
        this.Disposable = disposable;
        this.HasLoadFromPathMethod = hasFileLoadMethod;
        this.HasOnDBLoadMethod = hasOnLoadMethod;
        this.Attribute = attribute;
        this.ContentTypeDependencies = dependencies;
        Debug = debug;
        LoaderNameSpace = loaderNameSpace;
    }

    public string KeyClassName => GenerateStorage ? Name + "Key" : LoaderName + "Key";
    public string StorageName => Name + "s";
    public string StorageClassName => Name + "Storage";
    public string Extension => (string)Attribute.ConstructorArguments[0].Value;
    public bool TakesOverDirectory => (bool)Attribute.ConstructorArguments[1].Value;
    public bool GenerateStorage => (bool)Attribute.ConstructorArguments[2].Value;
    public bool NewtonsoftSerializer => (bool)Attribute.ConstructorArguments[3].Value;
    public string Debug { get; }
    public List<string> ContentTypeDependencies { get; }
    public string Name { get; }
    public string LoaderName { get; }
    public string LoaderNameSpace { get; }
    public string Namespace { get; }
    public Location Location { get; }
    public AttributeData Attribute { get; }

    public bool Disposable { get; }
    public bool HasLoadFromPathMethod { get; }
    public bool HasOnDBLoadMethod { get; }
}
