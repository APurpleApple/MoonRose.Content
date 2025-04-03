using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace MoonRose.Content;

internal static class Generator
{
    public static SourceText GenerateDBClass(ImmutableArray<ContentClassToGenerate?> classEnumGeneration)
    {
        IEnumerable<ContentClassToGenerate> contentClasses = classEnumGeneration.Where(c => c.HasValue).Select(c=>c.Value);
        List<ContentClassToGenerate> sortedByDependency = Utils.TopologicalSort(contentClasses.Select(c=> (c, c.KeyClassName, c.ContentTypeDependencies)));

        CodeBuilder source = new();

        foreach (var item in contentClasses)
        {
            source = source.AppendLine($"// {item.KeyClassName} Dependencies:");
            foreach (var dep in item.ContentTypeDependencies)
            {
                source = source.AppendLine($"     //{dep}");
            }
        }

        HashSet<string> usings = [];
        usings.Add("System");
        usings.Add("MoonWorks.Storage");
        usings.Add("MoonWorks");
        usings.Add("MoonWorks.Graphics");
        usings.Add("SDL3");
        usings.Add("System.Runtime.InteropServices");
        usings.Add("System.Collections.Generic");
        usings.Add("System.IO");
        usings.Add("System.Diagnostics");
        foreach (var item in contentClasses)
        {
            usings.Add(item.LoaderNameSpace);
        }

        source = AddUsingDeclarations(source, usings.ToArray());

        //source = source.AppendLine($"namespace MoonRose.Content;").AppendBlankLine();


        using (source.StartBlock($"public static class DB"))
        {
            foreach (var item in contentClasses.Where(i=>i.GenerateStorage))
            {
                source.AppendLine($"public static {item.StorageClassName} {item.StorageName} = new();");
            }
            source.AppendBlankLine();

            source.AppendLine($"private static TitleStorage Storage;");
            source.AppendLine($"private static GraphicsDevice GraphicsDevice;");
            source.AppendLine($"public static Action OnReload;");
            source.AppendLine($"private static HashSet<string> immediateLoad = [];");
            source.AppendLine($"private static HashSet<string> validExtensions = [];");
            source.AppendBlankLine();

            source.AppendLine($"public static int LoadQueueSize {{get; private set;}} = 0;");
            source.AppendLine($"public static int LoadQueueProgress {{get; private set;}} = 0;");

            using (source.StartBlock($"public static void Init(TitleStorage titleStorage, GraphicsDevice graphicsDevice)"))
            {
                source.AppendLine($"Storage = titleStorage;");
                source.AppendLine($"GraphicsDevice = graphicsDevice;");
                foreach (var item in contentClasses)
                {
                    if (item.Extension != "")
                    {
                        source.AppendLine($"validExtensions.Add(\"{item.Extension}\");");
                    }
                }
            }

            using (source.StartBlock($"public static unsafe void LoadAssets(string relativePath)"))
            {
                source.AppendLine($"Stopwatch sw = Stopwatch.StartNew();");
                source.AppendLine($"Dictionary<string, List<string>> assets = [];");
                source.AppendLine($"assets.Add(\"immediate\", []);");
                using (source.StartBlock($"foreach(string ext in validExtensions)"))
                {
                    source.AppendLine($"assets.Add(ext, []);");
                }
                source.AppendLine($"ParseDirectories(relativePath, assets);");
                source.AppendBlankLine();

                source.AppendLine($"LoadQueueSize = 0;");
                source.AppendLine($"LoadQueueProgress = 0;");
                using (source.StartBlock($"foreach(var assetList in assets)"))
                {
                    source.AppendLine($"LoadQueueSize += assetList.Value.Count;");
                }
                source.AppendBlankLine();


                using (source.StartBlock($"foreach(var item in assets[\"immediate\"])"))
                {
                    source.AppendLine($"LoadQueueProgress ++;");
                }
                source.AppendBlankLine();

                foreach (var item in sortedByDependency)
                {
                    if (item.HasLoadFromPathMethod)
                    {
                        using (source.StartBlock($"foreach(var item in assets[\"{item.Extension}\"])"))
                        {
                            source.AppendLine($"{item.LoaderName}.LoadFromPath(Storage, GraphicsDevice, item);");
                            source.AppendLine($"LoadQueueProgress ++;");
                        }
                    }
                    if (item.HasOnDBLoadMethod)
                    {
                        source.AppendLine($"{item.LoaderName}.OnDBLoad(Storage, GraphicsDevice);");
                    }
                }
                source.AppendLine($"sw.Stop();");
                source.AppendLine($"Logger.LogInfo($\"Loaded {{LoadQueueSize}} assets in {{sw.ElapsedMilliseconds}} milliseconds.\");");
            }

            using (source.StartBlock($"private static unsafe void ParseDirectories(string relativePath, Dictionary<string, List<string>> assets)"))
            {
                foreach (var item in contentClasses)
                {
                    if (item.Extension != "" && item.TakesOverDirectory)
                    {
                        using (source.StartBlock($"if(Storage.Exists(relativePath + \"/{item.Extension}\"))"))
                        {
                            source.AppendLine($"assets[\"{item.Extension}\"].Add(relativePath);");
                            source.AppendLine($"return;");
                        }
                    }
                }
                source.AppendLine($"SDL.SDL_EnumerateStorageDirectory(Storage.Handle, relativePath, ParseInDirectory, (nint)(&assets));");
            }

            using (source.StartBlock($"private static unsafe SDL.SDL_EnumerationResult ParseInDirectory(nint assetListPtr, byte* dirname, byte* fname)"))
            {
                source.AppendLine($"string fileName = Marshal.PtrToStringAnsi((nint)fname);");
                source.AppendLine($"string dirName = Marshal.PtrToStringAnsi((nint)dirname);");
                source.AppendLine($"Dictionary<string, List<string>> assets = *(Dictionary<string, List<string>>*)assetListPtr;");

                source.AppendBlankLine();

                using (source.StartBlock($"if(!Path.HasExtension(fileName))"))
                {
                    source.AppendLine($"ParseDirectories(dirName + fileName, assets);");
                }
                using (source.StartBlock($"else"))
                {
                    source.AppendLine($"string ext = Path.GetExtension(fileName);");
                    source.AppendLine($"string path = dirName + fileName;");

                    source.AppendLine($"if(!validExtensions.Contains(ext)) return SDL.SDL_EnumerationResult.SDL_ENUM_CONTINUE;");
                    using (source.StartBlock($"if(immediateLoad.Contains(fileName))"))
                    {
                        source.AppendLine($"assets[\"immediate\"].Add(path);");
                    }
                    using (source.StartBlock($"else"))
                    {
                        source.AppendLine($"assets[ext].Add(path);");
                    }
                }

                source.AppendLine($"return SDL.SDL_EnumerationResult.SDL_ENUM_CONTINUE;");
            }

            using (source.StartBlock($"public static void HotReload()"))
            {
                //foreach (var item in contentClasses)
                //{
                //    source.AppendLine($"{item.StorageName}.Clear();");
                //}
            }
        }
        return source.Text;
    }

    public static SourceText GenerateContentAttribute()
    {
        CodeBuilder source = new();

        source = AddUsingDeclarations(source,
            "System"
            );

        source = source.AppendLine($"namespace MoonRose.Content;").AppendBlankLine();

        source = source.AppendLine($"[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]");
        using (source.StartBlock($"public class ContentAttribute(string fileExtension = \"\", bool takeOverDirectory = false, bool generateStorage = true) : Attribute"))
        {

        }
        source = source.AppendBlankLine();
        source = source.AppendLine($"[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]");
        using (source.StartBlock($"public class ContentLoaderAttribute<T>(string fileExtension = \"\", bool takeOverDirectory = false, bool generateStorage = true) : Attribute"))
        {

        }
        return source.Text;
    }

    public static string GenerateKeyStruct(in ContentClassToGenerate classDeclaration, out CodeBuilder source)
    {
        string keyClassName = classDeclaration.Name + "Key";
        string storageClassName = classDeclaration.Name + "Storage";
        string storageDBName = classDeclaration.Name + "s";

        source = AddUsingDeclarations(new(), 
            classDeclaration.Namespace,
            "System.Text.Json", 
            "System", 
            "System.Text.Json.Serialization"
            );

        //source = source.AppendLine($"namespace MoonRose.Content;").AppendBlankLine();

        source = source.AppendLine($"[JsonConverter(typeof({keyClassName}JsonConverter))]");
        using (source.StartBlock($"public readonly record struct {keyClassName}(int ID)"))
        {
            source.AppendLine($"public {classDeclaration.Name} Fetch() => DB.{storageDBName}.Get(this);");
            source.AppendLine($"public static {storageClassName} Storage => DB.{storageDBName};");
            source.AppendLine($"public static implicit operator {classDeclaration.Name}({keyClassName} k) => DB.{storageDBName}.Get(k);");

            source.AppendBlankLine();

            using (source.StartBlock($"private class {keyClassName}JsonConverter : JsonConverter<{keyClassName}>"))
            {
                using (source.StartBlock($"public override void Write(Utf8JsonWriter writer, {keyClassName} value, JsonSerializerOptions options)"))
                {
                    source.AppendLine($"writer.WriteStringValue({keyClassName}.Storage.GetName(value));");
                }

                using (source.StartBlock($"public override {keyClassName} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)"))
                {
                    source.AppendLine($"return {keyClassName}.Storage.LookupKey(reader.GetString());");
                }
            }
        }

        return keyClassName;
    }

    public static string GenerateContentStorage(in ContentClassToGenerate classDeclaration, out CodeBuilder source)
    {
        string storageClassName = classDeclaration.Name + "Storage";
        string keyClassName = classDeclaration.Name + "Key";
        string storageDBName = classDeclaration.Name + "s";

        source = AddUsingDeclarations(new(),
            classDeclaration.Namespace,
            "System",
            "System.Collections.Generic",
            "MoonWorks"
            );

        //source = source.AppendLine($"namespace MoonRose.Content;").AppendBlankLine();
        using (source.StartBlock($"public class {storageClassName}"))
        {
            source.AppendLine($"readonly Dictionary<string, {classDeclaration.Name}> lookup = [];");
            source.AppendLine($"readonly Dictionary<int, string> IDToName = [];");
            source.AppendLine($"readonly Dictionary<string, int> NameToID = [];");
            source.AppendLine($"{classDeclaration.Name}[] storage = [];");
            source.AppendLine($"int nextID = 0;");
            source.AppendLine($"//{classDeclaration.Debug}");

            using (source.StartBlock($"public {keyClassName} LookupKey(string name)"))
            {
                source.AppendLine($"return new {keyClassName}(NameToID[name]);");
            }

            using (source.StartBlock($"public {classDeclaration.Name} Lookup(string name)"))
            {
                source.AppendLine($"return lookup[name];");
            }

            using (source.StartBlock($"public {classDeclaration.Name} Get({keyClassName} key)"))
            {
                source.AppendLine($"return storage[key.ID];");
            }

            using (source.StartBlock($"public string GetName({keyClassName} key)"))
            {
                source.AppendLine($"return IDToName[key.ID];");
            }

            using (source.StartBlock($"public {keyClassName} Register({classDeclaration.Name} thing, string name)"))
            {
                using (source.StartBlock($"if (NameToID.TryGetValue(name, out var existingID))"))
                {
                    source.AppendLine($"Logger.LogWarn($\"Overriding {classDeclaration.Name} {{name}} with new value!\");");
                    if (classDeclaration.Disposable)
                    {
                        source.AppendLine($"storage[existingID].Dispose();");
                    }
                    source.AppendLine($"lookup[name] = thing;");
                    source.AppendLine($"storage[existingID] = thing;");
                    source.AppendLine($"return new {keyClassName}(existingID);");
                }

                using (source.StartBlock($"if (nextID >= storage.Length)"))
                {
                    source.AppendLine($"Array.Resize(ref storage, (int)((nextID+1) * 2));");
                }

                source.AppendLine($"lookup.Add(name, thing);");
                source.AppendLine($"NameToID.Add(name, nextID);");
                source.AppendLine($"IDToName.Add(nextID, name);");
                source.AppendLine($"storage[nextID] = thing;");
                source.AppendLine($"var result = new {keyClassName}(nextID);");

                source.AppendLine($"nextID ++;");
                source.AppendLine($"return result;");
            }

            using (source.StartBlock($"public void Clear()"))
            {
                source.AppendLine($"lookup.Clear();");
                source.AppendLine($"IDToName.Clear();");
                source.AppendLine($"NameToID.Clear();");
                if (classDeclaration.Disposable)
                {
                    using (source.StartBlock($"foreach (var item in storage)"))
                    {
                        source.AppendLine($"item.Dispose();");
                    }
                }
                source.AppendLine($"storage = [];");
                source.AppendLine($"nextID = 0;");
            }
        }

        return storageClassName;
    }

    private static CodeBuilder AddUsingDeclarations(CodeBuilder source)
        => AddUsingDeclarations(source: source, "System", "System.CodeDom.Compiler", "System.Collections.Generic");

    private static CodeBuilder AddUsingDeclarations(CodeBuilder source, params string[] namespaces)
    {
        return namespaces
            .OrderBy(keySelector: n => n, comparer: StringComparer.OrdinalIgnoreCase)
            .Aggregate(seed: source, func: (current, ns) => current.AppendLine($"using {ns};"))
            .AppendBlankLine();
    }

    private static string ClassWithNamespaceFormatter(ContentClassToGenerate d)
        => d.Namespace == "<global namespace>" ? d.Name : $"{d.Namespace}.{d.Name}";

    private static void GenerateAccessors(CodeBuilder source, in ContentClassToGenerate attribute, Func<ContentClassToGenerate, string> classNameFormatter, int index)
    {
        string className = classNameFormatter(attribute);
    }

    private static string ConvertAccessType(AccessType accessType)
        => accessType switch
        {
            AccessType.PUBLIC => "public",
            AccessType.PRIVATE => "private",
            AccessType.PROTECTED => "protected",
            AccessType.PROTECTED_INTERNAL => "protected internal",
            AccessType.INTERNAL => "internal",
            _ => throw new ArgumentOutOfRangeException(nameof(accessType), actualValue: accessType, message: "Unknown access type")
        };
}
